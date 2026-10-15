import { useCallback, useEffect, useRef, useState } from 'react';
import {
  CheckCircle2,
  Download,
  ExternalLink,
  RefreshCw,
  ShieldCheck,
  Sparkles,
  XCircle,
} from 'lucide-react';
import { bridge, BridgeError } from '../services/pcsuiteBridge';
import { OperationIds, EventIds } from '../types/bridge';
import type {
  JobEventPayload,
  AppUpdateCheckResult,
  UpdateDownloadJobResult,
  UpdateDownloadResult,
} from '../types/bridge';

/**
 * Task 17.3/17.4 â€” actualizaciones dentro de la app.
 *
 * Flujo (propuesta Â§8): check al montar (no bloquea, 5 s mÃ¡ximo) â†’ badge
 * discreto â†’ panel con versiÃ³n/tamaÃ±o/notas â†’ "Descargar e instalar" (Job
 * con progreso y SHA-256 en streaming del lado nativo) â†’ "Instalar ahora"
 * (ÃšNICO botÃ³n que ejecuta algo) â†’ la app se cierra sola y el Setup pide
 * UAC Ã©l solo.
 */

const CHECK_TIMEOUT = 10_000;
const DOWNLOAD_TIMEOUT = 10 * 60_000;
const INSTALL_TIMEOUT = 30_000;

type DownloadState =
  | { phase: 'idle' }
  | { phase: 'downloading'; jobId: string; progress: number }
  | { phase: 'ready'; path: string }
  | { phase: 'failed'; error: string };

function formatMb(bytes: number): string {
  return `${Math.round(bytes / (1024 * 1024))} MB`;
}

/**
 * Estado global de actualizaciÃ³n, montado UNA vez en App: hace el check al
 * arrancar y expone el badge. El panel completo vive en Ajustes ("Acerca de").
 */
export function UpdateCenter() {
  const [check, setCheck] = useState<AppUpdateCheckResult | null>(null);

  useEffect(() => {
    let cancelled = false;
    bridge
      .call<AppUpdateCheckResult>(OperationIds.updateCheck, undefined, CHECK_TIMEOUT)
      .then((result) => { if (!cancelled) setCheck(result); })
      .catch(() => { /* sin red o fallo: la app funciona igual â€” sin drama */ });
    return () => { cancelled = true; };
  }, []);

  if (!check?.available) return null;

  return (
    <a
      href="#/settings"
      className="flex shrink-0 items-center gap-1.5 rounded-full px-3 py-1 text-xs font-semibold transition-colors"
      style={{
        background: 'color-mix(in srgb, var(--pcs-accent) 16%, transparent)',
        color: 'var(--pcs-accent)',
      }}
      title={`VersiÃ³n ${check.latestVersion} disponible â€” Ajustes â€º Acerca de`}
      onClick={() => { void bridge.call(OperationIds.updateCheck, undefined, CHECK_TIMEOUT).catch(() => {}); }}
    >
      <Sparkles size={12} aria-hidden />
      ActualizaciÃ³n
    </a>
  );
}

/**
 * Panel de actualizaciones para la pÃ¡gina de Ajustes: check manual, versiÃ³n
 * actual vs. disponible, descarga con progreso (Job) y el botÃ³n final de
 * instalaciÃ³n (el Ãºnico que ejecuta algo).
 */
