import { useEffect, useState } from 'react';
import { createPortal } from 'react-dom';
import {
  AlertTriangle,
  CheckCircle2,
  Clock,
  Copy,
  Play,
  RotateCcw,
  Terminal,
  XCircle,
} from 'lucide-react';
import { bridge, BridgeError } from '../services/pcsuiteBridge';
import { OperationIds } from '../types/bridge';
import type { JobSnapshot, WindowsCommandInfo, WindowsRunJobResult } from '../types/bridge';
import { useTranslation } from '../hooks/useTranslation';

/**
 * Página "Windows" (Task 036): catálogo de comandos de reparación (sfc,
 * DISM…). Cada comando es una entrada allow-listed del catálogo del backend
 * con argv fijo — aquí solo viaja el id, jamás un comando libre. La
 * ejecución pide UAC por operación y abre una CONSOLA ELEVADA VISIBLE con
 * el progreso nativo de la herramienta; el desenlace llega por el job.
 */
export function WindowsPage() {
  const { t } = useTranslation();
  const [commands, setCommands] = useState<WindowsCommandInfo[] | null>(null);
  const [confirming, setConfirming] = useState<WindowsCommandInfo | null>(null);
  const [running, setRunning] = useState<{ jobId: string; command: WindowsCommandInfo; startedAt: number } | null>(null);
  const [elapsed, setElapsed] = useState(0);
  const [outcome, setOutcome] = useState<WindowsRunJobResult | null>(null);
  const [outcomeFor, setOutcomeFor] = useState<WindowsCommandInfo | null>(null);
  const [failure, setFailure] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    bridge
      .call<{ commands: WindowsCommandInfo[] }>(OperationIds.windowsListCommands, undefined, 15_000)
      .then((result) => { if (!cancelled) setCommands(result.commands); })
      .catch(() => { if (!cancelled) setCommands([]); });
    return () => { cancelled = true; };
  }, []);

  // Sondeo del desenlace del job (2 s) + cronómetro mientras corre.
  useEffect(() => {
    if (!running) return;
    const tick = setInterval(() => setElapsed(Math.floor((Date.now() - running.startedAt) / 1000)), 1000);
    const poll = setInterval(() => {
      void (async () => {
        try {
          const snap = await bridge.call<JobSnapshot>(OperationIds.jobGet, { jobId: running.jobId }, 10_000);
          if (snap.status === 'completed') {
            setOutcome((snap.result as WindowsRunJobResult | null) ?? null);
            setOutcomeFor(running.command);
            setRunning(null);
          } else if (snap.status === 'failed') {
            setFailure(snap.error ?? 'El comando falló.');
            setRunning(null);
          } else if (snap.status === 'cancelled') {
            setNotice('Comando cancelado: la espera se detuvo. Si la consola elevada seguía abierta, cerrala a mano.');
            setRunning(null);
          }
        } catch { /* best-effort: reintenta en el próximo tick */ }
      })();
    }, 2000);
    return () => { clearInterval(tick); clearInterval(poll); };
  }, [running]);

  const startCommand = async (command: WindowsCommandInfo) => {
    setConfirming(null);
    setOutcome(null);
    setFailure(null);
    setNotice(null);
    setElapsed(0);
    try {
      const start = await bridge.call<{ jobId: string }>(
        OperationIds.windowsRunCommand,
        { commandId: command.id },
        15_000,
      );
      setRunning({ jobId: start.jobId, command, startedAt: Date.now() });
    } catch (e) {
      setFailure(e instanceof BridgeError ? e.message : 'No se pudo iniciar el comando.');
    }
  };

  const cancelRunning = async () => {
    if (!running) return;
    try {
      await bridge.call(OperationIds.jobCancel, { jobId: running.jobId }, 10_000);
    } catch { /* el sondeo detecta el desenlace */ }
  };

  const copyCommand = async (text: string) => {
    try { await bridge.call(OperationIds.systemWriteClipboard, { text }); } catch { /* */ }
  };

  const categories = new Map<string, WindowsCommandInfo[]>();
  (commands ?? []).forEach((command) => {
    const list = categories.get(command.category) ?? [];
    list.push(command);
    categories.set(command.category, list);
  });

  const mmss = `${String(Math.floor(elapsed / 60)).padStart(2, '0')}:${String(elapsed % 60).padStart(2, '0')}`;

  return (
    <div className="space-y-6">
      <header>
        <h1 className="text-2xl font-semibold">{t('sidebar.windows')}</h1>
        <p className="mt-1 text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
          {t('pages.windows.subtitle')}
        </p>
      </header>

      {/* Secuencia recomendada */}
      <div
        className="rounded-md border px-3 py-2.5 text-xs"
        style={{ borderColor: 'var(--pcs-border)', background: 'var(--pcs-surface-2)', color: 'var(--pcs-text-muted)' }}
      >
        <strong style={{ color: 'var(--pcs-text)' }}>{t('pages.windows.recommendedLabel')}: </strong>
        DISM CheckHealth → ScanHealth → RestoreHealth → <code>sfc /scannow</code> — {t('pages.windows.recommendedNote')}
      </div>

      {/* Comando en curso */}
      {running && (
        <section
          className="flex items-center justify-between gap-4 rounded-lg border p-4"
          style={{ borderColor: 'var(--pcs-accent)', background: 'var(--pcs-surface)' }}
        >
          <div className="flex items-center gap-3">
            <Clock size={16} aria-hidden className="animate-pulse" style={{ color: 'var(--pcs-accent)' }} />
            <div>
              <p className="text-sm font-medium">{running.command.name}</p>
              <p className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
                {t('pages.windows.elapsed', { n: mmss })} — {t('pages.windows.nativeProgress')}
              </p>
            </div>
          </div>
          <button
            type="button"
            onClick={() => void cancelRunning()}
            className="shrink-0 cursor-pointer rounded-md border px-3 py-1.5 text-xs font-medium"
            style={{ borderColor: 'var(--pcs-border)', color: 'var(--pcs-text)' }}
            title="Detiene la espera; la consola elevada puede quedar abierta y se cierra a mano"
          >
            {t('common.cancel')}
          </button>
        </section>
      )}

      {/* Desenlace */}
      {outcome && outcomeFor && (
        <section
          className="space-y-2 rounded-lg border p-4"
          style={{
            borderColor: outcome.succeeded ? 'color-mix(in srgb, var(--pcs-success) 55%, transparent)' : 'var(--pcs-border)',
            background: 'var(--pcs-surface)',
          }}
        >
          <div className="flex items-center gap-2">
            {outcome.succeeded
              ? <CheckCircle2 size={16} aria-hidden style={{ color: 'var(--pcs-success)' }} />
              : <AlertTriangle size={16} aria-hidden style={{ color: 'var(--pcs-warning, #d97706)' }} />}
            <p className="text-sm font-medium">{outcomeFor.name}</p>
          </div>
          <p className="text-sm" style={{ color: 'var(--pcs-text)' }}>{outcome.message}</p>
          <p className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>{outcome.logHint}</p>
          <button
            type="button"
            onClick={() => { setOutcome(null); setOutcomeFor(null); }}
            className="flex cursor-pointer items-center gap-1.5 rounded-md px-3 py-1.5 text-xs font-medium"
            style={{ background: 'var(--pcs-surface-2)', color: 'var(--pcs-text)' }}
          >
            <RotateCcw size={12} aria-hidden /> {t('pages.windows.runAnother')}
          </button>
        </section>
      )}

      {failure && (
        <section
          className="flex items-start gap-2 rounded-lg border p-4 text-sm"
          style={{ borderColor: 'color-mix(in srgb, var(--pcs-danger) 55%, transparent)', background: 'var(--pcs-surface)' }}
        >
          <XCircle size={16} aria-hidden className="mt-0.5 shrink-0" style={{ color: 'var(--pcs-danger)' }} />
          <p style={{ color: 'var(--pcs-text)' }}>{failure}</p>
        </section>
      )}

      {notice && (
        <p className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>{notice}</p>
      )}

      {/* Catálogo */}
      {commands === null && (
        <p className="text-sm" style={{ color: 'var(--pcs-text-muted)' }}>Cargando catálogo…</p>
      )}

      {[...categories.entries()].map(([category, items]) => (
        <section key={category} className="space-y-3">
          <h2 className="text-xs font-semibold uppercase tracking-wide" style={{ color: 'var(--pcs-text-muted)' }}>
            {category}
          </h2>
          <div className="grid grid-cols-1 gap-3 xl:grid-cols-2">
            {items.map((command) => (
              <article
                key={command.id}
                className="space-y-3 rounded-lg border p-4"
                style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
              >
                <div className="flex items-start justify-between gap-3">
                  <h3 className="text-sm font-medium">{command.name}</h3>
                  <span
                    className="shrink-0 rounded-full px-2 py-0.5 text-[11px]"
                    style={{ background: 'var(--pcs-surface-2)', color: 'var(--pcs-text-muted)' }}
                  >
                    {command.durationHint}
                  </span>
                </div>
                <p className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>{command.description}</p>
                <div className="flex items-center gap-2">
                  <code
                    className="min-w-0 flex-1 truncate rounded-md border px-2 py-1 font-mono text-xs"
                    style={{ borderColor: 'var(--pcs-border)', color: 'var(--pcs-text)' }}
                    title={command.displayCommand}
                  >
                    {command.displayCommand}
                  </code>
                  <button
                    type="button"
                    onClick={() => void copyCommand(command.displayCommand)}
                    className="shrink-0 cursor-pointer rounded border px-1.5 py-1 text-xs"
                    style={{ borderColor: 'var(--pcs-border)', color: 'var(--pcs-text-muted)' }}
                    title={t('pages.windows.copyCommand')}
                  >
                    <Copy size={12} aria-hidden />
                  </button>
                </div>
                <button
                  type="button"
                  onClick={() => setConfirming(command)}
                  disabled={running !== null}
                  className="flex w-full items-center justify-center gap-2 rounded-md px-3 py-2 text-sm font-medium transition-colors disabled:cursor-not-allowed disabled:opacity-40"
                  style={{ background: 'var(--pcs-accent)', color: '#fff' }}
                >
                  <Play size={14} aria-hidden />
                  {command.heavy ? t('pages.windows.runHeavy') : t('pages.windows.run')}
                </button>
              </article>
            ))}
          </div>
        </section>
      ))}

      {/* Confirmación — createPortal: flotante garantizado sobre todo (lección 027) */}
      {confirming &&
        createPortal(
          <div
            className="fixed inset-0 flex items-center justify-center p-4"
            style={{ background: 'rgba(0,0,0,0.55)', zIndex: 1000 }}
            onClick={() => setConfirming(null)}
          >
            <div
              className="w-full max-w-md space-y-4 rounded-lg border p-5 shadow-xl"
              style={{ background: 'var(--pcs-bg, #111827)', borderColor: 'var(--pcs-border)' }}
              onClick={(event) => event.stopPropagation()}
              role="dialog"
              aria-modal="true"
            >
              <div className="flex items-center gap-2">
                <Terminal size={16} aria-hidden style={{ color: 'var(--pcs-accent)' }} />
                <h3 className="text-sm font-semibold">{confirming.name}</h3>
              </div>

              <p className="text-sm" style={{ color: 'var(--pcs-text)' }}>{confirming.description}</p>

              <div className="space-y-2 text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
                <p>
                  {t('pages.windows.modalDesc')}
                </p>
                {confirming.heavy && (
                  <p className="flex items-center gap-1.5" style={{ color: 'var(--pcs-warning, #d97706)' }}>
                    <AlertTriangle size={12} aria-hidden className="shrink-0" />
                    {t('pages.windows.modalHeavy', { durationHint: confirming.durationHint })}
                  </p>
                )}
                <p>{t('pages.windows.modalCommand', { displayCommand: confirming.displayCommand })}</p>
              </div>

              <div className="flex justify-end gap-2">
                <button
                  type="button"
                  onClick={() => setConfirming(null)}
                  className="cursor-pointer rounded-md px-3 py-1.5 text-xs font-medium"
                  style={{ background: 'var(--pcs-surface-2)', color: 'var(--pcs-text)' }}
                >
                  {t('common.cancel')}
                </button>
                <button
                  type="button"
                  onClick={() => void startCommand(confirming)}
                  className="cursor-pointer rounded-md px-3 py-1.5 text-xs font-medium"
                  style={{ background: 'var(--pcs-accent)', color: '#fff' }}
                >
                  {t('pages.windows.run')}
                </button>
              </div>
            </div>
          </div>,
          document.body,
        )}
    </div>
  );
}
