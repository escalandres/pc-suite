import { useCallback, useEffect, useRef, useState } from 'react';
import { ArrowUpCircle, CheckSquare, Info, List, RefreshCw, Search, Square, Trash2, Download } from 'lucide-react';
import { bridge, BridgeError } from '../services/pcsuiteBridge';
import { OperationIds } from '../types/bridge';
import type {
  InstalledApp,
  InstalledAppListResult,
  RemovalPlan,
  UninstallCleanupResult,
  UninstallOfficialResult,
  WinGetDetectResult,
  WinGetMutationResult,
  WinGetPackageCollectionResult,
  WinGetPackageResult,
} from '../types/bridge';

/**
 * Página "Paquetes" (Task 006): detección de winget (winget.detect), búsqueda
 * en el índice (winget.search), paquetes instalados (winget.list),
 * actualizaciones disponibles (winget.updates) y las 4 mutaciones
 * (install/upgrade/upgradeAll/uninstall) con payload SOLO de id de paquete.
 *
 * La UI jamás envía una línea "winget ..." arbitraria: .NET valida el id y
 * construye los argv fijos. Un fallo del CLI vuelve como resultado
 * estructurado (succeeded:false + detail controlado), no como error del
 * puente; los códigos controlados (WINGET_TIMEOUT…) se muestran tal cual.
 */

// Timeouts del lado del cliente, siempre por encima de los del nativo
// (detect 30 s, consultas 120 s, mutaciones 900 s).
const DETECT_TIMEOUT_MS = 45_000;
const QUERY_TIMEOUT_MS = 135_000;
const MUTATION_TIMEOUT_MS = 930_000;

type Mode = 'search' | 'installed' | 'updates' | 'registry';
type MutationKind = 'install' | 'upgrade' | 'uninstall' | 'upgradeAll';

interface Notice {
  kind: 'success' | 'error';
  text: string;
}

const MODES: { id: Mode; label: string; icon: typeof Search }[] = [
  { id: 'search', label: 'Buscar', icon: Search },
  { id: 'installed', label: 'Instalados', icon: List },
  { id: 'updates', label: 'Actualizaciones', icon: ArrowUpCircle },
  { id: 'registry', label: 'Registro', icon: Trash2 },
];

const EMPTY_HINTS: Record<Mode, string> = {
  search: 'Escribe una consulta para buscar en el índice de winget.',
  installed: 'No hay paquetes instalados desde winget.',
  updates: 'Todo está actualizado: no hay actualizaciones pendientes.',
  registry: 'No hay apps con entrada de desinstalación en el registro.',
};

function errorMessage(error: unknown, fallback: string): string {
  return error instanceof BridgeError ? error.message : fallback;
}

