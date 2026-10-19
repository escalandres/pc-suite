import { useCallback, useEffect, useState, useRef } from 'react';
import {
  ArrowUpCircle,
  Clock,
  Copy,
  Download,
  FileText,
  Folder,
  FolderSearch,
  HardDrive,
  ListChecks,
  RefreshCw,
  ShieldAlert,
  Zap,
  MousePointer2,
  Keyboard,
} from 'lucide-react';
import { bridge, BridgeError } from '../services/pcsuiteBridge';
import { OperationIds, EventIds } from '../types/bridge';
import type {
  ClamAvHistoryEntryResult,
  ClamAvHistoryResult,
  ClamAvMutationResult,
  ClamAvScanResult,
  ClamAvScanStartResult,
  ClamAvStatusResult,
  ClamAvFreshclamProgressPayload,
  ClamAvScanProgressPayload,
  JobEventPayload,
} from '../types/bridge';

// Timeouts del lado del cliente, siempre por encima de los del nativo
// (lecturas 30 s, install/update 900 s). Los ESCANEOS son jobs: la llamada
// inicial responde {jobId} al instante —" ya no hay promesa retenida durante
// horas (el desenlace llega por job.event y se puede cancelar con job.cancel).
const READ_TIMEOUT_MS = 45_000;
const MUTATION_TIMEOUT_MS = 930_000;

type ScanKind = 'quick' | 'full' | 'custom';
type ActionKind = ScanKind | 'install' | 'update';

interface Notice {
  kind: 'success' | 'error';
  text: string;
}

function errorMessage(error: unknown, fallback: string): string {
  return error instanceof BridgeError ? error.message : fallback;
}

const SCAN_LABELS: Record<ScanKind, string> = {
  quick: 'Escaneo rápido',
  full: 'Escaneo completo',
  custom: 'Escaneo personalizado',
};

// Instalación automática: UN solo intento por sesión de app. Si el usuario
// cancela el UAC o winget falla, no se re-lanza en cada visita a la página;
// el botón manual queda como reintento explícito.
let autoInstallAttempted = false;

/** Formatea ms como mm:ss (o h:mm:ss si supera la hora) —" cronómetro y
 * duración del panel de ejecución (duration del backend llega en segundos). */
function formatElapsed(totalMs: number): string {
  const totalSeconds = Math.max(0, Math.floor(totalMs / 1000));
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;
  const mm = String(minutes).padStart(2, '0');
  const ss = String(seconds).padStart(2, '0');
  return hours > 0 ? `${hours}:${mm}:${ss}` : `${mm}:${ss}`;
}

