import { useCallback, useEffect, useState } from 'react';
import type { CSSProperties, ReactNode } from 'react';
import {
  AlertTriangle,
  CheckCircle2,
  Globe,
  HelpCircle,
  Network,
  RefreshCw,
  Server,
  Shield,
  ShieldCheck,
} from 'lucide-react';
import type { LucideIcon } from 'lucide-react';
import { bridge, BridgeError } from '../services/pcsuiteBridge';
import { OperationIds } from '../types/bridge';
import type { SecurityStatusResult } from '../types/bridge';

/**
 * Página "Seguridad" (Task 012 / §14): Centro de seguridad de Windows en
 * SOLO LECTURA — una única llamada security.getStatus (payload cero) trae el
 * snapshot completo (Defender, firewall por perfil, antivirus, puertos y
 * diagnóstico). No existe ninguna acción de modificación: no toca ajustes de
 * seguridad, no eleva y los campos sin datos llegan AUSENTES del JSON (nunca
 * false) para distinguir "desactivado" de "sin datos". Los fallos del sondeo
 * degradan a estados unknown con detalle legible — nunca stack traces.
 */

const READ_TIMEOUT_MS = 30_000;

type Tone = 'success' | 'danger' | 'warning' | 'muted';

/** Semáforo del Security Center (espejo de SecurityStatuses en .NET). */
const STATUS_META: Record<string, { label: string; tone: Tone }> = {
  good: { label: 'Óptimo', tone: 'success' },
  poor: { label: 'Deficiente', tone: 'danger' },
  notMonitored: { label: 'Sin supervisar', tone: 'warning' },
  snoozed: { label: 'Pausado', tone: 'warning' },
  unknown: { label: 'Sin datos', tone: 'muted' },
};

/** Diagnóstico (espejo de SecurityStatuses: ok / warn / unknown). */
const CHECK_META: Record<string, { label: string; tone: Tone }> = {
  ok: { label: 'Correcto', tone: 'success' },
  warn: { label: 'Aviso', tone: 'warning' },
  unknown: { label: 'Sin datos', tone: 'muted' },
};

function statusMeta(status: string) {
  return STATUS_META[status] ?? STATUS_META.unknown;
}

function checkMeta(status: string) {
  return CHECK_META[status] ?? CHECK_META.unknown;
}

function toneColor(tone: Tone): string {
  if (tone === 'success') return 'var(--pcs-success)';
  if (tone === 'danger') return 'var(--pcs-danger)';
  if (tone === 'warning') return 'var(--pcs-warning)';
  return 'var(--pcs-text-muted)';
}

function toneStyle(tone: Tone): CSSProperties {
  if (tone === 'muted') {
    return {
      background: 'var(--pcs-surface-2)',
      color: toneColor(tone),
      border: '1px solid var(--pcs-border)',
    };
  }
  const color = toneColor(tone);
  return {
    background: `color-mix(in srgb, ${color} 16%, transparent)`,
    color,
    border: `1px solid color-mix(in srgb, ${color} 40%, transparent)`,
  };
}

/** true / false / ausente → texto + color; el ausente es "Sin datos", nunca false. */
function boolCell(value: boolean | null | undefined, onText: string, offText: string) {
  if (value === true) return { text: onText, color: 'var(--pcs-success)' };
  if (value === false) return { text: offText, color: 'var(--pcs-danger)' };
  return { text: 'Sin datos', color: 'var(--pcs-text-muted)' };
}

function errorMessage(error: unknown, fallback: string): string {
  return error instanceof BridgeError ? error.message : fallback;
}

function StatusBadge(props: { status: string }) {
  const meta = statusMeta(props.status);
  return (
    <span
      className="whitespace-nowrap rounded-full px-2 py-0.5 text-xs font-medium"
      style={toneStyle(meta.tone)}
    >
      {meta.label}
    </span>
  );
}

function Card(props: { label: string; icon: LucideIcon; status: string; children: ReactNode }) {
  const Icon = props.icon;
  return (
    <section
      className="rounded-lg border p-4"
      style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
      aria-label={props.label}
    >
      <div className="flex items-center justify-between gap-2">
        <h2 className="flex items-center gap-2 text-sm font-semibold">
          <Icon size={16} aria-hidden style={{ color: 'var(--pcs-accent)' }} />
          {props.label}
        </h2>
        <StatusBadge status={props.status} />
      </div>
      <dl className="mt-3 space-y-1.5 text-xs">{props.children}</dl>
    </section>
  );
}

