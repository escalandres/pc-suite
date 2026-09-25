import { useCallback, useEffect, useState } from 'react';
import {
  ArrowUpCircle,
  Download,
  FolderSearch,
  HardDrive,
  ListChecks,
  RefreshCw,
  Zap,
} from 'lucide-react';
import { bridge, BridgeError } from '../services/pcsuiteBridge';
import { OperationIds } from '../types/bridge';
import type {
  ClamAvHistoryEntryResult,
  ClamAvHistoryResult,
  ClamAvMutationResult,
  ClamAvScanResult,
  ClamAvStatusResult,
} from '../types/bridge';

/**
 * Página "ClamAV" (Task 008): estado del antivirus (clamav.status), instalación
 * (clamav.install), actualización de definiciones (clamav.updateDefinitions),
 * los 3 escaneos (clamav.quickScan / fullScan / customScan) e historial
 * (clamav.history).
 *
 * Seguridad (§12 / §53): el único dato que viaja desde el frontend es la ruta
 * de customScan ({"path"}), validada en .NET (absoluta, existente, sin NUL);
 * el resto son operaciones de payload cero y .NET construye el argv fijo de
 * clamscan/freshclam — nunca hay shell ni argumentos arbitrarios. Solo install
 * y updateDefinitions pueden elevar (Windows genera el UAC, la app no lo
 * simula) y los escaneos jamás lo piden. Los fallos del CLI vuelven como
 * resultado estructurado (succeeded:false + detail controlado), nunca como
 * error del puente; los códigos controlados (CLAMAV_FAILED / CLAMAV_TIMEOUT)
 * se muestran tal cual, sin stack traces ni salidas crudas.
 */