export function Clamav() {
  const [status, setStatus] = useState<ClamAvStatusResult | null>(null);
  const [history, setHistory] = useState<ClamAvHistoryEntryResult[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);

  const [busy, setBusy] = useState<ActionKind | null>(null);
  const [notice, setNotice] = useState<Notice | null>(null);
  const [lastScan, setLastScan] = useState<{
    kind: ScanKind;
    result: ClamAvScanResult;
  } | null>(null);
  const [customPath, setCustomPath] = useState('');
  const customPathInputRef = useRef<HTMLInputElement>(null);

  // Progreso de actualización de definiciones (freshclam)
  const [freshclamProgress, setFreshclamProgress] = useState<ClamAvFreshclamProgressPayload | null>(null);

  // Progreso de escaneo (clamscan)
  const [scanProgress, setScanProgress] = useState<ClamAvScanProgressPayload | null>(null);

  // Job del escaneo en curso: {jobId} al iniciar (la promesa ya no se
  // retiene); el desenlace llega por job.event y se cancela con job.cancel.
  const [scanJobId, setScanJobId] = useState<string | null>(null);

  // Kind del escaneo en curso (para el desenlace del job).
  const scanKindRef = useRef<ScanKind | null>(null);

  // Cronómetro local del panel de ejecución: mide desde el inicio de
  // runScan (no requiere backend; un full scan puede durar horas y sin
  // reloj el usuario no sabe cuánto lleva).
  const [scanElapsedMs, setScanElapsedMs] = useState(0);
  const scanStartedAtRef = useRef<number | null>(null);

  const loadHistory = useCallback(async () => {
    try {
      const result = await bridge.call<ClamAvHistoryResult>(
        OperationIds.clamavHistory,
        undefined,
        READ_TIMEOUT_MS,
      );
      setHistory(result.entries ?? []);
    } catch {
      // Best-effort: un fallo de lectura del historial no bloquea la página.
      setHistory([]);
    }
  }, []);

  const refresh = useCallback(async () => {
    setLoading(true);
    setLoadError(null);
    try {
      const statusResult = await bridge.call<ClamAvStatusResult>(
        OperationIds.clamavStatus,
        undefined,
        READ_TIMEOUT_MS,
      );
      setStatus(statusResult);
      if (statusResult.installed) {
        await loadHistory();
      } else {
        setHistory([]);
      }
    } catch (error) {
      setLoadError(errorMessage(error, 'No se pudo consultar ClamAV.'));
    } finally {
      setLoading(false);
    }
  }, [loadHistory]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  // Escuchar eventos de progreso de freshclam
  useEffect(() => {
    const unsub = bridge.on<ClamAvFreshclamProgressPayload>(
      EventIds.clamavFreshclamProgress,
      (payload) => {
        setFreshclamProgress(payload);
        // Limpiar progreso después de completado (con pequeño delay para que se vea el 100%)
        if (payload.isComplete) {
          setTimeout(() => setFreshclamProgress(null), 3000);
        }
      }
    );
    return () => unsub();
  }, []);

  // Escuchar eventos de progreso de escaneo (clamscan)
  useEffect(() => {
    const unsub = bridge.on<ClamAvScanProgressPayload>(
      EventIds.clamavScanProgress,
      (payload) => {
        setScanProgress(payload);
        // Limpiar progreso después de completado
        if (payload.isComplete) {
          setTimeout(() => setScanProgress(null), 3000);
        }
      },
    );
    return () => unsub();
  }, []);

  // Reloj del panel de ejecución: hace tick mientras dura el escaneo
  useEffect(() => {
    const isScanning = busy === 'quick' || busy === 'full' || busy === 'custom';
    if (!isScanning || scanStartedAtRef.current === null) return;
    const tick = () => setScanElapsedMs(Date.now() - (scanStartedAtRef.current ?? 0));
    tick();
    const timer = window.setInterval(tick, 500);
    return () => window.clearInterval(timer);
  }, [busy]);

  const runMutation = async (
    kind: ActionKind,
    operation: string,
    successText: string,
  ) => {
    setBusy(kind);
    setNotice(null);
    if (kind === 'update') {
      setFreshclamProgress(null); // Resetear progreso al iniciar actualización
    }
    try {
      // Payload cero: .NET construye el argv fijo (install delega en winget).
      const result = await bridge.call<ClamAvMutationResult>(
        operation,
        undefined,
        MUTATION_TIMEOUT_MS,
      );
      if (result.succeeded) {
        setNotice({
          kind: 'success',
          text: result.elevated
            ? `${result.detail ?? successText} (elevado)`
            : (result.detail ?? successText),
        });
        await refresh();
      } else {
        setNotice({
          kind: 'error',
          text: result.detail ?? `El proceso terminó con el código ${result.exitCode}.`,
        });
      }
    } catch (error) {
      setNotice({ kind: 'error', text: errorMessage(error, 'La operación falló.') });
    } finally {
      setBusy(null);
    }
  };

  // Desenlace del job de escaneo: el resultado completo del escaneo llega
  // en el evento job.event (statusChanged). Filtra por el job activo.
  useEffect(() => {
    const unsub = bridge.on<JobEventPayload>(EventIds.jobEvent, (payload) => {
      if (payload.jobId !== scanJobId || payload.type !== 'statusChanged') return;

      const kind = scanKindRef.current;
      setScanJobId(null);
      setBusy(null);
      scanStartedAtRef.current = null;

      if (payload.status === 'cancelled') {
        setNotice({ kind: 'error', text: 'Escaneo cancelado.' });
        return;
      }

      if (payload.status === 'failed') {
        setNotice({ kind: 'error', text: payload.error ?? 'El escaneo falló.' });
        return;
      }

      const result = (payload.result as ClamAvScanResult | null) ?? null;
      if (result && kind) {
        setLastScan({ kind, result });
        if (result.succeeded) {
          const durationText = result.duration ? ` (${formatElapsed(result.duration * 1000)})` : '';
          setNotice({
            kind: 'success',
            text:
              result.detail ??
              (result.infectedFiles > 0
                ? `¡Amenazas encontradas! ${result.infectedFiles} archivo(s) infectado(s).${durationText}`
                : `Escaneo completado${durationText}: no se detectaron amenazas.`),
          });
          void loadHistory();
        } else {
          setNotice({
            kind: 'error',
            text: result.detail ?? `clamscan terminó con el código ${result.exitCode}.`,
          });
        }
      }
    });
    return () => unsub();
  }, [scanJobId, loadHistory]);

  /** Cancela el escaneo en curso (job.cancel mata clamscan). */
  const cancelScan = async () => {
    if (!scanJobId) return;
    try {
      await bridge.call(OperationIds.jobCancel, { jobId: scanJobId }, READ_TIMEOUT_MS);
      // El desenlace (estado cancelled) llega por job.event.
    } catch (error) {
      setNotice({ kind: 'error', text: errorMessage(error, 'No se pudo cancelar el escaneo.') });
    }
  };

  const runScan = async (kind: ScanKind, operation: string, payload?: unknown) => {
    setBusy(kind);
    setNotice(null);
    setScanProgress(null); // Resetear progreso al iniciar escaneo
    // El cronómetro del panel de ejecución arranca aquí
    scanStartedAtRef.current = Date.now();
    scanKindRef.current = kind;
    setScanElapsedMs(0);
    try {
      // Contrato job: responde {jobId} AL INSTANTE —" el desenlace llega por
      // job.event y el escaneo se puede cancelar con el botón Cancelar.
      const start = await bridge.call<ClamAvScanStartResult>(operation, payload);
      setScanJobId(start.jobId);
    } catch (error) {
      setNotice({ kind: 'error', text: errorMessage(error, 'El escaneo falló.') });
      setBusy(null);
      scanStartedAtRef.current = null;
    }
  };

  /** Instala ClamAV vía winget (la operación nativa eleva con UAC puntual). */
  const runInstall = () => {
    void runMutation('install', OperationIds.clamavInstall, 'ClamAV se instaló correctamente.');
  };

  // Si ClamAV no está en la máquina, se instala automáticamente al entrar
  // en la página (una vez por sesión). El consentimiento lo da Windows con
  // el UAC de la operación; si se cancela, queda el botón manual.
  useEffect(() => {
    if (loading || !status || status.installed || busy || autoInstallAttempted) {
      return;
    }
    autoInstallAttempted = true;
    runInstall();
    // eslint-disable-next-line react-hooks/exhaustive-deps -- runInstall es estable por sesión
  }, [loading, status, busy]);

  const runCustomScan = () => {
    const target = customPath.trim();
    if (!target) return;
    // La validación (absoluta/existente/sin NUL) es de .NET: aquí solo
    // evitamos la llamada vacía.
    void runScan('custom', OperationIds.clamavCustomScan, {
      path: target,
    });
  };

  // 14.3.5 —" Atajos de teclado para acciones principales (Q=quick, F=full, C=custom, D=definiciones)
  useEffect(() => {
    if (loading || !status?.installed) return;
    const onKeyDown = (event: KeyboardEvent) => {
      // Ignorar si el foco está en un input/textarea
      const target = event.target as HTMLElement;
      if (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA' || target.isContentEditable) return;

      if (event.ctrlKey || event.metaKey || event.altKey) return; // Solo teclas simples

      switch (event.key.toLowerCase()) {
        case 'q':
          if (!busy) void runScan('quick', OperationIds.clamavQuickScan);
          break;
        case 'f':
          if (!busy) {
            if (window.confirm('¿Ejecutar un escaneo completo de todos los discos? Puede tardar varias horas y consumir mucha CPU.')) {
              void runScan('full', OperationIds.clamavFullScan);
            }
          }
          break;
        case 'c':
          if (!busy && customPath.trim()) runCustomScan();
          break;
        case 'd':
          if (!busy && status?.canUpdateDefinitions) {
            if (window.confirm('⚠️ actualizar definiciones de ClamAV requiere permisos de administrador (UAC). Se abrirá una ventana de credenciales de administrador. ¿Desea continuar?')) {
              void runMutation('update', OperationIds.clamavUpdateDefinitions, 'Definiciones actualizadas.');
            }
          }
          break;
        case 'r':
          if (!busy) void refresh();
          break;
      }
    };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [loading, status, busy, customPath, refresh, runScan, runMutation, runCustomScan]);

  const installed = status?.installed ?? false;
  const busyAny = busy !== null;

  return (
    <div className="space-y-6">
      <header className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold">ClamAV</h1>
          <p className="mt-1 text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
            Antivirus de código abierto: estado, instalación, definiciones, escaneos e
            historial —" solo operaciones fijas, nunca argumentos arbitrarios.
          </p>
        </div>
        <div className="flex shrink-0 items-center gap-2">
          <button
            type="button"
            onClick={() => {
              void bridge.call(OperationIds.blazorToggle, undefined, READ_TIMEOUT_MS).catch(() => {
                // Sin host nativo (dev en navegador): sin feedback.
              });
            }}
            title="Piloto Blazor (Task 015.2: bloqueado — el componente no renderiza)"
            className="flex items-center gap-1.5 rounded-md border px-2.5 py-1.5 text-xs font-medium transition-colors cursor-pointer hover:bg-(--pcs-surface-2)"
            style={{ borderColor: 'var(--pcs-border)', color: 'var(--pcs-text-muted)' }}
          >
            Ver en Blazor
          </button>
          <button
            type="button"
            onClick={() => void refresh()}
            disabled={loading || busyAny}
            className="flex items-center gap-2 rounded-md px-3 py-2 text-sm font-medium transition-colors disabled:opacity-50"
            style={{ background: 'var(--pcs-accent)', color: '#fff' }}
          >
            <RefreshCw size={14} aria-hidden className={loading ? 'animate-spin' : undefined} />
            Actualizar
          </button>
        </div>
      </header>

      {loading && (
        <p className="text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
          Consultando clamscan…
        </p>
      )}

      {loadError && (
        <div
          className="rounded-md border px-4 py-3 text-sm"
          style={{ borderColor: 'var(--pcs-danger)', color: 'var(--pcs-danger)' }}
          role="alert"
        >
          {loadError}
        </div>
      )}

      {notice && (
        <div
          className="rounded-md border px-4 py-3 text-sm"
          style={
            notice.kind === 'success'
              ? { borderColor: 'var(--pcs-success)', color: 'var(--pcs-success)' }
              : { borderColor: 'var(--pcs-danger)', color: 'var(--pcs-danger)' }
          }
          role={notice.kind === 'error' ? 'alert' : 'status'}
        >
          {notice.text}
        </div>
      )}

      {/* Estado */}
      {!loading && status && (
        <section
          className="rounded-lg border p-4"
          style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
          aria-label="Estado de ClamAV"
        >
          {!installed ? (
            <div className="space-y-3">
              <p
                className="text-sm"
                style={{ color: busy === 'install' ? 'var(--pcs-warning)' : 'var(--pcs-danger)' }}
              >
                {busy === 'install'
                  ? 'Instalando ClamAV automáticamente con winget —" Windows puede pedir permiso de administrador (UAC).'
                  : 'clamscan.exe no se encontró (PATH ni directorios comunes). Se puede instalar con winget; Windows pedirá permiso (UAC).'}
              </p>
              <ActionButton
                busy={busy === 'install'}
                disabled={busyAny}
                label={busy === 'install' ? 'Instalando…' : 'Instalar ClamAV'}
                icon={<Download size={14} aria-hidden />}
                primary
                onClick={() => {
                  if (
                    !window.confirm(
                      '¿Instalar ClamAV (paquete Cisco.ClamAV) con winget? Windows puede mostrar un control de cuentas de usuario (UAC).',
                    )
                  ) {
                    return;
                  }
                  runInstall();
                }}
              />
            </div>
          ) : (
            <div className="grid gap-x-6 gap-y-3 text-sm sm:grid-cols-2 lg:grid-cols-4">
              <StatusCell label="Motor" value={status.version ?? '—'} />
              <StatusCell label="Definiciones" value={status.definitions ?? '—'} />
              <StatusCell label="Ejecutable" value={status.path ?? '—'} mono />
              <StatusCell
                label="Actualización de BD"
                value={status.canUpdateDefinitions ? 'Disponible (freshclam)' : 'No disponible'}
              />
            </div>
          )}
        </section>
      )}

      {/* Operaciones */}
      {!loading && status && installed && (
        <section className="space-y-3" aria-label="Operaciones ClamAV">
          <div className="flex flex-wrap items-center gap-2">
            <ActionButton
              busy={busy === 'update'}
              disabled={busyAny || !status.canUpdateDefinitions}
              label={busy === 'update' ? 'Actualizando…' : 'Actualizar definiciones'}
              icon={<ArrowUpCircle size={14} aria-hidden />}
              shortcut="D"
              onClick={() => {
                  // Advertencia reforzada: la actualización de definiciones requiere
                  // permisos de administrador (UAC). Windows solicitará el consentimiento
                  // del usuario administrador antes de proceder.
                  const warning = '⚠️ actualizar definiciones de ClamAV requiere permisos de administrador (UAC). Se abrirá una ventana de credenciales de administrador. ¿Desea continuar?';
                  if (!window.confirm(warning)) {
                    return;
                  }
                  // Si el usuario acepta, se ejecuta la operación que desencadenará
                  // el prompt de UAC nativo de Windows a través del puente .NET.
                  void runMutation(
                    'update',
                    OperationIds.clamavUpdateDefinitions,
                    'Definiciones actualizadas.',
                  );
                }}
            />
            <ActionButton
              busy={busy === 'quick'}
              disabled={busyAny}
              label={busy === 'quick' ? 'Escaneando…' : 'Escaneo rápido'}
              icon={<Zap size={14} aria-hidden />}
              shortcut="Q"
              onClick={() =>
                void runScan('quick', OperationIds.clamavQuickScan)
              }
            />
            <ActionButton
              busy={busy === 'full'}
              disabled={busyAny}
              label={busy === 'full' ? 'Escaneando…' : 'Escaneo completo'}
              icon={<HardDrive size={14} aria-hidden />}
              shortcut="F"
              onClick={() => {
                if (
                  !window.confirm(
                    '¿Ejecutar un escaneo completo de todos los discos? Puede tardar varias horas y consumir mucha CPU. Windows pedirá permiso de administrador (UAC) para leer también los archivos del sistema.',
                  )
                ) {
                  return;
                }
                void runScan('full', OperationIds.clamavFullScan);
              }}
            />
            {/* Escaneo de directorio seleccionado */}
            <ActionButton
              busy={busy === 'custom'}
              disabled={busyAny || customPath.trim().length === 0}
              label={busy === 'custom' ? 'Escaneando…' : 'Escanear ruta'}
              icon={<FolderSearch size={14} aria-hidden />}
              shortcut="C"
              onClick={runCustomScan}
            />
          </div>

          {/* Ingresar ruta manualmente con drag-and-drop */}
          <div className="mt-3">
            <div className="relative">
              <input
                ref={customPathInputRef}
                type="text"
                value={customPath}
                onChange={(event) => setCustomPath(event.target.value)}
                onKeyDown={(event) => {
                  if (event.key === 'Enter') runCustomScan();
                }}
                onDragOver={(event) => {
                  event.preventDefault();
                  event.currentTarget.classList.add('ring-2', 'ring-offset-2', 'ring-[var(--pcs-accent)]');
                }}
                onDragLeave={(event) => {
                  event.currentTarget.classList.remove('ring-2', 'ring-offset-2', 'ring-[var(--pcs-accent)]');
                }}
                onDrop={(event) => {
                  event.preventDefault();
                  event.currentTarget.classList.remove('ring-2', 'ring-offset-2', 'ring-[var(--pcs-accent)]');
                  const items = event.dataTransfer.items;
                  if (items && items.length > 0) {
                    // Para directorios, usar webkitRelativePath
                    const entry = items[0].webkitGetAsEntry?.();
                    if (entry && entry.isDirectory) {
                      // No se puede obtener la ruta completa por seguridad del navegador
                      // Mostrar mensaje al usuario
                      setCustomPath(''); // Limpiar para que el usuario ingrese manualmente
                      alert('Por seguridad, los navegadores no permiten obtener la ruta completa de carpetas.\nPor favor, ingrese la ruta manualmente (ej: C:\\Users\\Usuario\\Descargas)');
                    } else if (items[0].kind === 'file') {
                      const file = items[0].getAsFile();
                      if (file) {
                        // Para archivos, tampoco hay ruta completa
                        alert('Por seguridad, los navegadores no permiten obtener la ruta completa.\nPor favor, ingrese la ruta del directorio manualmente.');
                      }
                    }
                  } else if (event.dataTransfer.files.length > 0) {
                    // Fallback para archivos
                    alert('Por seguridad, los navegadores no permiten obtener la ruta completa.\nPor favor, ingrese la ruta del directorio manualmente.');
                  }
                }}
                disabled={busyAny}
                placeholder="C:\\Descargas o directorio (arrastre aquí)"
                aria-label="Ruta a escanear"
                className="w-full max-w-xs rounded-md border px-3 py-2 text-sm outline-none sm:w-auto transition-colors"
                style={{
                  background: 'var(--pcs-surface-2)',
                  borderColor: 'var(--pcs-border)',
                  color: 'var(--pcs-text)',
                }}
              />
              <div className="absolute right-3 top-1/2 -translate-y-1/2 flex items-center gap-1.5 text-xs" style={{ color: 'var(--pcs-text-muted)', pointerEvents: 'none' }}>
                <Keyboard size={12} aria-hidden />
                <span>Enter: escanear</span>
                <MousePointer2 size={12} aria-hidden />
                <span>Arrastar carpeta</span>
              </div>
            </div>
          </div>

          {!status.canUpdateDefinitions && (
            <p className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
              freshclam.exe no está junto a clamscan.exe: la actualización de definiciones no
              está disponible.
            </p>
          )}
        </section>
      )}

      {/* Progreso de actualización de definiciones (freshclam) */}
      {(busy === 'update' || freshclamProgress) && freshclamProgress && (
        <section className="space-y-2" aria-label="Progreso de actualización de definiciones">
          <div className="flex items-center justify-between text-sm">
            <span style={{ color: 'var(--pcs-text-muted)' }}>
              {freshclamProgress.statusText ?? 'Actualizando definiciones…'}
              {freshclamProgress.currentFile && (
                <> —" <code>{freshclamProgress.currentFile}</code></>
              )}
            </span>
            {freshclamProgress.percent !== null && freshclamProgress.percent !== undefined && (
              <span className="font-mono">{freshclamProgress.percent}%</span>
            )}
          </div>
          <div className="h-2 rounded-full overflow-hidden" style={{ background: 'var(--pcs-surface-2)' }}>
            <div
              className="h-full transition-all duration-300 ease-out"
              style={{
                width: `${Math.min(100, Math.max(0, freshclamProgress.percent ?? 0))}%`,
                background: freshclamProgress.isComplete && freshclamProgress.succeeded === false
                  ? 'var(--pcs-danger)'
                  : 'var(--pcs-accent)',
              }}
            />
          </div>
          {freshclamProgress.detail && (
            <p className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
              {freshclamProgress.detail}
            </p>
          )}
          {freshclamProgress.isComplete && freshclamProgress.succeeded && (
            <p className="text-xs" style={{ color: 'var(--pcs-success)' }}>
              ✅. Actualización completada correctamente
            </p>
          )}
          {freshclamProgress.isComplete && freshclamProgress.succeeded === false && (
            <p className="text-xs" style={{ color: 'var(--pcs-danger)' }}>
              ❌ {freshclamProgress.detail ?? 'La actualización falló'}
            </p>
          )}
        </section>
      )}

      {/* Progreso de escaneo (clamscan) —" panel de ejecución. Se muestra
          mientras busy indica escaneo activo. */}
      {(busy === 'quick' || busy === 'full' || busy === 'custom') && (
        <section
          className="rounded-lg border p-4"
          style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
          aria-label="Progreso de escaneo"
        >
          {/* Encabezado: tipo de escaneo, objetivo (custom) y cronómetro */}
          <div className="flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1">
            <div className="flex min-w-0 flex-wrap items-baseline gap-x-2">
              <h2 className="text-sm font-semibold">{SCAN_LABELS[busy as ScanKind]}</h2>
              <span
                className="rounded-full px-2 py-0.5 text-xs font-medium"
                style={{
                  background: 'color-mix(in srgb, var(--pcs-accent) 16%, transparent)',
                  color: 'var(--pcs-accent)',
                }}
              >
                en curso
              </span>
              {busy === 'custom' && customPath.trim() && (
                <span
                  className="min-w-0 max-w-[24rem] truncate font-mono text-xs"
                  style={{ color: 'var(--pcs-text-muted)' }}
                  title={customPath.trim()}
                >
                  {customPath.trim()}
                </span>
              )}
            </div>
            <span
              className="pcs-tabular-nums text-xs"
              style={{ color: 'var(--pcs-text-muted)' }}
              aria-label={`Tiempo transcurrido: ${formatElapsed(scanElapsedMs)}`}
            >
              <Clock size={12} aria-hidden className="mr-1 inline align-[-1px]" />
              {formatElapsed(scanElapsedMs)}
              {scanProgress && typeof scanProgress.percent === 'number' && (
                <> · {Math.min(100, Math.max(0, scanProgress.percent))}%</>
              )}
            </span>
            <button
              type="button"
              onClick={() => void cancelScan()}
              className="flex shrink-0 cursor-pointer items-center gap-1.5 rounded-md border px-2.5 py-1 text-xs font-medium transition-colors hover:bg-(--pcs-surface-2)"
              style={{ borderColor: 'var(--pcs-danger)', color: 'var(--pcs-danger)' }}
            >
              Cancelar
            </button>
          </div>

          {/* Contadores en vivo (pcs-tabular-nums: los dígitos no bailan) */}
          <div className="mt-3 grid grid-cols-3 gap-3">
            <ProgressStat
              icon={<Folder size={13} aria-hidden />}
              label="Directorios"
              value={(scanProgress?.scannedDirectories ?? 0).toLocaleString()}
            />
            <ProgressStat
              icon={<FileText size={13} aria-hidden />}
              label="Archivos"
              value={(scanProgress?.scannedFiles ?? 0).toLocaleString()}
            />
            <ProgressStat
              icon={<ShieldAlert size={13} aria-hidden />}
              label="Amenazas"
              value={(scanProgress?.infectedFiles ?? 0).toLocaleString()}
              danger={(scanProgress?.infectedFiles ?? 0) > 0}
            />
          </div>

          {/* Barra: determinada si llega percent, indeterminada si no
              (fragmento en movimiento —" nunca ancho 100%, que parece
              "completado") */}
          <div
            className="mt-3 h-2 overflow-hidden rounded-full"
            style={{ background: 'var(--pcs-surface-2)' }}
            role="progressbar"
            aria-label="Progreso del escaneo"
            aria-valuemin={0}
            aria-valuemax={100}
            aria-valuenow={
              scanProgress && typeof scanProgress.percent === 'number'
                ? Math.min(100, Math.max(0, scanProgress.percent))
                : undefined
            }
          >
            {scanProgress && typeof scanProgress.percent === 'number' ? (
              <div
                className="h-full transition-all duration-300 ease-out"
                style={{
                  width: `${Math.min(100, Math.max(0, scanProgress.percent))}%`,
                  background:
                    scanProgress.isComplete && scanProgress.succeeded === false
                      ? 'var(--pcs-danger)'
                      : 'var(--pcs-accent)',
                }}
              />
            ) : (
              <div className="pcs-progress-indeterminate h-full w-full" />
            )}
          </div>

          {/* Ruta actual: línea propia monoespaciada, truncada con title
              (hover muestra la completa) y altura estable para que no
              salte. aria-hidden: cambia a cada archivo y saturaría el
              lector de pantalla. */}
          <div
            className="mt-3 flex min-w-0 items-center gap-2 rounded-md border px-2.5 py-1.5"
            style={{ background: 'var(--pcs-surface-2)', borderColor: 'var(--pcs-border)' }}
            aria-hidden="true"
          >
            <FolderSearch
              size={13}
              className="shrink-0"
              style={{ color: 'var(--pcs-text-muted)' }}
            />
            <code
              className="min-w-0 flex-1 truncate font-mono text-xs"
              style={{ color: 'var(--pcs-text-muted)' }}
              title={
                scanProgress?.currentDirectory ??
                scanProgress?.currentFile ??
                scanProgress?.statusText ??
                undefined
              }
            >
              {scanProgress?.currentDirectory ??
                scanProgress?.currentFile ??
                scanProgress?.statusText ??
                'Preparando escaneo…'}
            </code>
          </div>

          {/* Nota de detalle durante el escaneo y estado final (breve:
              al completar, el panel se desmonta y el resultado pasa al
              panel "Resultado del escaneo") */}
          {scanProgress?.detail && !scanProgress.isComplete && (
            <p className="mt-2 text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
              {scanProgress.detail}
            </p>
          )}
          {scanProgress?.isComplete && scanProgress.succeeded && (
            <p className="mt-2 text-xs" style={{ color: 'var(--pcs-success)' }} role="status">
              ✅. Escaneo completado:{' '}
              {(scanProgress.scannedFiles ?? 0).toLocaleString()} archivos,{' '}
              {(scanProgress.scannedDirectories ?? 0).toLocaleString()} directorios
              {scanProgress.infectedFiles > 0 &&
                `, ${scanProgress.infectedFiles.toLocaleString()} amenaza(s) detectada(s)`}
            </p>
          )}
          {scanProgress?.isComplete && scanProgress.succeeded === false && (
            <p className="mt-2 text-xs" style={{ color: 'var(--pcs-danger)' }} role="alert">
              ❌ {scanProgress.detail ?? 'El escaneo falló'}
            </p>
          )}
        </section>
      )}

      {/* Resultado del último escaneo */}
      {lastScan && lastScan.result.succeeded && (
        <section
          className="rounded-lg border p-4"
          style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
          aria-label="Resultado del escaneo"
        >
          <h2 className="mb-3 text-sm font-semibold">{SCAN_LABELS[lastScan.kind]}</h2>

          {/* Información resumida del escaneo */}
          <div className="grid gap-3 mb-4 grid-cols-1 sm:grid-cols-2 lg:grid-cols-5">
            <StatusCell
              label="Archivos"
              value={lastScan.result.scannedFiles.toLocaleString()}
            />
            <StatusCell
              label="Directorios"
              value={lastScan.result.scannedDirectories.toLocaleString()}
            />
            <StatusCell
              label="Infectados"
              value={lastScan.result.infectedFiles.toLocaleString()}
            />
            <StatusCell label="Motor" value={lastScan.result.engineVersion ?? '—'} />
            <StatusCell
              label="Firmas"
              value={
                lastScan.result.knownViruses === null ||
                lastScan.result.knownViruses === undefined
                  ? '—'
                  : lastScan.result.knownViruses.toLocaleString()
              }
            />
            {/* Duración del escaneo */}
            {lastScan.result.duration != null && (
              <StatusCell
                label="Tiempo"
                value={formatElapsed(lastScan.result.duration * 1000)}
              />
            )}
          </div>

          {/* Detalle de amenazas encontradas */}
          {lastScan.result.threats.length > 0 && (
            <ul className="mt-3 space-y-1 max-h-40 overflow-y-auto text-xs">
              {lastScan.result.threats.map((threat, index) => (
                <li
                  key={`${threat.file}-${index}`}
                  className="flex flex-wrap items-center gap-x-2 gap-y-1 rounded-lg border px-3 py-1.5 text-medium"
                  style={{ background: 'var(--pcs-surface-2)', borderColor: 'var(--pcs-border)' }}
                >
                  <span className="font-mono font-medium flex-1" style={{ color: 'var(--pcs-danger)' }}>
                    {threat.threat}
                  </span>
                  <span className="font-mono text-(--pcs-text-muted) truncate max-w-[300px]">
                    {threat.file}
                  </span>
                  <div className="flex items-center gap-1">
                    {/* Abrir archivo directamente */}
                    <button
                      type="button"
                      className="flex items-center gap-1 rounded px-2 py-1 text-xs font-medium transition-colors"
                      style={{
                        background: 'var(--pcs-surface)',
                        color: 'var(--pcs-text)',
                        border: '1px solid var(--pcs-border)',
                        cursor: 'pointer',
                      }}
                      onClick={() => {
                        void bridge.call('explorer.openPath', { path: threat.file });
                      }}
                      title="Abrir archivo"
                    >
                      <FileText size={12} aria-hidden />
                      <span className="hidden sm:inline">Abrir archivo</span>
                    </button>
                    {/* Abrir carpeta contenedora */}
                    <button
                      type="button"
                      className="flex items-center gap-1 rounded px-2 py-1 text-xs font-medium transition-colors"
                      style={{
                        background: 'var(--pcs-surface)',
                        color: 'var(--pcs-text)',
                        border: '1px solid var(--pcs-border)',
                        cursor: 'pointer',
                      }}
                      onClick={() => {
                        // Extract directory from file path
                        const dir = threat.file.substring(0, threat.file.lastIndexOf('\\'));
                        if (dir) {
                          void bridge.call('explorer.openPath', { path: dir });
                        }
                      }}
                      title="Abrir carpeta contenedora"
                    >
                      <FolderSearch size={12} aria-hidden />
                      <span className="hidden sm:inline">Abrir carpeta</span>
                    </button>
                    {/* Copiar ruta al portapapeles */}
                    <button
                      type="button"
                      className="flex items-center gap-1 rounded px-2 py-1 text-xs font-medium transition-colors"
                      style={{
                        background: 'var(--pcs-surface)',
                        color: 'var(--pcs-text)',
                        border: '1px solid var(--pcs-border)',
                        cursor: 'pointer',
                      }}
                      onClick={() => {
                        navigator.clipboard.writeText(threat.file).then(() => {
                          // Toast temporal o feedback visual
                          const originalTitle = document.title;
                          document.title = '✓ Ruta copiada al portapapeles';
                          setTimeout(() => { document.title = originalTitle; }, 1500);
                        });
                      }}
                      title="Copiar ruta al portapapeles"
                    >
                      <Copy size={12} aria-hidden />
                      <span className="hidden sm:inline">Copiar</span>
                    </button>
                  </div>
                </li>
              ))}
            </ul>
          )}

          {/* Contador de amenazas */}
          {lastScan.result.infectedFiles > 0 && (
            <p className="mt-2 text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
              Se muestran {lastScan.result.threats.length} de{' '}
              {lastScan.result.infectedFiles.toLocaleString()} amenazas detectadas.
            </p>
          )}

          {/* Acción rápida: ver log completo */}
          {lastScan.result.logFile && (
            <p className="mt-2 text-xs cursor-pointer" style={{ color: 'var(--pcs-accent)', textDecoration: 'underline' }}>
              Ver log en: {lastScan.result.logFile}
            </p>
          )}

          {/* Nota informativa: escaneo parcial (exit code 2 con resumen) */}
          {lastScan.result.exitCode === 2 && lastScan.result.detail && (
            <div className="mt-3 rounded-lg border p-3 text-sm"
                 style={{ background: 'var(--pcs-surface-2)', borderColor: 'var(--pcs-warning)', color: 'var(--pcs-text)' }}
                 role="status">
              <div className="flex items-start gap-2">
                <span className="flex-shrink-0 text-lg">ℹ️</span>
                <div>
                  <p className="font-medium" style={{ color: 'var(--pcs-warning)' }}>
                    Escaneo completado con accesos limitados
                  </p>
                  <p className="mt-1">{lastScan.result.detail}</p>
                </div>
              </div>
            </div>
          )}
        </section>
      )}

      {/* Historial */}
      {!loading && status && installed && (
        <section aria-label="Historial de escaneos">
          <h2 className="mb-2 flex items-center gap-2 text-sm font-semibold">
            <ListChecks size={15} aria-hidden />
            Historial de escaneos
          </h2>
          {history.length === 0 ? (
            <p className="text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
              Aún no hay escaneos registrados.
            </p>
          ) : (
            <ul className="space-y-2">
              {history.map((entry, index) => (
                <li
                  key={`${entry.timestamp}-${index}`}
                  className="flex flex-wrap items-center gap-x-3 gap-y-1 rounded-lg border px-4 py-3 text-sm"
                  style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
                >
                  <span
                    className="rounded-full px-2 py-0.5 text-xs font-medium"
                    style={{
                      background: 'color-mix(in srgb, var(--pcs-accent) 16%, transparent)',
                      color: 'var(--pcs-accent)',
                    }}
                  >
                    {SCAN_LABELS[entry.kind as ScanKind] ?? entry.kind}
                  </span>
                  <span className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
                    {new Date(entry.timestamp).toLocaleString()}
                  </span>
                  {entry.path && (
                    <span
                      className="max-w-xs truncate font-mono text-xs"
                      style={{ color: 'var(--pcs-text-muted)' }}
                      title={entry.path}
                    >
                      {entry.path}
                    </span>
                  )}
                  <span className="ml-auto text-xs">{entry.scannedFiles} archivos</span>
                  <span
                    className="rounded-full px-2 py-0.5 text-xs font-medium"
                    style={
                      entry.succeeded && entry.infectedFiles === 0
                        ? {
                            background:
                              'color-mix(in srgb, var(--pcs-success) 16%, transparent)',
                            color: 'var(--pcs-success)',
                          }
                        : {
                            background: 'color-mix(in srgb, var(--pcs-danger) 16%, transparent)',
                            color: 'var(--pcs-danger)',
                          }
                    }
                  >
                    {!entry.succeeded
                      ? 'Fallido'
                      : entry.infectedFiles > 0
                        ? `${entry.infectedFiles} infectado(s)`
                        : 'Sin amenazas'}
                  </span>
                  {entry.detail && (
                    <p
                      className="w-full text-xs"
                      style={{ color: 'var(--pcs-danger)' }}
                    >
                      {entry.detail}
                    </p>
                  )}
                </li>
              ))}
            </ul>
          )}
        </section>
      )}
    </div>
  );
}

function StatusCell(props: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="min-w-0">
      <p className="text-xs uppercase tracking-wide" style={{ color: 'var(--pcs-text-muted)' }}>
        {props.label}
      </p>
      <p
        className={`mt-0.5 truncate text-sm ${props.mono ? 'font-mono' : 'font-medium'}`}
        title={props.value}
      >
        {props.value}
      </p>
    </div>
  );
}

/** Celda de contador vivo del panel de ejecución (ClamAV). */
function ProgressStat(props: {
  icon: React.ReactNode;
  label: string;
  value: string;
  danger?: boolean;
}) {
  return (
    <div
      className="min-w-0 rounded-md border px-3 py-2"
      style={{ background: 'var(--pcs-surface-2)', borderColor: 'var(--pcs-border)' }}
    >
      <div
        className="flex items-center gap-1.5 text-xs uppercase tracking-wide"
        style={{ color: props.danger ? 'var(--pcs-danger)' : 'var(--pcs-text-muted)' }}
      >
        <span className="shrink-0">{props.icon}</span>
        <span className="truncate">{props.label}</span>
      </div>
      <p
        className="pcs-tabular-nums mt-0.5 text-lg font-semibold"
        style={{ color: props.danger ? 'var(--pcs-danger)' : 'var(--pcs-text)' }}
      >
        {props.value}
      </p>
    </div>
  );
}

function ActionButton(props: {
  busy: boolean;
  disabled?: boolean;
  label: string;
  icon: React.ReactNode;
  danger?: boolean;
  primary?: boolean;
  onClick: () => void;
  shortcut?: string;
}) {
  let background = 'var(--pcs-surface-2)';
  let color = 'var(--pcs-text)';
  if (props.primary) {
    background = 'var(--pcs-accent)';
    color = '#fff';
  } else if (props.danger) {
    background = 'color-mix(in srgb, var(--pcs-danger) 14%, transparent)';
    color = 'var(--pcs-danger)';
  }
  return (
    <button
      type="button"
      onClick={props.onClick}
      disabled={props.busy || props.disabled}
      className="flex items-center gap-2 rounded-md px-3 py-2 text-sm font-medium transition-colors disabled:opacity-40"
      style={{ background, color }}
    >
      {props.icon}
      <span>{props.label}</span>
      {props.shortcut && (
        <kbd className="ml-1 px-1.5 py-0.5 text-xs rounded" style={{ background: 'color-mix(in srgb, currentColor 10%, transparent)', border: '1px solid currentColor', opacity: 0.7 }}>
          {props.shortcut}
        </kbd>
      )}
    </button>
  );
}
