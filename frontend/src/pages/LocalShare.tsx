import { useCallback, useEffect, useRef, useState } from 'react';
import {
  FolderOpen,
  Globe,
  HardDrive,
  MonitorSmartphone,
  Play,
  RefreshCw,
  Send,
  ShieldAlert,
  Square,
  Users,
  XCircle,
  CheckCircle2,
} from 'lucide-react';
import { bridge, BridgeError } from '../services/pcsuiteBridge';
import { OperationIds, EventIds } from '../types/bridge';
import type {
  LocalShareStatusResult,
  LocalShareFirewallStatusResult,
  LocalSharePairResult,
  LocalSharePairRequestedPayload,
  LocalSharePairFinishedPayload,
  SystemPickFilesResult,
  SharedDeviceCollectionResult,
  SharedDeviceResult,
  ShareTransferCollectionResult,
  ShareTransferResult,
  LocalShareIncomingRequestPayload,
  LocalShareTransferProgressPayload,
  LocalShareTransferFinishedPayload,
} from '../types/bridge';

/**
 * Página "Local Share" (Task 016): transferencia de archivos en LAN.
 * Sin nube ni servicios externos — directo entre equipos con PCSuite.
 */

const READ_TIMEOUT = 30_000;
const SEND_TIMEOUT = 300_000;

type IncomingRequest = LocalShareIncomingRequestPayload;
type ProgressState = LocalShareTransferProgressPayload;
type FinishedState = LocalShareTransferFinishedPayload;

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  return `${(bytes / (1024 * 1024 * 1024)).toFixed(2)} GB`;
}

function statusColor(status: string): string {
  switch (status) {
    case 'completed': return 'var(--pcs-success)';
    case 'transferring': return 'var(--pcs-accent)';
    case 'paused': return 'var(--pcs-warning)';
    case 'failed': return 'var(--pcs-danger)';
    case 'cancelled':
    case 'rejected': return 'var(--pcs-text-muted)';
    default: return 'var(--pcs-warning)';
  }
}

