import { useCallback, useEffect, useState } from 'react';
import { Boxes, Power, Play, RefreshCw, ArrowUpCircle } from 'lucide-react';
import { bridge, BridgeError } from '../services/pcsuiteBridge';
import { OperationIds } from '../types/bridge';
import type { WslListResult, WslMutationResult, WslStatusResult } from '../types/bridge';

/**
 * Página "WSL" (Task 007): estado del subsistema (wsl.status), distribuciones
 * (wsl.list) y las 3 mutaciones (wsl.update / wsl.shutdown / wsl.start).
 *
 * Seguridad (§12 / §53): todas las operaciones son de payload cero — el frontend
 * NUNCA envía argumentos wsl.exe; .NET construye el argv fijo. Solo wsl.update
 * puede elevar (Windows genera el UAC, la app no lo simula). Los fallos del CLI
 * vuelven como resultado estructurado (succeeded:false + detail controlado),
 * nunca como error del puente; los códigos controlados (WSL_FAILED/WSL_TIMEOUT)
 * se muestran tal cual, sin stack traces ni salidas crudas.
 */

// Timeouts del lado del cliente, siempre por encima de los del nativo
// (lecturas 30 s, shutdown/start 120 s, update 900 s).
const READ_TIMEOUT_MS = 45_000;
const MUTATE_TIMEOUT_MS = 135_000;
const UPDATE_TIMEOUT_MS = 930_000;

type ActionKind = 'update' | 'shutdown' | 'start';

interface Notice {
  kind: 'success' | 'error';
  text: string;
}

function errorMessage(error: unknown, fallback: string): string {
  return error instanceof BridgeError ? error.message : fallback;
}

const STATE_LABELS: Record<string, string> = {
  running: 'En ejecución',
  stopped: 'Detenido',
  unknown: 'Desconocido',
};

const RUNNING_STATES = new Set(['running']);