function Row(props: { label: string; value: string; color?: string }) {
  return (
    <div className="flex items-center justify-between gap-3">
      <dt style={{ color: 'var(--pcs-text-muted)' }}>{props.label}</dt>
      <dd className="text-right font-medium" style={{ color: props.color ?? 'var(--pcs-text)' }}>
        {props.value}
      </dd>
    </div>
  );
}

/** Skeleton para tarjeta de estado */
function StatusCardSkeleton() {
  return (
    <section className="rounded-lg border p-4" style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}>
      <div className="flex items-center justify-between gap-2">
        <div className="pcs-skeleton h-5 w-32 rounded mb-3" />
        <div className="pcs-skeleton h-5 w-20 rounded" />
      </div>
      <dl className="mt-3 space-y-1.5 text-xs">
        <div className="pcs-skeleton h-4 w-1/2 rounded" />
        <div className="pcs-skeleton h-4 w-1/3 rounded" />
        <div className="pcs-skeleton h-4 w-1/4 rounded" />
      </dl>
    </section>
  );
}

/** Skeleton para diagnóstico item */
function DiagnosticSkeleton() {
  return (
    <li className="flex items-start gap-3 py-2.5 text-sm">
      <div className="pcs-skeleton h-4 w-4 rounded mt-0.5 shrink-0" />
      <div className="min-w-0 flex-1">
        <div className="pcs-skeleton h-4 w-3/4 rounded mb-1" />
        <div className="pcs-skeleton h-3 w-1/2 rounded" />
      </div>
      <div className="pcs-skeleton h-5 w-20 rounded" />
    </li>
  );
}

