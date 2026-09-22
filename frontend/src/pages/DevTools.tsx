import { useCallback, useEffect, useState } from 'react';
import { CheckCircle2, Download, RefreshCw, Rocket } from 'lucide-react';
import { bridge, BridgeError } from '../services/pcsuiteBridge';
import { OperationIds } from '../types/bridge';
import type { DeveloperCollectionResult, DeveloperToolInfo, UpdateCheckResult } from '../types/bridge';

/**
 * Página "Herramientas dev": detecta las tools de desarrollador
 * (developer.list), comprueba actualizaciones contra su fuente de origen
 * (developer.checkUpdate), ejecuta su self-update nativo (developer.update)
 * y lanza su interfaz interactiva (developer.launch).
 *
 * La UI solo invoca operaciones de la allow-list; los códigos controlados
 * del nativo (NOT_SUPPORTED, ELEVATION_REQUIRED…) se muestran tal cual.
 */

// Timeouts: la comprobación va a la red y el self-update descarga paquetes
// (el proveedor da 5 minutos a la descarga; el puente espera un poco más).
const LIST_TIMEOUT_MS = 30_000;
const CHECK_TIMEOUT_MS = 90_000;
const UPDATE_TIMEOUT_MS = 330_000;
const LAUNCH_TIMEOUT_MS = 30_000;

interface ToolNotice {
  kind: 'success' | 'error';
  text: string;
}

function errorMessage(error: unknown, fallback: string): string {
  return error instanceof BridgeError ? error.message : fallback;
}