export function LocalShare() {
  const [status, setStatus] = useState<LocalShareStatusResult | null>(null);
  const [devices, setDevices] = useState<SharedDeviceResult[]>([]);
  const [transfers, setTransfers] = useState<ShareTransferResult[]>([]);
  const [incomingRequest, setIncomingRequest] = useState<IncomingRequest | null>(null);
  const [selectedDevice, setSelectedDevice] = useState<string | null>(null);
  const [sending, setSending] = useState(false);
  const [notice, setNotice] = useState<{ kind: 'success' | 'error'; text: string } | null>(null);
  const [firewall, setFirewall] = useState<LocalShareFirewallStatusResult | null>(null);
  const [firewallBusy, setFirewallBusy] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  /** Emparejamiento iniciado por ESTE equipo: código visible mientras espera. */
  const [pairing, setPairing] = useState<LocalSharePairResult | null>(null);
  /** Solicitud de emparejamiento entrante (banner de decisión). */
  const [pairRequest, setPairRequest] = useState<LocalSharePairRequestedPayload | null>(null);
  /** Pairings cancelados por este usuario: su evento final no debe mostrarse como error. */
  const cancelledPairings = useRef<Set<string>>(new Set());

  // ------------------------------------------------------------------
  // Carga inicial
  // ------------------------------------------------------------------

  const refresh = useCallback(async () => {
    setRefreshing(true);
    try {
      const [statusResult, devicesResult, transfersResult] = await Promise.all([
        bridge.call<LocalShareStatusResult>(OperationIds.localShareStatus, undefined, READ_TIMEOUT),
        bridge.call<SharedDeviceCollectionResult>(OperationIds.localShareDevices, undefined, READ_TIMEOUT),
        bridge.call<ShareTransferCollectionResult>(OperationIds.localShareTransfers, undefined, READ_TIMEOUT),
      ]);
      setStatus(statusResult);
      setDevices(devicesResult.devices ?? []);
      setTransfers(transfersResult.transfers ?? []);
    } catch (error) {      setNotice({ kind: 'error', text: error instanceof BridgeError ? error.message : 'No se pudo consultar Local Share.' });
    } finally {
      setRefreshing(false);
    }
  }, []);

  useEffect(() => { void refresh(); }, [refresh]);

  // Notificaciones autodestructivas: los éxitos duran 5 s, los errores 9 s
  // (dar tiempo a leerlos). Cada notificación nueva reinicia el reloj; la
  // acción manual (setNotice(null)) también limpia el temporizador.
  useEffect(() => {
    if (!notice) return;
    const ms = notice.kind === 'success' ? 5_000 : 9_000;
    const timer = setTimeout(() => setNotice(null), ms);
    return () => clearTimeout(timer);
  }, [notice]);

  // Refresco ligero de solo-dispositivos (para eventos device-changed, que
  // llegan cada pocos segundos con los anuncios multicast).
  const refreshDevices = useCallback(async () => {
    try {
      const devicesResult = await bridge.call<SharedDeviceCollectionResult>(
        OperationIds.localShareDevices, undefined, READ_TIMEOUT);
      setDevices(devicesResult.devices ?? []);
    } catch { /* best-effort */ }
  }, []);

  // ------------------------------------------------------------------
  // Eventos
  // ------------------------------------------------------------------

  useEffect(() => {
    const unsubIncoming = bridge.on<IncomingRequest>(EventIds.localShareIncomingRequest, (payload) => {
      setIncomingRequest(payload);
    });
    const unsubProgress = bridge.on<ProgressState>(EventIds.localShareTransferProgress, (payload) => {
      setTransfers((prev) => prev.map((t) =>
        t.transferId === payload.transferId
          ? { ...t, transferredBytes: payload.transferredBytes, totalBytes: payload.totalBytes }
          : t));
    });
    const unsubFinished = bridge.on<FinishedState>(EventIds.localShareTransferFinished, (payload) => {
      setTransfers((prev) => prev.map((t) =>
        t.transferId === payload.transferId
          ? { ...t, status: payload.status, errorMessage: payload.errorMessage }
          : t));
      if (payload.status === 'completed') {
        setNotice({ kind: 'success', text: 'Transferencia completada.' });
      } else if (payload.status === 'failed') {
        setNotice({ kind: 'error', text: payload.errorMessage ?? 'Transferencia fallida.' });
      }
      setIncomingRequest(null);
    });

    // Emparejamiento entrante (lado receptor): banner con el código.
    const unsubPairRequested = bridge.on<LocalSharePairRequestedPayload>(
      EventIds.localSharePairRequested, (payload) => setPairRequest(payload));

    // Desenlace del emparejamiento que ESTE equipo inició.
    const unsubPairFinished = bridge.on<LocalSharePairFinishedPayload>(
      EventIds.localSharePairFinished, (payload) => {
        setPairing((current) =>
          current && current.requestId === payload.requestId ? null : current);
        if (cancelledPairings.current.has(payload.requestId)) {
          // Cancelado por este usuario: sin aviso (él ya lo sabe).
          cancelledPairings.current.delete(payload.requestId);
          void refreshDevices();
          return;
        }
        if (payload.outcome === 'accepted') {
          setNotice({ kind: 'success', text: `Emparejado con ${payload.displayName ?? 'el otro equipo'}.` });
        } else if (payload.outcome === 'rejected') {
          setNotice({ kind: 'error', text: 'El otro equipo rechazó el emparejamiento.' });
        } else {
          setNotice({ kind: 'error', text: payload.errorMessage ?? 'El emparejamiento falló.' });
        }
        void refreshDevices();
      });

    // Dispositivos: descubrimientos, emparejamientos y revocaciones.
    const unsubDeviceChanged = bridge.on<LocalSharePairRequestedPayload & { deviceId: string }>(
      EventIds.localShareDeviceChanged, () => { void refreshDevices(); });

    return () => {
      unsubIncoming();
      unsubProgress();
      unsubFinished();
      unsubPairRequested();
      unsubPairFinished();
      unsubDeviceChanged();
    };
  }, [refreshDevices]);

  // ------------------------------------------------------------------
  // Acciones
  // ------------------------------------------------------------------

  const startService = async () => {
    try { await bridge.call(OperationIds.localShareStart, undefined, READ_TIMEOUT); await refresh(); }
    catch (e) { setNotice({ kind: 'error', text: e instanceof BridgeError ? e.message : 'No se pudo iniciar.' }); }
  };

  const stopService = async () => {
    try { await bridge.call(OperationIds.localShareStop, undefined, READ_TIMEOUT); await refresh(); }
    catch (e) { setNotice({ kind: 'error', text: e instanceof BridgeError ? e.message : 'No se pudo detener.' }); }
  };

  const sendFiles = async (paths: string[]) => {
    if (!selectedDevice || paths.length === 0) return;
    setSending(true);
    setNotice(null);
    try {
      await bridge.call(OperationIds.localShareSend, { deviceId: selectedDevice, paths }, SEND_TIMEOUT);
      setNotice({ kind: 'success', text: 'Transferencia iniciada. Esperando aprobación del receptor.' });
      await refresh();
    } catch (e) {
      setNotice({ kind: 'error', text: e instanceof BridgeError ? e.message : 'No se pudo enviar.' });
    } finally { setSending(false); }
  };

  /**
   * Selector NATIVO de Windows (system.pickFiles): el WebView2 no expone
   * rutas en <input type=file> — solo nombres sueltos que no resuelven a
   * nada en el backend. El diálogo puede estar abierto minutos: timeout
   * generoso (el temporizador frontend por defecto son 15 s).
   */
  const pickAndSend = async () => {
    setNotice(null);
    try {
      const result = await bridge.call<SystemPickFilesResult>(
        OperationIds.systemPickFiles, undefined, 10 * 60_000);
      if (result.cancelled || result.paths.length === 0) return;
      await sendFiles(result.paths);
    } catch (e) {
      setNotice({ kind: 'error', text: e instanceof BridgeError ? e.message : 'No se pudo abrir el selector de archivos.' });
    }
  };

  const acceptIncoming = async () => {
    if (!incomingRequest) return;
    try {
      await bridge.call(OperationIds.localShareAccept, { transferId: incomingRequest.transferId }, READ_TIMEOUT);
      setNotice({ kind: 'success', text: 'Solicitud aceptada. Recibiendo archivos…' });
      setIncomingRequest(null);
    } catch (e) {
      setNotice({ kind: 'error', text: e instanceof BridgeError ? e.message : 'No se pudo aceptar.' });
    }
  };

  const rejectIncoming = async () => {
    if (!incomingRequest) return;
    try {
      await bridge.call(OperationIds.localShareReject, { transferId: incomingRequest.transferId }, READ_TIMEOUT);
      setIncomingRequest(null);
      setNotice({ kind: 'error', text: 'Solicitud rechazada.' });
    } catch { /* best-effort */ }
  };

  const cancelTransfer = async (transferId: string) => {
    try { await bridge.call(OperationIds.localShareCancelTransfer, { transferId }, READ_TIMEOUT); await refresh(); }
    catch { /* best-effort */ }
  };

  const pauseTransfer = async (transferId: string) => {
    try { await bridge.call(OperationIds.localSharePauseTransfer, { transferId }, READ_TIMEOUT); await refresh(); }
    catch (e) { setNotice({ kind: 'error', text: e instanceof BridgeError ? e.message : 'No se pudo pausar.' }); }
  };

  const resumeTransfer = async (transferId: string) => {
    try { await bridge.call(OperationIds.localShareResumeTransfer, { transferId }, READ_TIMEOUT); await refresh(); }
    catch (e) { setNotice({ kind: 'error', text: e instanceof BridgeError ? e.message : 'No se pudo reanudar.' }); }
  };

  // ------------------------------------------------------------------
  // Emparejamiento (comparación numérica estilo Bluetooth)
  // ------------------------------------------------------------------

  const pairDevice = async (deviceId: string) => {
    setNotice(null);
    try {
      const initiation = await bridge.call<LocalSharePairResult>(
        OperationIds.localSharePair, { deviceId }, READ_TIMEOUT);
      setPairing(initiation);
    } catch (e) {
      setNotice({ kind: 'error', text: e instanceof BridgeError ? e.message : 'No se pudo iniciar el emparejamiento.' });
    }
  };

  const acceptPair = async () => {
    if (!pairRequest) return;
    try {
      await bridge.call(OperationIds.localSharePairAccept, { requestId: pairRequest.requestId }, READ_TIMEOUT);
      setPairRequest(null);
      setNotice({ kind: 'success', text: 'Emparejamiento completado.' });
      await refreshDevices();
    } catch (e) {
      setNotice({ kind: 'error', text: e instanceof BridgeError ? e.message : 'No se pudo aceptar.' });
      setPairRequest(null);
    }
  };

  const rejectPair = async () => {
    if (!pairRequest) return;
    try { await bridge.call(OperationIds.localSharePairReject, { requestId: pairRequest.requestId }, READ_TIMEOUT); }
    catch { /* best-effort */ }
    setPairRequest(null);
  };

  /**
   * Cancelar el emparejamiento que ESTE equipo inició. El desenlace
   * (Failed/"cancelado") llegará igual por evento, pero lo hemos pedido
   * nosotros: se anota el requestId para NO mostrar el aviso de error.
   */
  const cancelPairing = async () => {
    if (!pairing) return;
    cancelledPairings.current.add(pairing.requestId);
    setPairing(null);
    try { await bridge.call(OperationIds.localSharePairCancel, { requestId: pairing.requestId }, READ_TIMEOUT); }
    catch { /* best-effort: expira solo a los 120 s */ }
  };

  /**
   * Auto-arranque de Local Share con la app (PATCH de configuración).
   */
  const toggleAutoStart = async () => {
    const next = !(status?.autoStartService ?? false);
    try {
      await bridge.call(OperationIds.localShareConfig, { autoStartService: next }, READ_TIMEOUT);
      await refresh();
      setNotice({
        kind: 'success',
        text: next
          ? 'Local Share se iniciará solo al abrir PCSuite.'
          : 'Local Share ya no se iniciará con la app.',
      });
    } catch (e) {
      setNotice({ kind: 'error', text: e instanceof BridgeError ? e.message : 'No se pudo guardar la preferencia.' });
    }
  };

  // ------------------------------------------------------------------
  // Firewall (diagnóstico sin elevación; regla con UAC puntual)
  // ------------------------------------------------------------------

  const refreshFirewall = useCallback(async () => {
    try {
      setFirewall(await bridge.call<LocalShareFirewallStatusResult>(
        OperationIds.localShareFirewallStatus, undefined, READ_TIMEOUT));
    } catch {
      // Best-effort: el diagnóstico nunca rompe la página.
      setFirewall(null);
    }
  }, []);

  useEffect(() => {
    if (status?.status === 'running') void refreshFirewall();
    else setFirewall(null);
  }, [status?.status, refreshFirewall]);

  const allowFirewall = async () => {
    setFirewallBusy(true);
    setNotice(null);
    try {
      await bridge.call(OperationIds.localShareFirewallRule, undefined, 120_000);
      await refreshFirewall();
      setNotice({ kind: 'success', text: 'Regla de firewall creada. Ya puedes recibir archivos.' });
    } catch (e) {
      setNotice({ kind: 'error', text: e instanceof BridgeError ? e.message : 'No se pudo crear la regla.' });
    } finally {
      setFirewallBusy(false);
    }
  };

  /**
   * ¿Parece una ruta absoluta de Windows? El WebView2 no expone "File.path"
   * (extensión de Electron): arrastrar desde el Explorador solo entrega
   * NOMBRES sueltos. Si llega algo sin formato de ruta real, no lo
   * mandamos al backend (fallaría con "no se encontraron archivos").
   */
  const esRutaAbsoluta = (p: string) =>
    /^[a-zA-Z]:[\\/]/.test(p) || p.startsWith('\\\\');

  const onDrop = async (event: React.DragEvent) => {
    event.preventDefault();
    const paths: string[] = [];
    for (const item of event.dataTransfer.items) {
      if (item.kind === 'file') {
        const file = item.getAsFile();
        const path = (file as unknown as { path?: string }).path;
        if (file && path) paths.push(path);
      }
    }
    if (paths.length === 0) {
      setNotice({
        kind: 'error',
        text: 'El navegador embebido no entrega rutas al arrastrar. Usa «Seleccionar archivos».',
      });
      return;
    }
    if (paths.some((p) => !esRutaAbsoluta(p))) {
      setNotice({
        kind: 'error',
        text: 'El navegador embebido no entrega rutas al arrastrar. Usa «Seleccionar archivos».',
      });
      return;
    }
    await sendFiles(paths);
  };

  // ------------------------------------------------------------------
  // Render
  // ------------------------------------------------------------------

  const isRunning = status?.status === 'running';

  return (
    <div className="space-y-6">
      <header className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold">Local Share</h1>
          <p className="mt-1 text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
            Envía archivos entre equipos con PCSuite en tu red local. Sin nube, sin servicios externos.
          </p>
        </div>
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={() => void refresh()}
            disabled={refreshing}
            className="flex items-center gap-2 rounded-md px-3 py-2 text-sm font-medium transition-colors cursor-pointer disabled:cursor-not-allowed disabled:opacity-50"
            style={{ background: 'var(--pcs-surface-2)', color: 'var(--pcs-text)' }}
          >
            <RefreshCw size={14} aria-hidden className={refreshing ? 'animate-spin' : undefined} />
            {refreshing ? 'Actualizando…' : 'Actualizar'}
          </button>
          <button
            type="button"
            onClick={() => void (isRunning ? stopService() : startService())}
            className="flex items-center gap-2 rounded-md px-3 py-2 text-sm font-medium transition-colors cursor-pointer"
            style={{ background: isRunning ? 'var(--pcs-danger)' : 'var(--pcs-accent)', color: '#fff' }}
          >
            {isRunning ? <Square size={14} aria-hidden /> : <Play size={14} aria-hidden />}
            {isRunning ? 'Detener' : 'Iniciar'}
          </button>
        </div>
      </header>

      {notice && (
        <div
          className="rounded-md border px-4 py-3 text-sm"
          style={{
            borderColor: notice.kind === 'success' ? 'var(--pcs-success)' : 'var(--pcs-danger)',
            color: notice.kind === 'success' ? 'var(--pcs-success)' : 'var(--pcs-danger)',
          }}
          role={notice.kind === 'error' ? 'alert' : 'status'}
        >
          {notice.text}
        </div>
      )}

      {/* Firewall: sin las reglas entrantes, otros equipos no pueden conectar
          (TCP: transferencia) ni verse (UDP multicast: detección). */}
      {isRunning && firewall &&
        (firewall.ruleExists === false || firewall.portMatches === false || firewall.discoveryPortMatches === false) && (
        <div
          className="flex flex-wrap items-center justify-between gap-3 rounded-md border px-4 py-3 text-sm"
          style={{ borderColor: 'var(--pcs-warning)', color: 'var(--pcs-warning)' }}
          role="status"
        >
          <span>
            {firewall.ruleExists === false
              ? 'El firewall de Windows podría bloquear conexiones entrantes de otros equipos.'
              : firewall.portMatches === false
                ? 'La regla del firewall existe pero no cubre el puerto de transferencia actual.'
                : 'La regla del firewall no permite la detección automática de equipos (UDP 5354).'}
          </span>
          <button
            type="button"
            onClick={() => void allowFirewall()}
            disabled={firewallBusy}
            className="flex items-center gap-2 rounded-md px-3 py-1.5 text-sm font-medium transition-colors cursor-pointer disabled:cursor-not-allowed disabled:opacity-50"
            style={{ background: 'var(--pcs-warning)', color: '#1a1a1a' }}
          >
            <ShieldAlert size={14} aria-hidden />
            {firewallBusy ? 'Pidiendo permiso…' : 'Permitir en el firewall'}
          </button>
        </div>
      )}

      {/* Solicitud entrante de emparejamiento (lado receptor): comparar y decidir. */}
      {pairRequest && (
        <section
          className="rounded-lg border p-4"
          style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-accent)' }}
        >
          <h2 className="mb-2 flex items-center gap-2 text-sm font-semibold">
            <Users size={16} aria-hidden style={{ color: 'var(--pcs-accent)' }} />
            «{pairRequest.peerDisplayName}» quiere emparejarse con este equipo
          </h2>
          <p className="mb-3 text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
            Compara este código con el que muestra la pantalla del otro equipo.
            Si coinciden, acepta:
          </p>
          <p
            className="mb-3 text-3xl font-bold font-mono tracking-[0.3em]"
            style={{ color: 'var(--pcs-accent)' }}
          >
            {pairRequest.code}
          </p>
          <div className="flex gap-2">
            <button
              type="button"
              onClick={() => void acceptPair()}
              className="flex items-center gap-2 rounded-md px-4 py-2 text-sm font-medium cursor-pointer"
              style={{ background: 'var(--pcs-success)', color: '#fff' }}
            >
              <CheckCircle2 size={14} aria-hidden /> Aceptar
            </button>
            <button
              type="button"
              onClick={() => void rejectPair()}
              className="flex items-center gap-2 rounded-md px-4 py-2 text-sm font-medium cursor-pointer"
              style={{ background: 'var(--pcs-danger)', color: '#fff' }}
            >
              <XCircle size={14} aria-hidden /> Rechazar
            </button>
          </div>
        </section>
      )}

      {/* Emparejamiento iniciado por este equipo: código mientras espera. */}
      {pairing && (
        <section
          className="rounded-lg border p-4"
          style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
        >
          <h2 className="mb-2 text-sm font-semibold">
            Emparejando con «{pairing.peerDisplayName}»…
          </h2>
          <p className="mb-3 text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
            Compara este código con el de la pantalla del otro equipo y acepta allí.
            Esperando confirmación…
          </p>
          <div className="flex items-center justify-between gap-4">
            <p
              className="text-3xl font-bold font-mono tracking-[0.3em]"
              style={{ color: 'var(--pcs-accent)' }}
            >
              {pairing.code}
            </p>
            <button
              type="button"
              onClick={() => void cancelPairing()}
              className="flex items-center gap-2 rounded-md px-3 py-1.5 text-sm font-medium cursor-pointer transition-colors"
              style={{ color: 'var(--pcs-danger)' }}
            >
              <XCircle size={14} aria-hidden /> Cancelar
            </button>
          </div>
        </section>
      )}

      {/* Estado del servicio */}
      <section className="rounded-lg border p-4" style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}>
        <div className="flex items-center gap-4">
          <span
            className="rounded-full px-3 py-1 text-xs font-semibold"
            style={{
              background: isRunning ? 'color-mix(in srgb, var(--pcs-success) 18%, transparent)' : 'var(--pcs-surface-2)',
              color: isRunning ? 'var(--pcs-success)' : 'var(--pcs-text-muted)',
            }}
          >
            {status?.status === 'running' ? '● Activo' : '○ Detenido'}
          </span>
          {isRunning && (
            <span className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
              Puerto {status?.port} · {status?.displayName}
            </span>
          )}
          <button
            type="button"
            onClick={() => void toggleAutoStart()}
            className="ml-auto rounded-full px-3 py-1 text-xs font-medium cursor-pointer transition-colors"
            style={{
              background: status?.autoStartService
                ? 'color-mix(in srgb, var(--pcs-accent) 16%, transparent)'
                : 'var(--pcs-surface-2)',
              color: status?.autoStartService ? 'var(--pcs-accent)' : 'var(--pcs-text-muted)',
            }}
            title="Iniciar Local Share automáticamente al abrir PCSuite"
          >
            {status?.autoStartService ? '↻ Auto-inicio ON' : '↻ Auto-inicio OFF'}
          </button>
        </div>
      </section>

      {/* Solicitud entrante */}
      {incomingRequest && (
        <section
          className="rounded-lg border p-4"
          style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-warning)' }}
        >
          <h2 className="mb-2 flex items-center gap-2 text-sm font-semibold">
            <MonitorSmartphone size={16} aria-hidden style={{ color: 'var(--pcs-warning)' }} />
            Solicitud entrante de {incomingRequest.peerDisplayName}
          </h2>
          <p className="mb-3 text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
            {incomingRequest.itemCount} archivo(s) · {formatBytes(incomingRequest.totalBytes)}
          </p>
          <div className="mb-3 max-h-40 overflow-y-auto">
            {incomingRequest.items.slice(0, 5).map((item) => (
              <p key={item.itemId} className="truncate text-xs font-mono" style={{ color: 'var(--pcs-text-muted)' }}>
                {item.relativePath} · {formatBytes(item.expectedSize)}
              </p>
            ))}
            {incomingRequest.items.length > 5 && (
              <p className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
                …y {incomingRequest.items.length - 5} más
              </p>
            )}
          </div>
          <div className="flex gap-2">
            <button
              type="button"
              onClick={() => void acceptIncoming()}
              className="flex items-center gap-2 rounded-md px-4 py-2 text-sm font-medium cursor-pointer"
              style={{ background: 'var(--pcs-success)', color: '#fff' }}
            >
              <CheckCircle2 size={14} aria-hidden /> Aceptar
            </button>
            <button
              type="button"
              onClick={() => void rejectIncoming()}
              className="flex items-center gap-2 rounded-md px-4 py-2 text-sm font-medium cursor-pointer"
              style={{ background: 'var(--pcs-danger)', color: '#fff' }}
            >
              <XCircle size={14} aria-hidden /> Rechazar
            </button>
          </div>
        </section>
      )}

      {/* Dispositivos + enviar */}
      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        {/* Dispositivos */}
        <section className="rounded-lg border p-4" style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}>
          <h2 className="mb-3 flex items-center gap-2 text-sm font-semibold">
            <Users size={15} aria-hidden /> Dispositivos
          </h2>
          {devices.length === 0 ? (
            <p className="text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
              Ningún equipo cercano por ahora. Ambos equipos deben tener Local
              Share activo: se detectan solos en la red y aparecerán aquí para emparejar.
            </p>
          ) : (
            <ul className="space-y-2">
              {devices.map((device) => {
                const selectable = device.trust === 'paired' && device.online;
                return (
                <li
                  key={device.deviceId}
                  className={
                    'flex items-center gap-2 rounded-md border px-3 py-2 transition-colors '
                    + (selectable ? 'cursor-pointer ' : 'cursor-default ')
                    + (device.online ? '' : 'opacity-50')
                  }
                  onClick={() =>
                    selectable && setSelectedDevice(device.deviceId === selectedDevice ? null : device.deviceId)}
                  style={{
                    background: selectedDevice === device.deviceId ? 'var(--pcs-surface-2)' : 'transparent',
                    borderColor: selectedDevice === device.deviceId ? 'var(--pcs-accent)' : 'var(--pcs-border)',
                  }}
                >
                  <Globe
                    size={14}
                    aria-hidden
                    style={{ color: !device.online ? 'var(--pcs-warning)' : device.trust === 'paired' ? 'var(--pcs-success)' : 'var(--pcs-text-muted)' }}
                  />
                  <span className="text-sm font-medium">{device.displayName}</span>
                  {device.address && (
                    <span className="text-xs font-mono" style={{ color: 'var(--pcs-text-muted)' }}>
                      {device.address}:{device.port}
                    </span>
                  )}
                  <span
                    className="ml-auto rounded-full px-2 py-0.5 text-xs"
                    style={{
                      background: device.trust === 'paired' ? 'color-mix(in srgb, var(--pcs-success) 16%, transparent)' : 'var(--pcs-surface-2)',
                      color: device.trust === 'paired' ? 'var(--pcs-success)' : 'var(--pcs-text-muted)',
                    }}
                  >
                    {device.trust === 'paired' ? 'Emparejado' : 'Desconocido'}
                  </span>
                  {!device.online && (
                    <span
                      className="rounded-full px-2 py-0.5 text-xs"
                      style={{
                        background: 'color-mix(in srgb, var(--pcs-warning) 16%, transparent)',
                        color: 'var(--pcs-warning)',
                      }}
                    >
                      Sin señal
                    </span>
                  )}
                  {device.trust !== 'paired' && device.online && (
                    <button
                      type="button"
                      onClick={(e) => { e.stopPropagation(); void pairDevice(device.deviceId); }}
                      className="rounded-md px-3 py-1 text-xs font-medium cursor-pointer transition-colors"
                      style={{ background: 'var(--pcs-accent)', color: '#fff' }}
                    >
                      Emparejar
                    </button>
                  )}
                </li>
                );
              })}
            </ul>
          )}
        </section>

        {/* Enviar */}
        <section
          className="rounded-lg border-2 border-dashed p-8"
          style={{ borderColor: 'var(--pcs-border)', background: 'var(--pcs-surface)' }}
          onDragOver={(e) => e.preventDefault()}
          onDrop={onDrop}
        >
          <div className="flex flex-col items-center justify-center text-center">
            <FolderOpen size={48} aria-hidden className="mb-4 opacity-30" style={{ color: 'var(--pcs-accent)' }} />
            <h3 className="text-lg font-medium mb-1">
              {selectedDevice
                ? 'Elige archivos para enviar'
                : 'Selecciona un dispositivo emparejado y en línea'}
            </h3>
            <p className="text-sm mb-4" style={{ color: 'var(--pcs-text-muted)' }}>
              Selector nativo de Windows con multiselección. Las transferencias
              van directas entre equipos por la red local.
            </p>
            <button
              type="button"
              disabled={!selectedDevice || sending}
              onClick={() => void pickAndSend()}
              className="flex items-center gap-2 rounded-md px-4 py-2 text-sm font-medium cursor-pointer transition-colors disabled:opacity-40"
              style={{ background: 'var(--pcs-accent)', color: '#fff' }}
            >
              <Send size={14} aria-hidden />
              {sending ? 'Enviando…' : 'Seleccionar archivos'}
            </button>
          </div>
        </section>
      </div>

      {/* Transferencias */}
      <section>
        <h2 className="mb-2 flex items-center gap-2 text-sm font-semibold">
          <HardDrive size={15} aria-hidden /> Transferencias
        </h2>
        {transfers.length === 0 ? (
          <p className="text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
            No hay transferencias.
          </p>
        ) : (
          <ul className="space-y-2">
            {transfers.map((t) => {
              const pct = t.totalBytes > 0 ? Math.min(100, (t.transferredBytes / t.totalBytes) * 100) : 0;
              return (
                <li
                  key={t.transferId}
                  className="rounded-lg border p-3"
                  style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
                >
                  <div className="flex items-center gap-2 text-sm">
                    <span className="font-medium">{t.peerDisplayName}</span>
                    <span className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
                      {t.direction === 'sending' ? '→ Enviando' : '← Recibiendo'} · {t.itemCount} archivo(s)
                    </span>
                    <span
                      className="ml-auto rounded-full px-2 py-0.5 text-xs font-medium"
                      style={{ background: 'var(--pcs-surface-2)', color: statusColor(t.status) }}
                    >
                      {t.status}
                    </span>
                  </div>
                  <div className="mt-2 h-2 overflow-hidden rounded-full" style={{ background: 'var(--pcs-surface-2)' }}>
                    <div
                      className="h-full transition-all duration-300"
                      style={{ width: `${pct}%`, background: statusColor(t.status) }}
                    />
                  </div>
                  <div className="mt-1 flex items-center justify-between text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
                    <span>{formatBytes(t.transferredBytes)} / {formatBytes(t.totalBytes)}</span>
                    <span className="flex items-center gap-3">
                      {t.direction === 'sending' && t.status === 'transferring' && (
                        <button
                          type="button"
                          onClick={() => void pauseTransfer(t.transferId)}
                          className="cursor-pointer text-xs font-medium"
                          style={{ color: 'var(--pcs-warning)' }}
                        >
                          Pausar
                        </button>
                      )}
                      {t.direction === 'sending' && t.status === 'paused' && (
                        <button
                          type="button"
                          onClick={() => void resumeTransfer(t.transferId)}
                          className="cursor-pointer text-xs font-medium"
                          style={{ color: 'var(--pcs-success)' }}
                        >
                          Reanudar
                        </button>
                      )}
                      {(t.status === 'transferring' || t.status === 'pendingApproval' || t.status === 'paused') && (
                        <button
                          type="button"
                          onClick={() => void cancelTransfer(t.transferId)}
                          className="cursor-pointer text-xs font-medium"
                          style={{ color: 'var(--pcs-danger)' }}
                        >
                          Cancelar
                        </button>
                      )}
                    </span>
                  </div>
                  {t.errorMessage && (
                    <p className="mt-1 text-xs" style={{ color: 'var(--pcs-danger)' }}>
                      {t.errorMessage}
                    </p>
                  )}
                </li>
              );
            })}
          </ul>
        )}
      </section>
    </div>
  );
}
