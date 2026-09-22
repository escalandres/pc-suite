import { useCallback, useEffect, useState } from 'react';
import { Cpu, HardDrive, MonitorSmartphone, ShieldCheck, RefreshCw } from 'lucide-react';
import { bridge, BridgeError } from '../services/pcsuiteBridge';
import { OperationIds } from '../types/bridge';
import type { SystemInfoResult } from '../types/bridge';

type LoadState = 'idle' | 'loading' | 'ready' | 'error';

interface StatCard {
  key: keyof SystemInfoResult;
  label: string;
  icon: typeof Cpu;
}

const CARDS: StatCard[] = [
  { key: 'machineName', label: 'Equipo', icon: MonitorSmartphone },
  { key: 'osDescription', label: 'Sistema operativo', icon: HardDrive },
  { key: 'processorCount', label: 'Procesadores lógicos', icon: Cpu },
  { key: 'isAdministrator', label: 'Sesión administrador', icon: ShieldCheck },
];

/**
 * Pantalla Home (Task 001): demuestra el round-trip frontend → nativo
 * invocando la operación permitida system.info.
 */
export function Home() {
  const [state, setState] = useState<LoadState>('idle');
  const [info, setInfo] = useState<SystemInfoResult | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setState('loading');
    setError(null);
    try {
      const result = await bridge.call<SystemInfoResult>(OperationIds.systemInfo);
      setInfo(result);
      setState('ready');
    } catch (err) {
      // Nunca mostrar stack traces: solo el mensaje controlado del BridgeError.
      const message =
        err instanceof BridgeError ? err.message : 'No se pudo obtener la información.';
      setError(message);
      setState('error');
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  return (
    <div className="space-y-6">
      <header className="flex items-end justify-between">
        <div>
          <h1 className="text-2xl font-semibold">Inicio</h1>
          <p className="mt-1 text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
            Información general de este equipo.
          </p>
        </div>
        <button
          type="button"
          onClick={() => void load()}
          disabled={state === 'loading'}
          className="flex items-center gap-2 rounded-md px-3 py-2 text-sm font-medium transition-colors disabled:opacity-50"
          style={{ background: 'var(--pcs-accent)', color: '#fff' }}
        >
          <RefreshCw size={14} aria-hidden className={state === 'loading' ? 'animate-spin' : ''} />
          Actualizar
        </button>
      </header>

      {state === 'loading' && (
        <p className="text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
          Consultando el sistema…
        </p>
      )}

      {state === 'error' && (
        <div
          className="rounded-md border px-4 py-3 text-sm"
          style={{ borderColor: 'var(--pcs-danger)', color: 'var(--pcs-danger)' }}
          role="alert"
        >
          {error}
        </div>
      )}

      {state === 'ready' && info && (
        <>
          <section className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            {CARDS.map((card) => {
              const Icon = card.icon;
              const raw = info[card.key];
              const value =
                typeof raw === 'boolean' ? (raw ? 'Sí' : 'No') : String(raw ?? '—');
              return (
                <article
                  key={card.key}
                  className="rounded-lg border p-4"
                  style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
                >
                  <div className="flex items-center gap-2 text-xs uppercase tracking-wide" style={{ color: 'var(--pcs-text-muted)' }}>
                    <Icon size={14} aria-hidden />
                    {card.label}
                  </div>
                  <div className="mt-2 break-words text-base font-medium">{value}</div>
                </article>
              );
            })}
          </section>

          <section
            className="rounded-lg border p-4 text-sm"
            style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
          >
            <h2 className="mb-3 font-medium">Detalles</h2>
            <dl className="grid grid-cols-1 gap-x-8 gap-y-2 sm:grid-cols-2">
              <Detail label="Versión de la app" value={info.appVersion} />
              <Detail label="Runtime" value={info.runtimeVersion} />
              <Detail label="Versión del SO" value={info.osVersion} />
              <Detail
                label="Arquitectura"
                value={info.is64BitOperatingSystem ? '64 bits' : '32 bits'}
              />
            </dl>
          </section>
        </>
      )}
    </div>
  );
}

function Detail({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex justify-between gap-4">
      <dt style={{ color: 'var(--pcs-text-muted)' }}>{label}</dt>
      <dd className="text-right">{value}</dd>
    </div>
  );
}