// Timeouts del lado del cliente, siempre por encima de los del nativo
// (lecturas 30 s, install/update/quick 900 s, custom 1 h, full 2 h).
const READ_TIMEOUT_MS = 45_000;
const MUTATION_TIMEOUT_MS = 930_000;
const QUICK_TIMEOUT_MS = 930_000;
const CUSTOM_TIMEOUT_MS = 3_630_000;
const FULL_TIMEOUT_MS = 7_230_000;

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

  const runMutation = async (
    kind: ActionKind,
    operation: string,
    successText: string,
  ) => {
    setBusy(kind);
    setNotice(null);
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

  const runScan = async (
    kind: ScanKind,
    operation: string,
    timeoutMs: number,
    payload?: unknown,
  ) => {
    setBusy(kind);
    setNotice(null);
    try {
      const result = await bridge.call<ClamAvScanResult>(operation, payload, timeoutMs);
      setLastScan({ kind, result });
      if (result.succeeded) {
        setNotice({
          kind: 'success',
          text:
            result.detail ??
            (result.infectedFiles > 0
              ? `Escaneo terminado: ${result.infectedFiles} archivo(s) infectado(s).`
              : 'Escaneo terminado: sin amenazas.'),
        });
        await loadHistory();
      } else {
        setNotice({
          kind: 'error',
          text: result.detail ?? `clamscan terminó con el código ${result.exitCode}.`,
        });
      }
    } catch (error) {
      setNotice({ kind: 'error', text: errorMessage(error, 'El escaneo falló.') });
    } finally {
      setBusy(null);
    }
  };

  const runCustomScan = () => {
    const target = customPath.trim();
    if (!target) return;
    // La validación (absoluta/existente/sin NUL) es de .NET: aquí solo
    // evitamos la llamada vacía.
    void runScan('custom', OperationIds.clamavCustomScan, CUSTOM_TIMEOUT_MS, {
      path: target,
    });
  };

  const installed = status?.installed ?? false;
  const busyAny = busy !== null;

  return (
    <div className="space-y-6">
      <header className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold">ClamAV</h1>
          <p className="mt-1 text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
            Antivirus de código abierto: estado, instalación, definiciones, escaneos e
            historial — solo operaciones fijas, nunca argumentos arbitrarios.
          </p>
        </div>
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
              <p className="text-sm" style={{ color: 'var(--pcs-danger)' }}>
                clamscan.exe no se encontró (PATH ni directorios comunes). Instala ClamAV para
                poder escanear.
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
                  void runMutation(
                    'install',
                    OperationIds.clamavInstall,
                    'ClamAV se instaló correctamente.',
                  );
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
          <div className="flex flex-wrap gap-2">
            <ActionButton
              busy={busy === 'update'}
              disabled={busyAny || !status.canUpdateDefinitions}
              label={busy === 'update' ? 'Actualizando…' : 'Actualizar definiciones'}
              icon={<ArrowUpCircle size={14} aria-hidden />}
              onClick={() => {
                if (
                  !window.confirm(
                    '¿Actualizar las definiciones de virus con freshclam? Windows puede mostrar un control de cuentas de usuario (UAC).',
                  )
                ) {
                  return;
                }
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
              onClick={() =>
                void runScan('quick', OperationIds.clamavQuickScan, QUICK_TIMEOUT_MS)
              }
            />
            <ActionButton
              busy={busy === 'full'}
              disabled={busyAny}
              label={busy === 'full' ? 'Escaneando…' : 'Escaneo completo'}
              icon={<HardDrive size={14} aria-hidden />}
              onClick={() => {
                if (
                  !window.confirm(
                    '¿Ejecutar un escaneo completo de todos los discos? Puede tardar varias horas y consumir mucha CPU.',
                  )
                ) {
                  return;
                }
                void runScan('full', OperationIds.clamavFullScan, FULL_TIMEOUT_MS);
              }}
            />
          </div>

          <div className="flex flex-wrap items-center gap-2">
            <input
              type="text"
              value={customPath}
              onChange={(event) => setCustomPath(event.target.value)}
              onKeyDown={(event) => {
                if (event.key === 'Enter') runCustomScan();
              }}
              disabled={busyAny}
              placeholder="C:\Descargas"
              aria-label="Ruta a escanear"
              className="w-full max-w-md rounded-md border px-3 py-2 text-sm outline-none sm:w-auto"
              style={{
                background: 'var(--pcs-surface-2)',
                borderColor: 'var(--pcs-border)',
                color: 'var(--pcs-text)',
              }}
            />
            <ActionButton
              busy={busy === 'custom'}
              disabled={busyAny || customPath.trim().length === 0}
              label={busy === 'custom' ? 'Escaneando…' : 'Escanear ruta'}
              icon={<FolderSearch size={14} aria-hidden />}
              onClick={runCustomScan}
            />
          </div>

          {!status.canUpdateDefinitions && (
            <p className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
              freshclam.exe no está junto a clamscan.exe: la actualización de definiciones no
              está disponible.
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

          <div className="grid gap-x-6 gap-y-3 text-sm sm:grid-cols-2 lg:grid-cols-5">
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
          </div>

          {lastScan.result.threats.length > 0 && (
            <ul className="mt-3 space-y-1">
              {lastScan.result.threats.map((threat, index) => (
                <li
                  key={`${threat.file}-${index}`}
                  className="flex flex-wrap gap-x-2 text-xs"
                >
                  <span className="font-mono font-medium" style={{ color: 'var(--pcs-danger)' }}>
                    {threat.threat}
                  </span>
                  <span className="font-mono" style={{ color: 'var(--pcs-text-muted)' }}>
                    {threat.file}
                  </span>
                </li>
              ))}
            </ul>
          )}

          {lastScan.result.infectedFiles > lastScan.result.threats.length && (
            <p className="mt-2 text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
              Se muestran {lastScan.result.threats.length} de{' '}
              {lastScan.result.infectedFiles} amenazas detectadas.
            </p>
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

function ActionButton(props: {
  busy: boolean;
  disabled?: boolean;
  label: string;
  icon: React.ReactNode;
  danger?: boolean;
  primary?: boolean;
  onClick: () => void;
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
      {props.label}
    </button>
  );
}
