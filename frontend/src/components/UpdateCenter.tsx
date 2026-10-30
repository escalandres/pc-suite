import { useCallback, useEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import {
  AlertTriangle,
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
import { t } from '../lib/i18n';
import type {
  JobEventPayload,
  AppUpdateCheckResult,
  UpdateDownloadJobResult,
  UpdateDownloadResult,
} from '../types/bridge';

/**
 * Task 17.3/17.4 — actualizaciones dentro de la app.
 *
 * Flujo (propuesta §8): check al montar (no bloquea, 5 s máximo) → modal de
 * versión nueva (Task 027, una vez por sesión) + badge discreto → panel con
 * versión/tamaño/notas → "Descargar e instalar" (Job con progreso y SHA-256
 * en streaming del lado nativo) → "Instalar ahora" (ÚNICO botón que ejecuta
 * algo) → la app se cierra sola y el Setup pide UAC él solo.
 *
 * El check vive en AppLayout (monta UNA vez, corre aunque el sidebar esté
 * colapsado) y alimenta al badge y al modal como props puras.
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
 * Badge de actualización disponible (sidebar). Presentacional: el check lo
 * hace AppLayout y llega por prop — así corre también con el sidebar
 * colapsado (antes el badge era el dueño del check y colapsarlo mataba
 * ambos de un golpe).
 */
export function UpdateCenter({ check }: { check: AppUpdateCheckResult | null }) {
  if (!check?.available) return null;

  return (
    <a
      href="#/settings"
      className="flex shrink-0 items-center gap-1.5 rounded-full px-3 py-1 text-xs font-semibold transition-colors"
      style={{
        background: 'color-mix(in srgb, var(--pcs-accent) 16%, transparent)',
        color: 'var(--pcs-accent)',
      }}
      title={`Versión ${check.latestVersion} disponible — Ajustes > Acerca de`}
    >
      <Sparkles size={12} aria-hidden />
      {t('pages.update.badge')}
    </a>
  );
}

/**
 * Task 027 — modal de versión nueva al arrancar. Antes el desenlace del check
 * de arranque era solo el badge del sidebar (fácil de perder); ahora, si hay
 * versión nueva, aparece UN diálogo con la versión y el camino directo a
 * Actualizaciones. Una vez por sesión: "Más tarde" lo despide hasta el
 * próximo arranque.
 *
 * No aparece si hay aviso de rotación de clave: esa es una anomalía de
 * seguridad que el panel de Actualizaciones explica con su aviso ámbar —
 * celebrar "¡versión nueva!" con la descarga bloqueada sería contradictorio.
 */
export function UpdateStartupModal({ check }: { check: AppUpdateCheckResult | null }) {
  const [dismissed, setDismissed] = useState(false);

  // Escape = "Más tarde": un diálogo respeta el teclado.
  useEffect(() => {
    if (!check?.available || check.keyRotationWarning || dismissed) return;
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setDismissed(true);
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [check?.available, check?.keyRotationWarning, dismissed]);

  if (!check?.available || check.keyRotationWarning || dismissed) return null;

  // createPortal: el diálogo se renderiza DIRECTO en <body>, fuera del
  // layout del AppLayout (flex container). position:fixed + z-[9999]
  // garantizan que flote sobre TODO — sidebar, contenido, cualquier cosa.
  // Sin esto, el modal puede quedar atrapado en el stacking context del
  // WebView2 (reporte del usuario: "solo aparece como un div en el sidebar").
  return createPortal(
    <div
      className="fixed inset-0 z-[9999] flex items-center justify-center p-4"
      style={{
        background: 'rgba(0, 0, 0, 0.55)',
        backdropFilter: 'blur(2px)',
      }}
      role="dialog"
      aria-modal="true"
      aria-labelledby="update-modal-title"
      onClick={() => setDismissed(true)}
    >
      <div
        className="w-[min(92vw,420px)] rounded-lg border p-6 shadow-2xl"
        style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
        onClick={(event) => event.stopPropagation()}
      >
        <div className="flex items-center gap-3">
          <span
            className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full"
            style={{
              background: 'color-mix(in srgb, var(--pcs-accent) 16%, transparent)',
              color: 'var(--pcs-accent)',
            }}
          >
            <Sparkles size={20} aria-hidden />
          </span>
          <h2 id="update-modal-title" className="text-base font-semibold">{t('pages.update.newVersion')}</h2>
        </div>

        <p className="mt-4 text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
          {t('pages.update.newVersionAvailable')}
        </p>
        <div className="mt-2 flex items-center gap-2">
          <span className="text-xs font-mono" style={{ color: 'var(--pcs-text-muted)' }}>
            {check.currentVersion}
          </span>
          <span style={{ color: 'var(--pcs-accent)' }}>→</span>
          <span className="text-lg font-semibold" style={{ color: 'var(--pcs-accent)' }}>
            {check.latestVersion}
          </span>
          {check.sizeBytes > 0 && (
            <span className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
              · {formatMb(check.sizeBytes)}
            </span>
          )}
        </div>

        {check.releaseNotesUrl && (
          <a
            href={check.releaseNotesUrl}
            target="_blank"
            rel="noreferrer"
            className="mt-2 inline-flex cursor-pointer items-center gap-1 text-xs underline-offset-2 transition-colors hover:underline"
            style={{ color: 'var(--pcs-accent)' }}
          >
{t('pages.winget.releaseNotes')}
            <ExternalLink size={10} aria-hidden className="opacity-70" />
          </a>
        )}

        <div className="mt-5 flex flex-wrap justify-end gap-2">
          <button
            type="button"
            onClick={() => setDismissed(true)}
            className="rounded-md border px-3 py-2 text-sm font-medium cursor-pointer transition-colors"
            style={{ borderColor: 'var(--pcs-border)', color: 'var(--pcs-text)' }}
          >
{t('common.later')}
          </button>
          <button
            type="button"
            onClick={() => {
              setDismissed(true);
              location.hash = '#/settings';
            }}
            className="flex items-center gap-2 rounded-md px-4 py-2 text-sm font-medium cursor-pointer transition-colors"
            style={{ background: 'var(--pcs-accent)', color: '#fff' }}
          >
            <Download size={14} aria-hidden />
{t('pages.winget.upgradeAll')}
          </button>
        </div>

        <p className="mt-3 text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
{t('pages.update.installNote')}
        </p>
      </div>
    </div>,
    document.body,
  );
}

/**
 * Panel de actualizaciones para la página de Ajustes: check manual, versión
 * actual vs. disponible, descarga con progreso (Job) y el botón final de
 * instalación (el único que ejecuta algo).
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
      setNotice({ kind: 'error', text: e instanceof BridgeError ? e.message : t('pages.update.checkFailed') });
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
          setNotice({ kind: 'success', text: t('pages.update.downloadComplete') });
        } else {
          setDownload({ phase: 'idle' });
          setNotice({ kind: 'error', text: 'La descarga terminó sin resultado.' });
        }
      } else if (payload.status === 'failed') {
        setDownload({ phase: 'failed', error: payload.error ?? t('pages.winget.downloadFailed') });
        setNotice({ kind: 'error', text: payload.error ?? t('pages.winget.downloadFailed') });
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
      setNotice({ kind: 'error', text: e instanceof BridgeError ? e.message : t('pages.winget.downloadFailed') });
    }
  };

  const install = async () => {
    if (download.phase !== 'ready') return;
    setInstalling(true);
    try {
      await bridge.call(OperationIds.updateInstall, { path: download.path }, INSTALL_TIMEOUT);
      setNotice({ kind: 'success', text: 'Instalador lanzado. La app se cerrará…' });
      // La app se cierra sola (onInstalled → MainForm.Close). Si no se cerró,
      // el usuario puede seguirla usando; el mensaje ya explica el desenlace.
    } catch (e) {
      setNotice({ kind: 'error', text: e instanceof BridgeError ? e.message : t('pages.update.installFailed') });
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
      aria-label={t('pages.winget.updates')}
    >
      <div className="flex items-center justify-between gap-3">
        <h2 className="text-sm font-semibold" aria-label={t('pages.winget.updates')}>{t('pages.winget.updates')}</h2>
        <button
          type="button"
          onClick={() => void runCheck()}
          disabled={checking}
          className="flex shrink-0 items-center gap-1.5 rounded-md border px-2.5 py-1.5 text-xs font-medium cursor-pointer transition-colors disabled:cursor-not-allowed disabled:opacity-50"
          style={{ borderColor: 'var(--pcs-border)', color: 'var(--pcs-text)' }}
        >
          <RefreshCw size={12} aria-hidden className={checking ? 'animate-spin' : undefined} />
          {checking ? t('pages.update.checking') : t('pages.update.checkButton')}
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

      {check?.keyRotationWarning && (
        <div
          role="alert"
          className="flex items-start gap-2 rounded-md border px-3 py-2.5 text-xs"
          style={{
            borderColor: 'color-mix(in srgb, var(--pcs-warning) 55%, transparent)',
            background: 'color-mix(in srgb, var(--pcs-warning) 10%, transparent)',
            color: 'var(--pcs-warning)',
          }}
        >
          <AlertTriangle size={14} aria-hidden className="mt-0.5 shrink-0" />
          <div>
            <p className="font-semibold">{t('pages.update.signatureRotated')}</p>
            <p className="mt-0.5" style={{ color: 'var(--pcs-text-muted)' }}>
              {check.keyRotationWarning} La descarga queda bloqueada hasta que
              verifiques el fingerprint en{' '}
              <span className="font-mono">aes-studio.dev</span>.
            </p>
          </div>
        </div>
      )}

      {check && !available && !check.error && (
        <p className="flex items-center gap-1.5 text-xs" style={{ color: 'var(--pcs-success)' }}>
          <ShieldCheck size={13} aria-hidden />
          {t('pages.update.upToDate', { version: check.currentVersion })}
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
            <span style={{ color: 'var(--pcs-accent)' }}>→</span>
            <span className="text-sm font-semibold" style={{ color: 'var(--pcs-accent)' }}>
              {check.latestVersion}
            </span>
            <span className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
              {check.sizeBytes > 0 ? `· ${formatMb(check.sizeBytes)}` : ''}
            </span>
            {check.releaseNotesUrl && (
              <a
                href={check.releaseNotesUrl}
                target="_blank"
                rel="noreferrer"
                className="ml-auto flex cursor-pointer items-center gap-1 text-xs underline-offset-2 transition-colors hover:underline"
                style={{ color: 'var(--pcs-accent)' }}
              >
                {t('pages.winget.releaseNotes')}
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
                {t('pages.update.downloading', { percent: Math.round(download.progress * 100) })}
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
                {installing ? t('pages.update.launching') : t('pages.winget.installNow')}
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
              t('pages.update.installNote')
              tu configuración y datos no se tocan. Windows puede pedir permiso (UAC).
            </p>
          )}
        </>
      )}
    </section>
  );
}