export function DevTools() {
  const [tools, setTools] = useState<DeveloperToolInfo[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);

  const [checking, setChecking] = useState(false);
  const [checks, setChecks] = useState<Record<string, UpdateCheckResult>>({});
  const [notices, setNotices] = useState<Record<string, ToolNotice>>({});
  const [busy, setBusy] = useState<Record<string, 'update' | 'launch'>>({});

  const load = useCallback(async () => {
    setLoading(true);
    setLoadError(null);
    try {
      const result = await bridge.call<DeveloperCollectionResult>(
        OperationIds.developerList,
        undefined,
        LIST_TIMEOUT_MS,
      );
      setTools(result.tools ?? []);
    } catch (error) {
      setLoadError(errorMessage(error, 'No se pudieron detectar las herramientas.'));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const checkAll = async () => {
    const installed = tools.filter((tool) => tool.isInstalled);
    if (installed.length === 0) return;

    setChecking(true);
    setNotices({});
    try {
      const settled = await Promise.allSettled(
        installed.map((tool) =>
          bridge.call<UpdateCheckResult>(
            OperationIds.developerCheckUpdate,
            { toolId: tool.id },
            CHECK_TIMEOUT_MS,
          ),
        ),
      );

      const nextChecks: Record<string, UpdateCheckResult> = {};
      const nextNotices: Record<string, ToolNotice> = {};
      settled.forEach((outcome, index) => {
        const toolId = installed[index].id;
        if (outcome.status === 'fulfilled') {
          nextChecks[toolId] = outcome.value;
        } else {
          nextNotices[toolId] = {
            kind: 'error',
            text: errorMessage(outcome.reason, 'No se pudo comprobar la actualización.'),
          };
        }
      });
      setChecks((previous) => ({ ...previous, ...nextChecks }));
      setNotices((previous) => ({ ...previous, ...nextNotices }));
    } finally {
      setChecking(false);
    }
  };

  const setNotice = (toolId: string, kind: ToolNotice['kind'], text: string) => {
    setNotices((previous) => ({ ...previous, [toolId]: { kind, text } }));
  };

  const clearNotice = (toolId: string) => {
    setNotices((previous) => {
      const next = { ...previous };
      delete next[toolId];
      return next;
    });
  };

  const runUpdate = async (tool: DeveloperToolInfo) => {
    setBusy((previous) => ({ ...previous, [tool.id]: 'update' }));
    clearNotice(tool.id);
    try {
      const result = await bridge.call<{ previousVersion?: string; currentVersion?: string }>(
        OperationIds.developerUpdate,
        { toolId: tool.id },
        UPDATE_TIMEOUT_MS,
      );
      const from = result?.previousVersion ?? tool.version ?? '';
      const to = result?.currentVersion ?? '';
      setNotice(
        tool.id,
        'success',
        from && to && from !== to ? `Actualizada de ${from} a ${to}.` : 'Actualización completada.',
      );
      // Refresca estado y comprobación de esta tool tras el update.
      await load();
    } catch (error) {
      setNotice(tool.id, 'error', errorMessage(error, 'No se pudo actualizar la herramienta.'));
    } finally {
      setBusy((previous) => {
        const next = { ...previous };
        delete next[tool.id];
        return next;
      });
    }
  };

  const runLaunch = async (tool: DeveloperToolInfo) => {
    setBusy((previous) => ({ ...previous, [tool.id]: 'launch' }));
    clearNotice(tool.id);
    try {
      await bridge.call(OperationIds.developerLaunch, { toolId: tool.id }, LAUNCH_TIMEOUT_MS);
      setNotice(tool.id, 'success', `Se lanzó ${tool.name}.`);
    } catch (error) {
      setNotice(tool.id, 'error', errorMessage(error, `No se pudo lanzar ${tool.name}.`));
    } finally {
      setBusy((previous) => {
        const next = { ...previous };
        delete next[tool.id];
        return next;
      });
    }
  };

  return (
    <div className="space-y-6">
      <header className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold">Herramientas dev</h1>
          <p className="mt-1 text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
            Node, npm, pnpm, Bun y Claude Code: detección, actualización y lanzamiento — todo
            desde operaciones permitidas del puente.
          </p>
        </div>
        <button
          type="button"
          onClick={() => void checkAll()}
          disabled={checking || loading || tools.every((tool) => !tool.isInstalled)}
          className="flex items-center gap-2 rounded-md px-3 py-2 text-sm font-medium transition-colors disabled:opacity-50"
          style={{ background: 'var(--pcs-accent)', color: '#fff' }}
        >
          <RefreshCw size={14} aria-hidden className={checking ? 'animate-spin' : undefined} />
          {checking ? 'Comprobando…' : 'Buscar actualizaciones'}
        </button>
      </header>

      {loadError && (
        <div
          className="rounded-md border px-4 py-3 text-sm"
          style={{ borderColor: 'var(--pcs-danger)', color: 'var(--pcs-danger)' }}
          role="alert"
        >
          {loadError}
        </div>
      )}

      {loading && (
        <p className="text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
          Detectando herramientas…
        </p>
      )}

      {!loading && !loadError && (
        <ul className="grid grid-cols-1 gap-4 xl:grid-cols-2">
          {tools.map((tool) => {
            const check = checks[tool.id];
            const notice = notices[tool.id];
            const action = busy[tool.id];
            const updateReady = Boolean(tool.isInstalled && check?.updateAvailable);

            return (
              <li
                key={tool.id}
                className="space-y-3 rounded-lg border p-4"
                style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
              >
                {/* Cabecera: nombre + badge de estado */}
                <div className="flex items-center gap-2">
                  <span className="text-sm font-medium">{tool.name}</span>
                  <code className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
                    {tool.id}
                  </code>
                  <span
                    className="ml-auto rounded px-2 py-0.5 text-xs font-medium"
                    style={
                      tool.isInstalled
                        ? {
                            background: 'color-mix(in srgb, var(--pcs-success) 18%, transparent)',
                            color: 'var(--pcs-success)',
                          }
                        : {
                            background: 'var(--pcs-surface-2)',
                            color: 'var(--pcs-text-muted)',
                          }
                    }
                  >
                    {tool.isInstalled
                      ? (tool.version ?? 'instalada')
                      : 'no instalada'}
                  </span>
                </div>

                {tool.isInstalled && tool.path && (
                  <p
                    className="truncate font-mono text-xs"
                    style={{ color: 'var(--pcs-text-muted)' }}
                    title={tool.path}
                  >
                    {tool.path}
                  </p>
                )}

                {/* Estado de la comprobación de actualizaciones */}
                {tool.isInstalled && (
                  <p className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
                    {!check
                      ? 'Pulsa «Buscar actualizaciones» para comparar con la fuente de origen.'
                      : !check.supported
                        ? 'Sin información de actualizaciones para esta herramienta.'
                        : check.updateAvailable
                          ? `v${check.currentVersion ?? '?'} → v${check.latestVersion ?? '?'} disponible.`
                          : `Actualizada (${check.currentVersion ?? tool.version ?? '—'}).`}
                  </p>
                )}

                {/* Acciones */}
                <div className="flex flex-wrap items-center gap-2">
                  {tool.isInstalled && (
                    <button
                      type="button"
                      onClick={() => void runUpdate(tool)}
                      disabled={Boolean(action) || !updateReady}
                      title={
                        updateReady
                          ? 'Ejecutar la actualización nativa'
                          : 'Comprueba actualizaciones antes de actualizar'
                      }
                      className="flex items-center gap-2 rounded-md px-3 py-1.5 text-xs font-medium transition-colors disabled:opacity-40"
                      style={{ background: 'var(--pcs-surface-2)', color: 'var(--pcs-text)' }}
                    >
                      <Download size={13} aria-hidden />
                      {action === 'update' ? 'Actualizando…' : 'Actualizar'}
                    </button>
                  )}

                  {tool.isInstalled && (
                    <button
                      type="button"
                      onClick={() => void runLaunch(tool)}
                      disabled={Boolean(action)}
                      className="flex items-center gap-2 rounded-md px-3 py-1.5 text-xs font-medium transition-colors disabled:opacity-40"
                      style={{ background: 'var(--pcs-surface-2)', color: 'var(--pcs-text)' }}
                    >
                      <Rocket size={13} aria-hidden />
                      {action === 'launch' ? 'Lanzando…' : 'Abrir'}
                    </button>
                  )}
                </div>

                {/* Resultado de la última acción */}
                {notice && (
                  <div
                    className="flex items-start gap-2 rounded-md border px-3 py-2 text-xs"
                    style={
                      notice.kind === 'success'
                        ? {
                            borderColor: 'var(--pcs-success)',
                            color: 'var(--pcs-success)',
                          }
                        : { borderColor: 'var(--pcs-danger)', color: 'var(--pcs-danger)' }
                    }
                    role={notice.kind === 'error' ? 'alert' : 'status'}
                  >
                    {notice.kind === 'success' && (
                      <CheckCircle2 size={13} aria-hidden className="mt-0.5 shrink-0" />
                    )}
                    <span>{notice.text}</span>
                  </div>
                )}
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}