export function Winget() {
  const [detect, setDetect] = useState<WinGetDetectResult | null>(null);
  const [loadingDetect, setLoadingDetect] = useState(true);

  const [mode, setMode] = useState<Mode>('installed');
  const [query, setQuery] = useState('');
  const [packages, setPackages] = useState<WinGetPackageResult[]>([]);
  const [loading, setLoading] = useState(false);
  const [listError, setListError] = useState<string | null>(null);

  const [busy, setBusy] = useState<Record<string, MutationKind>>({});
  const [notices, setNotices] = useState<Record<string, Notice>>({});

  // Task 018: selección múltiple para actualización selectiva
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [batchRunning, setBatchRunning] = useState(false);
  const [batchProgress, setBatchProgress] = useState<{ done: number; total: number; failed: string[] } | null>(null);
  const batchCancelRef = useRef(false);

  // Task 020: desinstalador forzado (pestaña Registro)
  const [registryApps, setRegistryApps] = useState<InstalledApp[]>([]);
  const [registryQuery, setRegistryQuery] = useState('');
  const [planFor, setPlanFor] = useState<InstalledApp | null>(null);
  const [plan, setPlan] = useState<RemovalPlan | null>(null);
  const [planLoading, setPlanLoading] = useState(false);
  const [checkedItems, setCheckedItems] = useState<Set<string>>(new Set());
  const [cleanupBusy, setCleanupBusy] = useState(false);
  const [cleanupResult, setCleanupResult] = useState<UninstallCleanupResult | null>(null);

  const openPlan = async (app: InstalledApp) => {
    setPlanFor(app);
    setPlan(null);
    setCleanupResult(null);
    setCheckedItems(new Set());
    setPlanLoading(true);
    try {
      const result = await bridge.call<RemovalPlan>(
        OperationIds.uninstallDiscover,
        { registryKeyPath: app.registryKeyPath },
        30_000,
      );
      setPlan(result);
      setCheckedItems(new Set(
        result.items.filter((item) => item.tier === 'derived').map((item) => item.identifier),
      ));
    } catch (error) {
      setPlanFor(null);
    setNotice('__registry__', 'error',
        errorMessage(error, 'No se pudo construir el plan de eliminación.'));
    } finally {
      setPlanLoading(false);
    }
  };

  const closePlan = () => {
    setPlanFor(null);
    setPlan(null);
    setCleanupResult(null);
    void load('registry', '');
  };

  const togglePlanItem = (identifier: string) => {
    setCheckedItems((current) => {
      const next = new Set(current);
      if (next.has(identifier)) next.delete(identifier);
      else next.add(identifier);
      return next;
    });
  };

  const runOfficialUninstaller = async () => {
    if (!planFor) return;
    if (!window.confirm(
      `¿Lanzar el desinstalador oficial de "${planFor.displayName}"? ` +
      'Windows pedirá permiso (UAC) y el asistente se abrirá en su propia ventana. ' +
      'Complétalo y vuelve aquí para limpiar los residuos.',
    )) {
      return;
    }

    clearNotice('__registry__');
    try {
      const result = await bridge.call<UninstallOfficialResult>(
        OperationIds.uninstallRunOfficial,
        { registryKeyPath: planFor.registryKeyPath },
        120_000,
      );
      setNotice('__registry__', 'success',
        `Desinstalador oficial lanzado (${result.launcher}). Complétalo en su ventana y luego limpia los residuos.`);
    } catch (error) {
      setNotice('__registry__', 'error',
        errorMessage(error, 'No se pudo lanzar el desinstalador oficial.'));
    }
  };

  const runCleanup = async () => {
    if (!plan || !planFor || checkedItems.size === 0 || cleanupBusy) return;
    if (!window.confirm(
      `¿Eliminar ${checkedItems.size} elemento(s) de "${planFor.displayName}"? ` +
      'Windows pedirá permiso (UAC). Esta acción no se puede deshacer.',
    )) {
      return;
    }

    setCleanupBusy(true);
    clearNotice('__registry__');
    setCleanupResult(null);
    try {
      const items = plan.items
        .filter((item) => checkedItems.has(item.identifier))
        .map((item) => ({ kind: item.kind, value: item.identifier }));
      const result = await bridge.call<UninstallCleanupResult>(
        OperationIds.uninstallCleanup,
        { items },
        300_000,
      );
      setCleanupResult(result);
      void load('registry', '');
    } catch (error) {
      setNotice('__registry__', 'error',
        errorMessage(error, 'La limpieza falló.'));
    } finally {
      setCleanupBusy(false);
    }
  };

  const load = useCallback(
    async (currentMode: Mode, search: string) => {
      setLoading(true);
      setListError(null);
      try {
        if (currentMode === 'registry') {
          // Task 020: registro de desinstalación de Windows — sin winget.
          const result = await bridge.call<InstalledAppListResult>(
            OperationIds.uninstallList,
            undefined,
            QUERY_TIMEOUT_MS,
          );
          setRegistryApps(result.apps ?? []);
          setPackages([]);
          return;
        }

        const operation =
          currentMode === 'search'
            ? OperationIds.wingetSearch
            : currentMode === 'installed'
              ? OperationIds.wingetList
              : OperationIds.wingetUpdates;
        const payload = currentMode === 'search' ? { query: search } : undefined;
        const result = await bridge.call<WinGetPackageCollectionResult>(
          operation,
          payload,
          QUERY_TIMEOUT_MS,
        );
        setPackages(result.packages ?? []);
      } catch (error) {
        setPackages([]);
        setRegistryApps([]);
        setListError(errorMessage(error, 'No se pudieron cargar los paquetes.'));
      } finally {
        setLoading(false);
      }
    },
    [],
  );

  // Detección inicial + carga de la pestaña por omisión.
  useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const result = await bridge.call<WinGetDetectResult>(
          OperationIds.wingetDetect,
          undefined,
          DETECT_TIMEOUT_MS,
        );
        if (cancelled) return;
        setDetect(result);
        if (result.installed) {
          await load('installed', '');
        } else {
          setLoadingDetect(false);
        }
      } catch (error) {
        if (!cancelled) {
          setListError(errorMessage(error, 'No se pudo detectar winget.'));
        }
      } finally {
        if (!cancelled) {
          setLoadingDetect(false);
        }
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [load]);

  const switchMode = (next: Mode) => {
    setMode(next);
    setListError(null);
    // Al cambiar de pestaña, limpiar selección: los checkboxes solo viven
    // en el contexto del tab "updates". El plan del Task 020 también se
    // cierra al navegar fuera de "Registro".
    setSelectedIds(new Set());
    setBatchProgress(null);
    setPlanFor(null);
    setPlan(null);
    setCleanupResult(null);
    if (next === 'search') {
      setPackages([]);
      return;
    }
    void load(next, '');
  };

  const submitSearch = () => {
    const trimmed = query.trim();
    if (!trimmed) return;
    void load('search', trimmed);
  };

  const setNotice = (key: string, kind: Notice['kind'], text: string) => {
    setNotices((previous) => ({ ...previous, [key]: { kind, text } }));
  };

  const clearNotice = (key: string) => {
    setNotices((previous) => {
      const next = { ...previous };
      delete next[key];
      return next;
    });
  };

  /**
   * Limpia TODOS los avisos de la página (resumen batch, "todo" y por
   * tarjeta). Se invoca al INICIAR una nueva tanda de actualizaciones: los
   * resultados de la tanda anterior ("2 actualizado(s), 1 fallido(s)…")
   * no deben seguir en pantalla mientras corre la nueva.
   */
  const clearAllNotices = () => setNotices({});

  const runMutation = async (
    key: string,
    kind: MutationKind,
    operation: string,
    payload: unknown,
    successText: string,
  ) => {
    setBusy((previous) => ({ ...previous, [key]: kind }));
    clearNotice(key);
    try {
      const result = await bridge.call<WinGetMutationResult>(
        operation,
        payload,
        MUTATION_TIMEOUT_MS,
      );
      if (result.succeeded) {
        setNotice(key, 'success', result.elevated ? `${successText} (elevado)` : successText);
        await load(mode, query.trim()); // refresca la lista tras la mutación
      } else {
        setNotice(
          key,
          'error',
          result.detail ?? `winget terminó con el código ${result.exitCode}.`,
        );
      }
    } catch (error) {
      setNotice(key, 'error', errorMessage(error, 'La operación falló.'));
    } finally {
      setBusy((previous) => {
        const next = { ...previous };
        delete next[key];
        return next;
      });
    }
  };

  // ------------------------------------------------------------------
  // Task 018: selección múltiple + batch de actualizaciones
  // ------------------------------------------------------------------

  const toggleSelected = (id: string) => {
    setSelectedIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  };

  const allSelected = mode === 'updates' && packages.length > 0
    && selectedIds.size === packages.filter((p) => p.available && p.available !== '').length;

  const toggleSelectAll = () => {
    if (allSelected) {
      setSelectedIds(new Set());
    } else {
      // Solo los paquetes CON actualización disponible (los demás no
      // tienen sentido actualizar).
      setSelectedIds(new Set(
        packages
          .filter((p) => p.available && p.available !== '')
          .map((p) => p.id),
      ));
    }
  };

  /**
   * Batch: ejecuta winget.upgrade por cada ID seleccionado, SECUENCIALMENTE
   * (evita que winget entre en race con múltiples instancias). Cada paquete
   * mantiene su propio estado busy/notice. Al final, un resumen agregado.
   * Cancelable: batchCancelRef detiene el loop antes del siguiente paquete.
   */
  const runBatchUpgrade = async () => {
    const ids = Array.from(selectedIds);
    if (ids.length === 0) return;

    // Pantalla limpia al empezar: los avisos y el resumen de la tanda
    // anterior desaparecen en el momento del clic, no al final de la nueva.
    clearAllNotices();
    const failed: string[] = [];
    setBatchRunning(true);
    setBatchProgress({ done: 0, total: ids.length, failed: [] });
    batchCancelRef.current = false;

    for (let i = 0; i < ids.length; i++) {
      if (batchCancelRef.current) {
        setNotice('__batch__', 'error', `Cancelado: ${i}/${ids.length} completados.`);
        break;
      }

      const pkgId = ids[i];
      const pkg = packages.find((p) => p.id === pkgId);

      setBusy((prev) => ({ ...prev, [pkgId]: 'upgrade' as MutationKind }));
      clearNotice(pkgId);

      try {
        const result = await bridge.call<WinGetMutationResult>(
          OperationIds.wingetUpgrade,
          { packageId: pkgId },
          MUTATION_TIMEOUT_MS,
        );
        if (result.succeeded) {
          setNotice(pkgId, 'success',
            `Se actualizó ${pkg?.name ?? pkgId}${result.elevated ? ' (elevado)' : ''}.`);
        } else {
          failed.push(pkg?.name ?? pkgId);
          setNotice(pkgId, 'error',
            result.detail ?? `winget terminó con el código ${result.exitCode}.`);
        }
      } catch {
        failed.push(pkg?.name ?? pkgId);
        setNotice(pkgId, 'error', 'La operación falló.');
      } finally {
        setBusy((prev) => {
          const next = { ...prev };
          delete next[pkgId];
          return next;
        });
        setBatchProgress({ done: i + 1, total: ids.length, failed });
      }
    }

    setBatchRunning(false);

    // Resumen final (solo si no fue cancelado — el cancel ya puso su aviso).
    if (!batchCancelRef.current) {
      if (failed.length === 0) {
        setNotice('__batch__', 'success', `Se actualizaron ${ids.length} paquete(s) correctamente.`);
      } else {
        setNotice('__batch__', 'error',
          `${ids.length - failed.length} actualizado(s), ${failed.length} fallido(s): ${failed.join(', ')}`);
      }
    }

    // Refrescar la lista tras el batch (elimina los ya actualizados del tab).
    await load('updates', '');
    setSelectedIds(new Set());
  };

  const cancelBatch = () => {
    batchCancelRef.current = true;
  };

  const installed = detect?.installed ?? false;

  return (
    <div className="space-y-6">
      <header className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold">Paquetes</h1>
          <p className="mt-1 text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
            Gestión de paquetes con winget: búsqueda, instalados, actualizaciones e
            instalación/desinstalación — solo ids validados por el nativo, nunca comandos.
          </p>
        </div>
        <button
          type="button"
          onClick={() => {
            // Re-chequear la lista = empezar limpio: los avisos de la tanda
            // anterior ("2 actualizado(s), 1 fallido(s)…") no deben persistir.
            clearAllNotices();
            void load(mode, query.trim());
          }}
          disabled={loadingDetect || loading || !installed}
          className="flex items-center gap-2 rounded-md px-3 py-2 text-sm font-medium transition-colors disabled:opacity-50"
          style={{ background: 'var(--pcs-accent)', color: '#fff' }}
        >
          <RefreshCw size={14} aria-hidden className={loading ? 'animate-spin' : undefined} />
          Actualizar
        </button>
      </header>

      {/* Detección de winget */}
      {loadingDetect && (
        <p className="text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
          Detectando winget…
        </p>
      )}

      {!loadingDetect && detect && (
        <div
          className="rounded-md border px-4 py-3 text-sm"
          style={{
            background: 'var(--pcs-surface)',
            borderColor: installed ? 'var(--pcs-border)' : 'var(--pcs-danger)',
            color: installed ? 'var(--pcs-text)' : 'var(--pcs-danger)',
          }}
          role={installed ? 'status' : 'alert'}
        >
          {installed ? (
            <span className="flex flex-wrap items-center gap-x-3 gap-y-1">
              <span className="font-medium">winget {detect.version ?? ''}</span>
              <code className="truncate text-xs" style={{ color: 'var(--pcs-text-muted)' }} title={detect.path ?? ''}>
                {detect.path}
              </code>
              {detect.isAdministrator && (
                <span className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
                  la app corre elevada
                </span>
              )}
            </span>
          ) : (
            'winget no está disponible en esta máquina (App Installer no está en la PATH).'
          )}
        </div>
      )}

      {listError && (
        <div
          className="rounded-md border px-4 py-3 text-sm"
          style={{ borderColor: 'var(--pcs-danger)', color: 'var(--pcs-danger)' }}
          role="alert"
        >
          {listError}
        </div>
      )}

      {/* Aviso global de upgradeAll */}
      {notices['__all__'] && <NoticeBox notice={notices['__all__']} />}
      {notices['__batch__'] && <NoticeBox notice={notices['__batch__']} />}
      {notices['__registry__'] && <NoticeBox notice={notices['__registry__']} />}

      {/* Task 020: las pestañas viven aquí — "Registro" funciona aunque winget
          no esté instalado (la pestaña no toca winget). */}
      {!loadingDetect && (
        <>
          {/* Selector de pestañas + acción "Actualizar todo" */}
          <div className="flex flex-wrap items-center gap-2">
            <div
              className="flex items-center gap-1 rounded-md border p-1"
              style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
            >
              {MODES.map((entry) => {
                const Icon = entry.icon;
                const active = entry.id === mode;
                return (
                  <button
                    key={entry.id}
                    type="button"
                    onClick={() => switchMode(entry.id)}
                    className="flex items-center gap-2 rounded px-3 py-1.5 text-sm font-medium transition-colors"
                    style={{
                      background: active ? 'var(--pcs-accent)' : 'transparent',
                      color: active ? '#fff' : 'var(--pcs-text-muted)',
                    }}
                  >
                    <Icon size={14} aria-hidden />
                    {entry.label}
                  </button>
                );
              })}
            </div>

            {mode === 'updates' && packages.length > 0 && (
              <>
                {/* Task 018: seleccionar todo / deseleccionar */}
                <button
                  type="button"
                  onClick={toggleSelectAll}
                  disabled={batchRunning}
                  className="flex items-center gap-1.5 rounded-md border px-3 py-2 text-sm font-medium transition-colors disabled:opacity-50"
                  style={{
                    borderColor: 'var(--pcs-border)',
                    color: allSelected ? 'var(--pcs-accent)' : 'var(--pcs-text-muted)',
                  }}
                >
                  {allSelected ? <CheckSquare size={14} aria-hidden /> : <Square size={14} aria-hidden />}
                  {allSelected ? 'Ninguno' : 'Todos'}
                </button>

                {/* Actualizar todo (existente) */}
                <button
                  type="button"
                  onClick={() => {
                    if (
                      !window.confirm(
                        `¿Actualizar ${packages.length} paquete(s)? Windows puede mostrar un control de cuentas de usuario (UAC).`,
                      )
                    ) {
                      return;
                    }
                    // Limpiar los resultados de la tanda anterior al iniciar.
                    clearAllNotices();
                    void runMutation(
                      '__all__',
                      'upgradeAll',
                      OperationIds.wingetUpgradeAll,
                      undefined,
                      'Todas las actualizaciones se aplicaron.',
                    );
                  }}
                  disabled={Boolean(busy['__all__']) || batchRunning}
                  className="flex items-center gap-2 rounded-md px-3 py-2 text-sm font-medium transition-colors disabled:opacity-50"
                  style={{ background: 'var(--pcs-accent)', color: '#fff' }}
                >
                  <Download size={14} aria-hidden />
                  {busy['__all__'] ? 'Actualizando…' : 'Actualizar todo'}
                </button>

                {/* Task 018: actualizar solo los seleccionados */}
                {selectedIds.size > 0 && !batchRunning && (
                  <button
                    type="button"
                    onClick={() => {
                      if (
                        !window.confirm(
                          `¿Actualizar ${selectedIds.size} paquete(s) seleccionado(s)? Windows puede mostrar un control de cuentas de usuario (UAC).`,
                        )
                      ) {
                        return;
                      }
                      void runBatchUpgrade();
                    }}
                    className="flex items-center gap-2 rounded-md px-3 py-2 text-sm font-medium transition-colors"
                    style={{ background: 'var(--pcs-success)', color: '#fff' }}
                  >
                    <ArrowUpCircle size={14} aria-hidden />
                    Actualizar seleccionados ({selectedIds.size})
                  </button>
                )}

                {/* Cancelar batch en curso */}
                {batchRunning && batchProgress && (
                  <div className="flex items-center gap-3">
                    <span className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
                      {batchProgress.done}/{batchProgress.total}
                    </span>
                    <button
                      type="button"
                      onClick={cancelBatch}
                      className="rounded-md border px-2 py-1 text-xs font-medium"
                      style={{ borderColor: 'var(--pcs-danger)', color: 'var(--pcs-danger)' }}
                    >
                      Cancelar
                    </button>
                  </div>
                )}
              </>
            )}
          </div>

          {/* Búsqueda */}
          {mode === 'search' && (
            <form
              onSubmit={(event) => {
                event.preventDefault();
                submitSearch();
              }}
              className="flex items-center gap-2 rounded-md border px-3"
              style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
            >
              <Search size={15} aria-hidden style={{ color: 'var(--pcs-text-muted)' }} />
              <input
                type="search"
                value={query}
                onChange={(event) => setQuery(event.target.value)}
                placeholder="Buscar paquetes en el índice de winget…"
                className="w-full bg-transparent py-2 text-sm outline-none"
                style={{ color: 'var(--pcs-text)' }}
              />
              <button
                type="submit"
                disabled={loading || !query.trim()}
                className="rounded-md px-3 py-1.5 text-sm font-medium transition-colors disabled:opacity-50"
                style={{ background: 'var(--pcs-accent)', color: '#fff' }}
              >
                Buscar
              </button>
            </form>
          )}

          {loading && (
            <p className="text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
              {mode === 'registry' ? 'Consultando el registro…' : 'Consultando winget…'}
            </p>
          )}

          {!loading && mode === 'registry' && !planFor && (
            <RegistryList
              apps={registryApps}
              query={registryQuery}
              onQueryChange={setQuery => setRegistryQuery(setQuery)}
              onOpenPlan={(app) => void openPlan(app)}
            />
          )}

          {!loading && mode === 'registry' && planFor && (
            <RegistryPlanPanel
              app={planFor}
              plan={plan}
              loading={planLoading}
              checked={checkedItems}
              onToggle={togglePlanItem}
              onRunOfficial={() => void runOfficialUninstaller()}
              onCleanup={() => void runCleanup()}
              cleanupBusy={cleanupBusy}
              cleanupResult={cleanupResult}
              onClose={closePlan}
            />
          )}

          {!loading && mode !== 'registry' && !listError && packages.length === 0 && (
            <p className="text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
              {EMPTY_HINTS[mode]}
            </p>
          )}

          {/* Resultados */}
          {!loading && mode !== 'registry' && packages.length > 0 && (
            <ul className="space-y-2">
              {packages.map((pkg) => {
                const notice = notices[pkg.id];
                const action = busy[pkg.id];
                // ¿Hay versión nueva? winget informa "available" tanto en la
                // pestaña de instalados como en la de actualizaciones.
                const hasUpdate = Boolean(pkg.available && pkg.available !== '');
                return (
                  <li
                    key={pkg.id}
                    className="space-y-2 rounded-lg border p-3"
                    style={{
                      background: selectedIds.has(pkg.id)
                        ? 'color-mix(in srgb, var(--pcs-accent) 6%, var(--pcs-surface))'
                        : 'var(--pcs-surface)',
                      borderColor: selectedIds.has(pkg.id)
                        ? 'color-mix(in srgb, var(--pcs-accent) 50%, var(--pcs-border))'
                        : 'var(--pcs-border)',
                    }}
                  >
                    <div className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
                      {/* Task 018: checkbox para selección (solo en updates con
                          versión disponible) */}
                      {mode === 'updates' && hasUpdate && (
                        <button
                          type="button"
                          onClick={() => toggleSelected(pkg.id)}
                          disabled={batchRunning || Boolean(action)}
                          aria-label={selectedIds.has(pkg.id) ? `Deseleccionar ${pkg.name}` : `Seleccionar ${pkg.name}`}
                          className="mr-1 shrink-0 cursor-pointer self-center disabled:cursor-not-allowed disabled:opacity-40"
                          style={{ color: selectedIds.has(pkg.id) ? 'var(--pcs-accent)' : 'var(--pcs-text-muted)' }}
                        >
                          {selectedIds.has(pkg.id)
                            ? <CheckSquare size={16} aria-hidden />
                            : <Square size={16} aria-hidden />}
                        </button>
                      )}
                      <span className="text-sm font-medium">{pkg.name}</span>
                      <code className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
                        {pkg.id}
                      </code>
                      {/* Misma lógica que Herramientas dev: badge naranja +
                          icono de información cuando hay update disponible. */}
                      <span className="ml-auto flex shrink-0 items-center gap-2">
                        {hasUpdate && (
                          <span
                            className="flex items-center"
                            style={{ color: 'var(--pcs-orange)' }}
                            title={`Nueva actualización disponible: v${pkg.version} → v${pkg.available}`}
                          >
                            <Info size={13} aria-hidden />
                          </span>
                        )}
                        <span
                          className="rounded px-2 py-0.5 text-xs font-medium"
                          style={
                            hasUpdate
                              ? {
                                  background: 'color-mix(in srgb, var(--pcs-orange) 18%, transparent)',
                                  color: 'var(--pcs-orange)',
                                }
                              : {
                                  background: 'var(--pcs-surface-2)',
                                  color: 'var(--pcs-text-muted)',
                                }
                          }
                        >
                          {pkg.version}
                        </span>
                        {hasUpdate && (
                          <span
                            className="text-xs font-medium"
                            style={{ color: 'var(--pcs-success)' }}
                          >
                            → v{pkg.available}
                          </span>
                        )}
                      </span>
                    </div>

                    {pkg.match && (
                      <p className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
                        {pkg.match}
                      </p>
                    )}

                    <div className="flex flex-wrap items-center gap-2">
                      {mode === 'search' && (
                        <ActionButton
                          busy={action === 'install'}
                          label="Instalar"
                          icon={<Download size={13} aria-hidden />}
                          onClick={() =>
                            void runMutation(
                              pkg.id,
                              'install',
                              OperationIds.wingetInstall,
                              { packageId: pkg.id },
                              `Se instaló ${pkg.name}.`,
                            )
                          }
                        />
                      )}

                      {mode === 'updates' && (
                        <ActionButton
                          busy={action === 'upgrade'}
                          label="Actualizar"
                          icon={<ArrowUpCircle size={13} aria-hidden />}
                          onClick={() =>
                            void runMutation(
                              pkg.id,
                              'upgrade',
                              OperationIds.wingetUpgrade,
                              { packageId: pkg.id },
                              `Se actualizó ${pkg.name}.`,
                            )
                          }
                        />
                      )}

                      {mode === 'installed' && (
                        <ActionButton
                          busy={action === 'uninstall'}
                          label="Desinstalar"
                          danger
                          icon={<Trash2 size={13} aria-hidden />}
                          onClick={() => {
                            if (!window.confirm(`¿Desinstalar ${pkg.name} (${pkg.id})?`)) {
                              return;
                            }
                            void runMutation(
                              pkg.id,
                              'uninstall',
                              OperationIds.wingetUninstall,
                              { packageId: pkg.id },
                              `Se desinstaló ${pkg.name}.`,
                            );
                          }}
                        />
                      )}
                    </div>

                    {notice && <NoticeBox notice={notice} />}
                  </li>
                );
              })}
            </ul>
          )}
        </>
      )}
    </div>
  );
}