export function UpdatePanel() {
  const [check, setCheck] = useState<AppUpdateCheckResult | null>(null);
  const [checking, setChecking] = useState(false);
  const [download, setDownload] = useState<DownloadState>({ phase: 'idle' });
  const [installing, setInstalling] = useState(false);
  const [notice, setNotice] = useState<{ kind: 'success' | 'error'; text: string } | null>(null);

  const jobRef = useRef<string | null>(null);

  const runCheck = useCallback(async () => {
    setChecking(true);
    setNotice(null);
    try {
      const result = await bridge.call<AppUpdateCheckResult>(
        OperationIds.updateCheck, undefined, CHECK_TIMEOUT);
      setCheck(result);
      if (result.error) {
        setNotice({ kind: 'error', text: result.error });
      }
    } catch (e) {
      setNotice({ kind: 'error', text: e instanceof BridgeError ? e.message : 'No se pudo comprobar.' });
    } finally {
      setChecking(false);
    }
  }, []);

  useEffect(() => { void runCheck(); }, [runCheck]);

  // Progreso y desenlace del job de descarga.
  useEffect(() => {
    const unsub = bridge.on<JobEventPayload>(EventIds.jobEvent, (payload) => {
      if (!jobRef.current || payload.jobId !== jobRef.current) return;

      if (payload.type === 'progress') {
        setDownload((d) =>
          d.phase === 'downloading' && payload.progress != null
            ? { ...d, progress: payload.progress }
            : d);
        return;
      }

      if (payload.type !== 'statusChanged') return;

      jobRef.current = null;
      if (payload.status === 'completed') {
        const result = payload.result as UpdateDownloadJobResult | null;
        if (result?.path) {
          setDownload({ phase: 'ready', path: result.path });
          setNotice({ kind: 'success', text: 'Descarga verificada (SHA-256 âœ“). Lista para instalar.' });
        } else {
          setDownload({ phase: 'idle' });
          setNotice({ kind: 'error', text: 'La descarga terminÃ³ sin resultado.' });
        }
      } else if (payload.status === 'failed') {
        setDownload({ phase: 'failed', error: payload.error ?? 'La descarga fallÃ³.' });
        setNotice({ kind: 'error', text: payload.error ?? 'La descarga fallÃ³.' });
      } else if (payload.status === 'cancelled') {
        setDownload({ phase: 'idle' });
        setNotice(null);
      }
    });
    return unsub;
  }, []);

  const startDownload = async () => {
    setNotice(null);
    setDownload({ phase: 'downloading', jobId: '', progress: 0 });
    try {
      const result = await bridge.call<UpdateDownloadResult>(
        OperationIds.updateDownload, undefined, DOWNLOAD_TIMEOUT);
      jobRef.current = result.jobId;
      setDownload({ phase: 'downloading', jobId: result.jobId, progress: 0 });
    } catch (e) {
      setDownload({ phase: 'idle' });
      setNotice({ kind: 'error', text: e instanceof BridgeError ? e.message : 'No se pudo descargar.' });
    }
  };

  const install = async () => {
    if (download.phase !== 'ready') return;
    setInstalling(true);
    try {
      await bridge.call(OperationIds.updateInstall, { path: download.path }, INSTALL_TIMEOUT);
      setNotice({ kind: 'success', text: 'Instalador lanzado. La app se cerrarÃ¡â€¦' });
      // La app se cierra sola (onInstalled â†’ MainForm.Close). Si no se cerrÃ³,
      // el usuario puede seguirla usando; el mensaje ya explica el desenlace.
    } catch (e) {
      setNotice({ kind: 'error', text: e instanceof BridgeError ? e.message : 'No se pudo instalar.' });
    } finally {
      setInstalling(false);
    }
  };

  const isDownloading = download.phase === 'downloading';
  const isReady = download.phase === 'ready';
  const available = check?.available === true;

  return (
    <section
      className="space-y-3 rounded-lg border p-4"
      style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
      aria-label="Actualizaciones"
    >
      <div className="flex items-center justify-between gap-3">
        <h2 className="text-sm font-semibold">Actualizaciones</h2>
        <button
          type="button"
          onClick={() => void runCheck()}
          disabled={checking}
          className="flex shrink-0 items-center gap-1.5 rounded-md border px-2.5 py-1.5 text-xs font-medium cursor-pointer transition-colors disabled:cursor-not-allowed disabled:opacity-50"
          style={{ borderColor: 'var(--pcs-border)', color: 'var(--pcs-text)' }}
        >
          <RefreshCw size={12} aria-hidden className={checking ? 'animate-spin' : undefined} />
          {checking ? 'Comprobandoâ€¦' : 'Buscar actualizaciones'}
        </button>
      </div>

      {notice && (
        <p
          role="status"
          className="text-xs"
          style={{ color: notice.kind === 'success' ? 'var(--pcs-success)' : 'var(--pcs-danger)' }}
        >
          {notice.text}
        </p>
      )}

      {check && !available && !check.error && (
        <p className="flex items-center gap-1.5 text-xs" style={{ color: 'var(--pcs-success)' }}>
          <ShieldCheck size={13} aria-hidden />
          PCSuite {check.currentVersion} estÃ¡ actualizado.
        </p>
      )}

      {check && available && (
        <>
          <div
            className="flex flex-wrap items-center gap-x-3 gap-y-1 rounded-md border px-3 py-2"
            style={{
              borderColor: 'color-mix(in srgb, var(--pcs-accent) 40%, transparent)',
              background: 'color-mix(in srgb, var(--pcs-accent) 8%, transparent)',
            }}
          >
            <span className="text-xs font-mono" style={{ color: 'var(--pcs-text-muted)' }}>
              {check.currentVersion}
            </span>
            <span style={{ color: 'var(--pcs-accent)' }}>â†’</span>
            <span className="text-sm font-semibold" style={{ color: 'var(--pcs-accent)' }}>
              {check.latestVersion}
            </span>
            <span className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
              {check.sizeBytes > 0 ? `Â· ${formatMb(check.sizeBytes)}` : ''}
            </span>
            {check.releaseNotesUrl && (
              <a
                href={check.releaseNotesUrl}
                target="_blank"
                rel="noreferrer"
                className="ml-auto flex items-center gap-1 text-xs"
                style={{ color: 'var(--pcs-accent)' }}
              >
                Notas de versiÃ³n
                <ExternalLink size={10} aria-hidden className="opacity-70" />
              </a>
            )}
          </div>

          {isDownloading && (
            <div className="space-y-1.5">
              <div
                className="h-2 overflow-hidden rounded-full"
                style={{ background: 'var(--pcs-surface-2)' }}
              >
                <div
                  className="h-full transition-all duration-200"
                  style={{
                    width: `${Math.round(download.progress * 100)}%`,
                    background: 'var(--pcs-accent)',
                  }}
                />
              </div>
              <p className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
                Descargando con verificaciÃ³n SHA-256â€¦ {Math.round(download.progress * 100)}%
              </p>
            </div>
          )}

          <div className="flex flex-wrap items-center gap-2">
            {!isDownloading && !isReady && (
              <button
                type="button"
                onClick={() => void startDownload()}
                className="flex items-center gap-2 rounded-md px-4 py-2 text-sm font-medium cursor-pointer transition-colors"
                style={{ background: 'var(--pcs-accent)', color: '#fff' }}
              >
                <Download size={14} aria-hidden />
                Descargar e instalar
              </button>
            )}

            {isReady && (
              <button
                type="button"
                onClick={() => void install()}
                disabled={installing}
                className="flex items-center gap-2 rounded-md px-4 py-2 text-sm font-medium cursor-pointer transition-colors disabled:cursor-not-allowed disabled:opacity-50"
                style={{ background: 'var(--pcs-success)', color: '#fff' }}
              >
                <CheckCircle2 size={14} aria-hidden />
                {installing ? 'Lanzandoâ€¦' : 'Instalar ahora'}
              </button>
            )}

            {download.phase === 'failed' && (
              <button
                type="button"
                onClick={() => void startDownload()}
                className="flex items-center gap-2 rounded-md border px-3 py-2 text-xs font-medium cursor-pointer"
                style={{ borderColor: 'var(--pcs-danger)', color: 'var(--pcs-danger)' }}
              >
                <XCircle size={13} aria-hidden />
                Reintentar descarga
              </button>
            )}
          </div>

          {isReady && (
            <p className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
              El instalador descargado y verificado reemplazarÃ¡ la versiÃ³n actual;
              tu configuraciÃ³n y datos no se tocan. Windows puede pedir permiso (UAC).
            </p>
          )}
        </>
      )}
    </section>
  );
}