/** Skeleton para tabla de puertos */
function PortsTableSkeleton() {
  return (
    <div className="mt-3 max-h-72 overflow-auto rounded-md border" style={{ borderColor: 'var(--pcs-border)' }}>
      <table className="w-full text-left text-xs pcs-tabular-nums">
        <thead>
          <tr style={{ background: 'var(--pcs-surface-2)', color: 'var(--pcs-text-muted)' }}>
            <th className="px-3 py-2 font-medium"><div className="pcs-skeleton h-4 w-16 rounded" /></th>
            <th className="px-3 py-2 font-medium"><div className="pcs-skeleton h-4 w-24 rounded" /></th>
            <th className="px-3 py-2 font-medium"><div className="pcs-skeleton h-4 w-12 rounded" /></th>
            <th className="px-3 py-2 font-medium"><div className="pcs-skeleton h-4 w-12 rounded" /></th>
            <th className="px-3 py-2 font-medium"><div className="pcs-skeleton h-4 w-20 rounded" /></th>
          </tr>
        </thead>
        <tbody>
          {[...Array(5)].map((_, i) => (
            <tr key={i} style={{ borderTop: '1px solid var(--pcs-border)' }}>
              <td className="px-3 py-1.5"><div className="pcs-skeleton h-4 w-10 rounded" /></td>
              <td className="px-3 py-1.5"><div className="pcs-skeleton h-4 w-20 rounded" /></td>
              <td className="px-3 py-1.5"><div className="pcs-skeleton h-4 w-12 rounded" /></td>
              <td className="px-3 py-1.5"><div className="pcs-skeleton h-4 w-8 rounded" /></td>
              <td className="px-3 py-1.5"><div className="pcs-skeleton h-4 w-16 rounded" /></td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export function Security() {
  const [snapshot, setSnapshot] = useState<SecurityStatusResult | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const run = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const result = await bridge.call<SecurityStatusResult>(
        OperationIds.securityGetStatus,
        undefined,
        READ_TIMEOUT_MS,
      );
      setSnapshot(result);
    } catch (caught) {
      setError(errorMessage(caught, 'No se pudo leer el Centro de seguridad de Windows.'));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void run();
  }, [run]);

  const defender = snapshot?.defender;
  const firewall = snapshot?.firewall;
  const antivirus = snapshot?.antivirus;
  const av = boolCell(defender?.antivirusEnabled, 'Activado', 'Desactivado');
  const rt = boolCell(defender?.realTimeProtection, 'Activada', 'Desactivada');
  const domain = boolCell(firewall?.domainEnabled, 'Activo', 'Inactivo');
  const priv = boolCell(firewall?.privateEnabled, 'Activo', 'Inactivo');
  const pub = boolCell(firewall?.publicEnabled, 'Activo', 'Inactivo');

  return (
    <div className="space-y-6">
      <header className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold">Seguridad</h1>
          <p className="mt-1 text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
            Centro de seguridad de Windows en solo lectura: Defender, firewall por perfil,
            antivirus, puertos abiertos y diagnóstico — sin modificaciones ni elevación.
          </p>
        </div>
        <button
          type="button"
          onClick={() => void run()}
          disabled={loading}
          className="flex items-center gap-2 rounded-md px-3 py-2 text-sm font-medium transition-colors disabled:opacity-50 pcs-press-feedback"
          style={{ background: 'var(--pcs-accent)', color: '#fff' }}
        >
          <RefreshCw size={14} aria-hidden className={loading ? 'animate-spin' : undefined} />
          Actualizar
        </button>
      </header>

      {loading && !snapshot && (
        <div className="space-y-4 pcs-stagger-in" role="status" aria-busy="true">
          <div className="grid gap-4 md:grid-cols-3">
            <StatusCardSkeleton />
            <StatusCardSkeleton />
            <StatusCardSkeleton />
          </div>
          <section className="rounded-lg border p-4" style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }} aria-label="Diagnóstico de seguridad">
            <div className="pcs-skeleton h-5 w-32 rounded mb-2" />
            <ul className="divide-y" style={{ borderColor: 'var(--pcs-border)' }}>
              {[...Array(4)].map((_, i) => (
                <DiagnosticSkeleton key={i} />
              ))}
            </ul>
          </section>
          <section className="rounded-lg border p-4" style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }} aria-label="Puertos abiertos">
            <div className="flex items-center justify-between gap-2">
              <div className="pcs-skeleton h-5 w-28 rounded" />
              <div className="pcs-skeleton h-5 w-12 rounded" />
            </div>
            <PortsTableSkeleton />
          </section>
        </div>
      )}

      {loading && snapshot && (
        <p className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
          Actualizando…
        </p>
      )}

      {error && (
        <div
          className="rounded-md border px-4 py-3 text-sm"
          style={{ borderColor: 'var(--pcs-danger)', color: 'var(--pcs-danger)' }}
          role="alert"
        >
          {error}
        </div>
      )}

      {snapshot && defender && firewall && antivirus && (
        <>
          {/* Tarjetas de estado (semáforo por fuente) */}
          <div className="grid gap-4 md:grid-cols-3 pcs-stagger-in">
            <Card label="Windows Defender" icon={ShieldCheck} status={defender.status}>
              <Row label="Antivirus" value={av.text} color={av.color} />
              <Row label="Protección en tiempo real" value={rt.text} color={rt.color} />
              <Row
                label="Firmas"
                value={
                  defender.signatureLastUpdated
                    ? new Date(defender.signatureLastUpdated).toLocaleDateString()
                    : defender.signatureNote ?? 'Sin datos'
                }
                color={defender.signatureLastUpdated || defender.signatureNote ? undefined : 'var(--pcs-text-muted)'}
              />
            </Card>

            <Card label="Firewall" icon={Network} status={firewall.status}>
              <Row label="Perfil dominio" value={domain.text} color={domain.color} />
              <Row label="Perfil privado" value={priv.text} color={priv.color} />
              <Row label="Perfil público" value={pub.text} color={pub.color} />
            </Card>

            <Card label="Antivirus" icon={Shield} status={antivirus.status}>
              {antivirus.products.length > 0 ? (
                <ul className="space-y-1.5">
                  {antivirus.products.map((product) => (
                    <li key={product} className="flex items-center gap-2">
                      <CheckCircle2
                        size={12}
                        aria-hidden
                        style={{ color: 'var(--pcs-success)', flexShrink: 0 }}
                      />
                      <span className="font-medium">{product}</span>
                    </li>
                  ))}
                </ul>
              ) : (
                <div className="flex flex-col items-center justify-center py-6 px-4 text-center">
                  <Shield size={24} aria-hidden className="mb-2 opacity-30" style={{ color: 'var(--pcs-accent)' }} />
                  <p className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
                    No hay productos antivirus registrados.
                  </p>
                </div>
              )}
            </Card>
          </div>

          {/* Diagnóstico */}
          <section
            className="rounded-lg border p-4"
            style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
            aria-label="Diagnóstico de seguridad"
          >
            <h2 className="text-sm font-semibold">Diagnóstico</h2>
            <ul className="mt-2 divide-y pcs-stagger-in" style={{ borderColor: 'var(--pcs-border)' }}>
              {snapshot.diagnostics.map((check) => {
                const meta = checkMeta(check.status);
                const Icon =
                  meta.tone === 'success' ? CheckCircle2 : meta.tone === 'warning' ? AlertTriangle : HelpCircle;
                return (
                  <li key={check.id} className="flex items-start gap-3 py-2.5 text-sm">
                    <Icon
                      size={16}
                      aria-hidden
                      className="mt-0.5 shrink-0"
                      style={{ color: toneColor(meta.tone) }}
                    />
                    <div className="min-w-0 flex-1">
                      <p className="font-medium">{check.label}</p>
                      {check.detail && (
                        <p className="mt-0.5 text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
                          {check.detail}
                        </p>
                      )}
                    </div>
                    <span
                      className="whitespace-nowrap rounded-full px-2 py-0.5 text-xs font-medium"
                      style={toneStyle(meta.tone)}
                    >
                      {meta.label}
                    </span>
                  </li>
                );
              })}
            </ul>
          </section>

          {/* Puertos en escucha (solo lectura) */}
          <section
            className="rounded-lg border p-4"
            style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
            aria-label="Puertos abiertos"
          >
            <div className="flex items-center justify-between gap-2">
              <h2 className="flex items-center gap-2 text-sm font-semibold">
                <Server size={14} aria-hidden style={{ color: 'var(--pcs-accent)' }} />
                Puertos abiertos
              </h2>
              <span
                className="rounded-full px-2 py-0.5 text-xs font-medium"
                style={toneStyle('muted')}
              >
                {snapshot.ports.length}
              </span>
            </div>
            {snapshot.ports.length === 0 ? (
              <div className="flex flex-col items-center justify-center py-8 px-4 text-center">
                <Server size={32} aria-hidden className="mb-3 opacity-30" style={{ color: 'var(--pcs-accent)' }} />
                <h4 className="text-sm font-medium mb-1">No hay puertos en escucha</h4>
                <p className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
                  El sistema no tiene puertos abiertos detectados.
                </p>
              </div>
            ) : (
              <div
                className="mt-3 max-h-72 overflow-auto rounded-md border"
                style={{ borderColor: 'var(--pcs-border)' }}
              >
                <table className="w-full text-left text-xs pcs-tabular-nums">
                  <thead>
                    <tr
                      style={{
                        background: 'var(--pcs-surface-2)',
                        color: 'var(--pcs-text-muted)',
                      }}
                    >
                      <th className="px-3 py-2 font-medium">Protocolo</th>
                      <th className="px-3 py-2 font-medium">Dirección</th>
                      <th className="px-3 py-2 font-medium">Puerto</th>
                      <th className="px-3 py-2 font-medium">PID</th>
                      <th className="px-3 py-2 font-medium">Proceso</th>
                    </tr>
                  </thead>
                  <tbody>
                    {snapshot.ports.map((port, index) => (
                      <tr
                        key={`${port.protocol}-${port.localAddress}-${port.localPort}-${index}`}
                        style={{ borderTop: '1px solid var(--pcs-border)' }}
                      >
                        <td className="px-3 py-1.5 font-mono">{port.protocol}</td>
                        <td className="px-3 py-1.5 font-mono">{port.localAddress}</td>
                        <td className="px-3 py-1.5 font-mono">{port.localPort}</td>
                        <td className="px-3 py-1.5 font-mono">{port.pid ?? '—'}</td>
                        <td
                          className="px-3 py-1.5"
                          title={
                            port.processDetail?.executablePath
                              ? `${port.processDetail.executablePath}${port.processDetail.signer ? `\nFirmado: ${port.processDetail.signer}` : '\nSin firma'}`
                              : undefined
                          }
                        >
                          {port.process ?? 'Sin datos'}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>

          {/* Task 029: conexiones TCP ESTABLECIDAS con extremo remoto —
              la vista de "¿quién habla con quién?" para detectar
              conexiones no autorizadas. */}
          <section
            className="rounded-lg border p-4"
            style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
            aria-label="Conexiones activas"
          >
            <div className="flex items-center justify-between gap-2">
              <h2 className="flex items-center gap-2 text-sm font-semibold">
                <Globe size={14} aria-hidden style={{ color: 'var(--pcs-accent)' }} />
                Conexiones activas (TCP)
              </h2>
              <span
                className="rounded-full px-2 py-0.5 text-xs font-medium"
                style={toneStyle('muted')}
              >
                {snapshot.connections.length}
              </span>
            </div>
            {snapshot.connections.length === 0 ? (
              <div className="flex flex-col items-center justify-center py-8 px-4 text-center">
                <Globe size={32} aria-hidden className="mb-3 opacity-30" style={{ color: 'var(--pcs-accent)' }} />
                <h4 className="text-sm font-medium mb-1">Sin conexiones TCP establecidas</h4>
                <p className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
                  Ningún proceso está hablando con el exterior ahora mismo.
                </p>
              </div>
            ) : (
              <div
                className="mt-3 max-h-72 overflow-auto rounded-md border"
                style={{ borderColor: 'var(--pcs-border)' }}
              >
                <table className="w-full text-left text-xs pcs-tabular-nums">
                  <thead>
                    <tr
                      style={{
                        background: 'var(--pcs-surface-2)',
                        color: 'var(--pcs-text-muted)',
                      }}
                    >
                      <th className="px-3 py-2 font-medium">Local</th>
                      <th className="px-3 py-2 font-medium">Remoto</th>
                      <th className="px-3 py-2 font-medium">PID</th>
                      <th className="px-3 py-2 font-medium">Proceso</th>
                      <th className="px-3 py-2 font-medium">Revisión</th>
                    </tr>
                  </thead>
                  <tbody>
                    {snapshot.connections.map((connection, index) => (
                      <tr
                        key={`${connection.localAddress}-${connection.localPort}-${connection.remoteAddress}-${connection.remotePort}-${index}`}
                        style={{ borderTop: '1px solid var(--pcs-border)' }}
                      >
                        <td className="px-3 py-1.5 font-mono">
                          {connection.localAddress}:{connection.localPort}
                        </td>
                        <td className="px-3 py-1.5 font-mono">
                          {connection.remoteAddress}:{connection.remotePort}
                        </td>
                        <td className="px-3 py-1.5 font-mono">{connection.pid ?? '—'}</td>
                        <td
                          className="px-3 py-1.5"
                          title={
                            connection.process?.executablePath
                              ? `${connection.process.executablePath}${connection.process.signer ? `\nFirmado: ${connection.process.signer}` : '\nSin firma'}`
                              : undefined
                          }
                        >
                          {connection.process?.name ?? 'Sin datos'}
                        </td>
                        <td className="px-3 py-1.5">
                          {connection.reviewNote ? (
                            <span
                              className="rounded-full px-2 py-0.5 text-xs font-medium"
                              style={{
                                background: 'color-mix(in srgb, var(--pcs-warning) 16%, transparent)',
                                color: 'var(--pcs-warning)',
                              }}
                              title="Hechos para revisar esta conexión — el veredicto es tuyo"
                            >
                              ⚠ {connection.reviewNote}
                            </span>
                          ) : (
                            <span style={{ color: 'var(--pcs-text-muted)' }}>—</span>
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>

          {/* Meta de la lectura */}
          <footer
            className="flex flex-wrap items-center justify-between gap-2 text-xs"
            style={{ color: 'var(--pcs-text-muted)' }}
          >
            <span>Última lectura: {new Date(snapshot.scannedAt).toLocaleString()}</span>
            <span>Solo lectura (§14): esta página no modifica ajustes de seguridad.</span>
          </footer>
        </>
      )}
    </div>
  );
}