function ActionButton(props: {
  busy: boolean;
  label: string;
  icon: React.ReactNode;
  danger?: boolean;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={props.onClick}
      disabled={props.busy}
      className="flex items-center gap-2 rounded-md px-3 py-1.5 text-xs font-medium transition-colors disabled:opacity-40"
      style={
        props.danger
          ? {
              background: 'color-mix(in srgb, var(--pcs-danger) 14%, transparent)',
              color: 'var(--pcs-danger)',
            }
          : { background: 'var(--pcs-surface-2)', color: 'var(--pcs-text)' }
      }
    >
      {props.icon}
      {props.busy ? 'Procesando…' : props.label}
    </button>
  );
}

function NoticeBox(props: { notice: Notice }) {
  const { notice } = props;
  return (
    <div
      className="flex items-start gap-2 rounded-md border px-3 py-2 text-xs"
      style={
        notice.kind === 'success'
          ? { borderColor: 'var(--pcs-success)', color: 'var(--pcs-success)' }
          : { borderColor: 'var(--pcs-danger)', color: 'var(--pcs-danger)' }
      }
      role={notice.kind === 'error' ? 'alert' : 'status'}
    >
      <span>{notice.text}</span>
    </div>
  );
}

// =====================================================================
// Task 020 — Desinstalador forzado: componentes de la pestaña Registro
// =====================================================================

