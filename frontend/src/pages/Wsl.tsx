import { useCallback, useEffect, useState } from 'react';
import { Boxes, Power, Play, RefreshCw, ArrowUpCircle } from 'lucide-react';
import { bridge, BridgeError } from '../services/pcsuiteBridge';
import { OperationIds } from '../types/bridge';
import type { WslListResult, WslMutationResult, WslStatusResult } from '../types/bridge';
import { useTranslation } from '../hooks/useTranslation';
import { t } from '../lib/i18n';

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
  running: t('pages.wslState.running'),
  stopped: t('pages.wslState.stopped'),
  unknown: t('pages.wslState.unknown'),
};

const RUNNING_STATES = new Set(['running']);

export function Wsl() {
  const { t } = useTranslation();
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
      setLoadError(errorMessage(error, t('pages.wsl.loadError')));
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
      setNotice({ kind: 'error', text: errorMessage(error, t('pages.wsl.opFailed')) });
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
          <h1 className="text-2xl font-semibold">{t('sidebar.wsl')}</h1>
          <p className="mt-1 text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
            {t('pages.wsl.subtitle')}
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
          {t('common.refresh')}
        </button>
      </header>

      {loading && (
        <p className="text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
          {t('pages.wsl.loading')}
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
          aria-label={t('pages.wsl.statusAria')}
        >
          {!installed ? (
            <p className="text-sm" style={{ color: 'var(--pcs-danger)' }}>
              {t('pages.wsl.notInstalled')}
            </p>
          ) : (
            <div className="grid gap-x-6 gap-y-3 text-sm sm:grid-cols-2 lg:grid-cols-4">
              <StatusCell label={t('pages.wsl.labelVersion')} value={status.version ?? t('common.dash')} />
              <StatusCell label={t('pages.wsl.labelState')} value={STATE_LABELS[state] ?? state} />
              <StatusCell label={t('pages.wsl.labelDefaultDistro')} value={status.defaultDistro ?? t('common.dash')} />
              <StatusCell label={t('pages.wsl.labelExecutable')} value={status.path ?? t('common.dash')} mono />
            </div>
          )}
        </section>
      )}

      {/* Acciones */}
      {!loading && status && installed && (
        <section className="flex flex-wrap gap-2" aria-label={t('pages.wsl.actionsAria')}>
          <ActionButton
            busy={busy === 'update'}
            label={busy === 'update' ? t('pages.wsl.updating') : t('pages.wsl.updateWSL')}
            icon={<ArrowUpCircle size={14} aria-hidden />}
            primary
            onClick={() => {
              if (
                !window.confirm(
                  t('pages.wsl.confirmUpdate')
                )
              ) {
                return;
              }
              void run(
                'update',
                OperationIds.wslUpdate,
                UPDATE_TIMEOUT_MS,
                t('pages.wsl.updateDone'),
              );
            }}
          />
          <ActionButton
            busy={busy === 'start'}
            label={busy === 'start' ? t('pages.wsl.starting') : t('pages.wsl.startDistro')}
            icon={<Play size={14} aria-hidden />}
            onClick={() =>
              void run(
                'start',
                OperationIds.wslStart,
                MUTATE_TIMEOUT_MS,
                t('pages.wsl.startingDistro'),
              )
            }
          />
          <ActionButton
            busy={busy === 'shutdown'}
            label={busy === 'shutdown' ? t('pages.wsl.shuttingDown') : t('pages.wsl.shutdown')}
            icon={<Power size={14} aria-hidden />}
            danger
            onClick={() => {
              if (
                !window.confirm(
                  t('pages.wsl.confirmShutdown')
                )
              ) {
                return;
              }
              void run(
                'shutdown',
                OperationIds.wslShutdown,
                MUTATE_TIMEOUT_MS,
                t('pages.wsl.shutdownSuccess'),
              );
            }}
          />
        </section>
      )}

      {/* Distribuciones */}
      {!loading && status && installed && (
        <section aria-label={t('pages.wsl.distributionsAria')}>
          <h2 className="mb-2 flex items-center gap-2 text-sm font-semibold">
            <Boxes size={15} aria-hidden />
            {t('pages.wsl.distributions')}
          </h2>
          {distributions.length === 0 ? (
            <p className="text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
              {t('pages.wsl.noDistros')}
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
                      {running ? t('pages.wsl.running') : t('pages.wsl.stopped')}
                    </span>
                    <span className="ml-auto text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
                      {t('pages.wsl.wslVersion', { version: distro.version })}
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
          {t('pages.wsl.stoppedNotice')}
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
      {props.busy ? t('pages.wsl.processing') : props.label}
    </button>
  );
}