export function Wsl() {
  const [status, setStatus] = useState<WslStatusResult | null>(null);
  const [distributions, setDistributions] = useState<WslListResult['distributions']>([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);

  const [busy, setBusy] = useState<ActionKind | null>(null);
  const [notice, setNotice] = useState<Notice | null>(null);

  const refresh = useCallback(async () => {
    setLoading(true);
    setLoadError(null);
    try {
      const statusResult = await bridge.call<WslStatusResult>(
        OperationIds.wslStatus,
        undefined,
        READ_TIMEOUT_MS,
      );
      setStatus(statusResult);
      if (statusResult.installed) {
        const listResult = await bridge.call<WslListResult>(
          OperationIds.wslList,
          undefined,
          READ_TIMEOUT_MS,
        );
        setDistributions(listResult.distributions ?? []);
      } else {
        setDistributions([]);
      }
    } catch (error) {
      setLoadError(errorMessage(error, 'No se pudo consultar WSL.'));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const run = async (
    kind: ActionKind,
    operation: string,
    timeoutMs: number,
    successText: string,
  ) => {
    setBusy(kind);
    setNotice(null);
    try {
      // Payload cero: sin argumentos, el nativo construye el argv fijo.
      const result = await bridge.call<WslMutationResult>(operation, undefined, timeoutMs);
      if (result.succeeded) {
        setNotice({
          kind: 'success',
          text: result.elevated ? `${successText} (elevado)` : successText,
        });
        await refresh();
      } else {
        setNotice({
          kind: 'error',
          text: result.detail ?? `wsl terminó con el código ${result.exitCode}.`,
        });
      }
    } catch (error) {
      setNotice({ kind: 'error', text: errorMessage(error, 'La operación falló.') });
    } finally {
      setBusy(null);
    }
  };

  const installed = status?.installed ?? false;
  const state = status?.state ?? 'unknown';
  const isRunning = RUNNING_STATES.has(state);

  return (
    <div className="space-y-6">
      <header className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold">WSL</h1>
          <p className="mt-1 text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
            Subsistema de Windows para Linux: estado, distribuciones, actualización y
            apagado — solo operaciones fijas, nunca argumentos arbitrarios.
          </p>
        </div>
        <button
          type="button"
          onClick={() => void refresh()}
          disabled={loading || busy !== null}
          className="flex items-center gap-2 rounded-md px-3 py-2 text-sm font-medium transition-colors disabled:opacity-50"
          style={{ background: 'var(--pcs-accent)', color: '#fff' }}
        >
          <RefreshCw size={14} aria-hidden className={loading ? 'animate-spin' : undefined} />
          Actualizar
        </button>
      </header>

      {loading && (
        <p className="text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
          Consultando wsl…
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
          aria-label="Estado de WSL"
        >
          {!installed ? (
            <p className="text-sm" style={{ color: 'var(--pcs-danger)' }}>
              wsl.exe no está en la PATH. Instala el subsistema de Windows para Linux para
              usar esta página.
            </p>
          ) : (
            <div className="grid gap-x-6 gap-y-3 text-sm sm:grid-cols-2 lg:grid-cols-4">
              <StatusCell label="Versión" value={status.version ?? '—'} />
              <StatusCell label="Estado" value={STATE_LABELS[state] ?? state} />
              <StatusCell label="Distribución por defecto" value={status.defaultDistro ?? '—'} />
              <StatusCell label="Ejecutable" value={status.path ?? '—'} mono />
            </div>
          )}
        </section>
      )}

      {/* Acciones */}
      {!loading && status && installed && (
        <section className="flex flex-wrap gap-2" aria-label="Operaciones WSL">
          <ActionButton
            busy={busy === 'update'}
            label={busy === 'update' ? 'Actualizando…' : 'Actualizar WSL'}
            icon={<ArrowUpCircle size={14} aria-hidden />}
            primary
            onClick={() => {
              if (
                !window.confirm(
                  '¿Actualizar WSL a la última versión disponible? Windows puede mostrar un control de cuentas de usuario (UAC).',
                )
              ) {
                return;
              }
              void run(
                'update',
                OperationIds.wslUpdate,
                UPDATE_TIMEOUT_MS,
                'Se ejecutó la actualización de WSL.',
              );
            }}
          />
          <ActionButton
            busy={busy === 'start'}
            label={busy === 'start' ? 'Arrancando…' : 'Arrancar distribución'}
            icon={<Play size={14} aria-hidden />}
            onClick={() =>
              void run(
                'start',
                OperationIds.wslStart,
                MUTATE_TIMEOUT_MS,
                'La distribución por defecto se está arrancando.',
              )
            }
          />
          <ActionButton
            busy={busy === 'shutdown'}
            label={busy === 'shutdown' ? 'Apagando…' : 'Apagar subsistema'}
            icon={<Power size={14} aria-hidden />}
            danger
            onClick={() => {
              if (
                !window.confirm(
                  '¿Apagar el subsistema WSL? Se detendrán todas las distribuciones en ejecución (incluido docker-desktop si está corriendo).',
                )
              ) {
                return;
              }
              void run(
                'shutdown',
                OperationIds.wslShutdown,
                MUTATE_TIMEOUT_MS,
                'El subsistema WSL se apagó.',
              );
            }}
          />
        </section>
      )}

      {/* Distribuciones */}
      {!loading && status && installed && (
        <section aria-label="Distribuciones">
          <h2 className="mb-2 flex items-center gap-2 text-sm font-semibold">
            <Boxes size={15} aria-hidden />
            Distribuciones
          </h2>
          {distributions.length === 0 ? (
            <p className="text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
              No se encontraron distribuciones instaladas.
            </p>
          ) : (
            <ul className="space-y-2">
              {distributions.map((distro) => {
                const running = distro.state.toLowerCase() === 'running';
                return (
                  <li
                    key={distro.name}
                    className="flex flex-wrap items-center gap-3 rounded-lg border px-4 py-3 text-sm"
                    style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
                  >
                    <span className="font-medium">{distro.name}</span>
                    <span
                      className="rounded-full px-2 py-0.5 text-xs font-medium"
                      style={{
                        background: running
                          ? 'color-mix(in srgb, var(--pcs-success) 16%, transparent)'
                          : 'color-mix(in srgb, var(--pcs-text-muted) 16%, transparent)',
                        color: running ? 'var(--pcs-success)' : 'var(--pcs-text-muted)',
                      }}
                    >
                      {running ? 'En ejecución' : 'Detenida'}
                    </span>
                    <span className="ml-auto text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
                      WSL {distro.version}
                    </span>
                  </li>
                );
              })}
            </ul>
          )}
        </section>
      )}

      {!loading && status && !isRunning && installed && distributions.length > 0 && (
        <p className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
          El subsistema está detenido: la distribución por defecto se arrancará en la próxima
          operación.
        </p>
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
      disabled={props.busy}
      className="flex items-center gap-2 rounded-md px-3 py-2 text-sm font-medium transition-colors disabled:opacity-40"
      style={{ background, color }}
    >
      {props.icon}
      {props.label}
    </button>
  );
}