const KIND_LABELS: Record<string, string> = {
  process: 'Proceso',
  service: 'Servicio',
  scheduledtask: 'Tarea programada',
  folder: 'Carpeta',
  registrykey: 'Clave de registro',
};

function formatSize(kb: number): string {
  if (kb <= 0) return '';
  if (kb < 1024) return `${kb} KB`;
  if (kb < 1024 * 1024) return `${Math.round(kb / 1024)} MB`;
  return `${(kb / 1024 / 1024).toFixed(1)} GB`;
}

function RegistryList(props: {
  apps: InstalledApp[];
  query: string;
  onQueryChange: (query: string) => void;
  onOpenPlan: (app: InstalledApp) => void;
}) {
  const { apps, query, onQueryChange, onOpenPlan } = props;
  const normalized = query.trim().toLowerCase();
  const visible = normalized
    ? apps.filter((app) =>
        app.displayName.toLowerCase().includes(normalized)
        || app.publisher.toLowerCase().includes(normalized))
    : apps;

  return (
    <>
      <p className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
        Apps con entrada de desinstalación en el registro de Windows — incluye
        las que winget no ve. El desinstalador forzado funciona en dos pasos:
        plan revisado + UAC puntual.
      </p>

      <form
        onSubmit={(event) => event.preventDefault()}
        className="flex items-center gap-2 rounded-md border px-3"
        style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
      >
        <Search size={15} aria-hidden style={{ color: 'var(--pcs-text-muted)' }} />
        <input
          type="search"
          value={query}
          onChange={(event) => onQueryChange(event.target.value)}
          placeholder="Filtrar por nombre o editor…"
          className="w-full bg-transparent py-2 text-sm outline-none"
          style={{ color: 'var(--pcs-text)' }}
        />
      </form>

      {visible.length === 0 ? (
        <p className="text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
          {EMPTY_HINTS.registry}
        </p>
      ) : (
        <ul className="space-y-2">
          {visible.map((app) => (
            <li
              key={app.registryKeyPath}
              className="rounded-lg border p-3"
              style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
            >
              <div className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
                <span className="text-sm font-medium">{app.displayName}</span>
                {app.displayVersion && (
                  <span className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
                    v{app.displayVersion}
                  </span>
                )}
                {app.publisher && (
                  <span className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
                    {app.publisher}
                  </span>
                )}
                {app.msiGuid && (
                  <span
                    className="rounded border px-1.5 py-0.5 text-[10px] font-medium"
                    style={{ borderColor: 'var(--pcs-border)', color: 'var(--pcs-text-muted)' }}
                    title={app.msiGuid}
                  >
                    MSI
                  </span>
                )}
                {app.isSystemComponent && (
                  <span
                    className="rounded border px-1.5 py-0.5 text-[10px] font-medium"
                    style={{ borderColor: 'var(--pcs-orange)', color: 'var(--pcs-orange)' }}
                    title="Componente marcado como del sistema por el instalador"
                  >
                    sistema
                  </span>
                )}
                <span className="ml-auto flex shrink-0 items-center gap-2">
                  {app.estimatedSizeKb > 0 && (
                    <span className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
                      {formatSize(app.estimatedSizeKb)}
                    </span>
                  )}
                  <button
                    type="button"
                    onClick={() => onOpenPlan(app)}
                    className="flex items-center gap-1.5 rounded-md border px-2.5 py-1.5 text-xs font-medium transition-colors"
                    style={{ borderColor: 'var(--pcs-danger)', color: 'var(--pcs-danger)' }}
                  >
                    <Trash2 size={12} aria-hidden />
                    Desinstalar…
                  </button>
                </span>
              </div>
              {app.installLocation && (
                <p className="mt-1 truncate font-mono text-xs" style={{ color: 'var(--pcs-text-muted)' }} title={app.installLocation}>
                  {app.installLocation}
                </p>
              )}
            </li>
          ))}
        </ul>
      )}
    </>
  );
}

function RegistryPlanPanel(props: {
  app: InstalledApp;
  plan: RemovalPlan | null;
  loading: boolean;
  checked: Set<string>;
  onToggle: (identifier: string) => void;
  onRunOfficial: () => void;
  onCleanup: () => void;
  cleanupBusy: boolean;
  cleanupResult: UninstallCleanupResult | null;
  onClose: () => void;
}) {
  const { app, plan, loading, checked, onToggle, onRunOfficial, onCleanup, cleanupBusy, cleanupResult, onClose } = props;

  return (
    <div
      className="space-y-3 rounded-lg border p-4"
      style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
    >
      <div className="flex items-center gap-3">
        <button
          type="button"
          onClick={onClose}
          className="rounded-md border px-2.5 py-1.5 text-xs font-medium"
          style={{ borderColor: 'var(--pcs-border)', color: 'var(--pcs-text)' }}
        >
          ← Volver al listado
        </button>
        <span className="text-sm font-semibold">{app.displayName}</span>
      </div>

      {app.isSystemComponent && (
        <p
          className="rounded-md border px-3 py-2 text-xs"
          style={{ borderColor: 'var(--pcs-orange)', color: 'var(--pcs-orange)' }}
          role="alert"
        >
          Esta app está marcada como componente del sistema por su instalador.
          Eliminarla puede afectar a otro software: revisa el plan con calma.
        </p>
      )}

      {loading && (
        <p className="text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
          Construyendo el plan de eliminación…
        </p>
      )}

      {plan && (
        <>
          {plan.hasOfficialUninstaller && (
            <div className="flex flex-wrap items-center gap-3">
              <button
                type="button"
                onClick={onRunOfficial}
                className="flex items-center gap-2 rounded-md px-3 py-2 text-sm font-medium"
                style={{ background: 'var(--pcs-accent)', color: '#fff' }}
              >
                1. Ejecutar desinstalador oficial (UAC)
              </button>
              <span className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
                Camino preferente: el asistente de la propia app, con elevación.
                Complétalo y vuelve para limpiar lo que deje.
              </span>
            </div>
          )}

          <p className="text-sm font-medium">Esto es EXACTAMENTE lo que se eliminará:</p>
          <ul className="space-y-1.5">
            {plan.items.map((item) => {
              const isChecked = checked.has(item.identifier);
              const isDerived = item.tier === 'derived';
              return (
                <li key={`${item.kind}:${item.identifier}`} className="flex items-start gap-2 text-sm">
                  <button
                    type="button"
                    onClick={() => onToggle(item.identifier)}
                    aria-label={isChecked ? `Deseleccionar ${item.identifier}` : `Seleccionar ${item.identifier}`}
                    className="mt-0.5 shrink-0 cursor-pointer"
                    style={{ color: isChecked ? 'var(--pcs-accent)' : 'var(--pcs-text-muted)' }}
                  >
                    {isChecked ? <CheckSquare size={15} aria-hidden /> : <Square size={15} aria-hidden />}
                  </button>
                  <span
                    className="shrink-0 rounded border px-1.5 py-0.5 text-[10px] font-medium"
                    style={{
                      borderColor: isDerived ? 'color-mix(in srgb, var(--pcs-accent) 45%, transparent)' : 'var(--pcs-border)',
                      color: isDerived ? 'var(--pcs-accent)' : 'var(--pcs-text-muted)',
                    }}
                    title={isDerived
                      ? 'Derivado del registro de la app o por contención de ruta (quirúrgico)'
                      : 'Candidato heurístico: revisa antes de marcar'}
                  >
                    {KIND_LABELS[item.kind] ?? item.kind}
                    {isDerived ? '' : '?'}
                  </span>
                  <span className="min-w-0">
                    <span className="block break-all font-mono text-xs">{item.identifier}</span>
                    {item.detail && (
                      <span className="block text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
                        {item.detail}
                      </span>
                    )}
                  </span>
                </li>
              );
            })}
          </ul>

          <div className="flex flex-wrap items-center gap-3">
            <button
              type="button"
              onClick={onCleanup}
              disabled={checked.size === 0 || cleanupBusy}
              className="flex items-center gap-2 rounded-md px-3 py-2 text-sm font-medium disabled:opacity-40"
              style={{ background: 'var(--pcs-danger)', color: '#fff' }}
            >
              <Trash2 size={14} aria-hidden />
              {cleanupBusy ? 'Limpiando…' : `${plan.hasOfficialUninstaller ? '2. Limpiar residuos' : 'Aplicar limpieza'} (${checked.size})`}
            </button>
            <span className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
              Los ítems derivados (azul) vienen del registro de la app; los
              candidatos (?) son heurísticos — desmárcalos si no te convencen.
              UAC puntual por limpieza.
            </span>
          </div>

          {cleanupResult && (
            <div
              className="rounded-md border px-4 py-3 text-sm"
              role="status"
              style={{
                borderColor: cleanupResult.results.every((r) => r.succeeded)
                  ? 'var(--pcs-success)'
                  : 'var(--pcs-warning)',
                color: cleanupResult.results.every((r) => r.succeeded)
                  ? 'var(--pcs-success)'
                  : 'var(--pcs-warning)',
              }}
            >
              <p className="font-semibold">
                {cleanupResult.results.filter((r) => r.succeeded).length}/
                {cleanupResult.results.length} ítems eliminados.
                {cleanupResult.requiresRestart && ' Reinicia para completar los servicios.'}
              </p>
              <ul className="mt-1 space-y-0.5">
                {cleanupResult.results
                  .filter((result) => !result.succeeded)
                  .map((result) => (
                    <li key={result.identifier} className="text-xs">
                      ✗ [{KIND_LABELS[result.kind] ?? result.kind}] {result.identifier}
                      {result.detail ? ` — ${result.detail}` : ''}
                    </li>
                  ))}
              </ul>
            </div>
          )}
        </>
      )}
    </div>
  );
}
