import type { LucideIcon } from 'lucide-react';
import { Bell, ExternalLink, Globe, Monitor, Moon, Sun, Layout } from 'lucide-react';
import { useTheme } from '../theme';
import type { ThemePreference } from '../theme';
import { bridge } from '../services/pcsuiteBridge';
import { OperationIds } from '../types/bridge';
import { UpdatePanel } from '../components/UpdateCenter';
import {
  PREF_DENSITY,
  PREF_NOTIFICATIONS,
  setPreference as setStoredPreference,
  usePreference,
} from '../prefs';

/**
 * Página Ajustes (Task 010, parte 10.2): preferencias de UX sobre el
 * store local (prefs.ts / theme.ts). El tema usa el motor de 10.1; los
 * toggles los consume después notificaciones (10.6). La sección
 * «Acerca de» expone el copyright del desarrollador y su web
 * (explorer.openUrl — el host bloquea window.open, así que el navegador
 * del sistema solo se abre vía puente).
 */

const DEVELOPER_NAME = 'AES Studio';
const DEVELOPER_WEBSITE = 'https://aes-studio.dev';

const THEME_OPTIONS: { value: ThemePreference; label: string; icon: LucideIcon }[] = [
  { value: 'dark', label: 'Oscuro', icon: Moon },
  { value: 'light', label: 'Claro', icon: Sun },
  { value: 'system', label: 'Sistema', icon: Monitor },
];

const DENSITY_OPTIONS: { value: 'compact' | 'comfy'; label: string; icon: LucideIcon }[] = [
  { value: 'compact', label: 'Compacto', icon: Layout },
  { value: 'comfy', label: 'Cómodo', icon: Layout },
];

export function Settings() {
  const { preference, setPreference } = useTheme();
  const notifications = usePreference<boolean>(PREF_NOTIFICATIONS, true);
  const density = usePreference<'compact' | 'comfy'>(PREF_DENSITY, 'comfy');

  /** Abre la web del desarrollador: en la app lo hace el host nativo
   * (explorer.openUrl); sin host (dev en navegador), pestaña nueva. */
  const openDeveloperWebsite = async (): Promise<void> => {
    try {
      await bridge.call(OperationIds.explorerOpenUrl, { url: DEVELOPER_WEBSITE });
    } catch {
      window.open(DEVELOPER_WEBSITE, '_blank', 'noopener,noreferrer');
    }
  };

  return (
    <div className="space-y-6">
      <header>
        <h1 className="text-2xl font-semibold">Ajustes</h1>
        <p className="mt-1 text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
          Preferencias de PCSuite. Se guardan localmente en este equipo.
        </p>
      </header>

      {/* Apariencia */}
      <section
        className="space-y-3 rounded-lg border p-4"
        style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
      >
        <h2 className="text-sm font-semibold">Apariencia</h2>
        <div className="flex flex-wrap gap-2">
          {THEME_OPTIONS.map((option) => {
            const Icon = option.icon;
            const active = preference === option.value;
            return (
              <button
                key={option.value}
                type="button"
                aria-pressed={active}
                onClick={() => setPreference(option.value)}
                className="flex items-center gap-2 rounded-md border px-3 py-2 text-sm transition-colors"
                style={{
                  background: active ? 'var(--pcs-accent)' : 'var(--pcs-surface-2)',
                  borderColor: active ? 'var(--pcs-accent)' : 'var(--pcs-border)',
                  color: active ? '#fff' : 'var(--pcs-text)',
                }}
              >
                <Icon size={15} aria-hidden />
                {option.label}
              </button>
            );
          })}
        </div>
        <p className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
          «Sistema» sigue el tema de Windows; el oscuro es la experiencia principal.
        </p>
      </section>

      {/* Comportamiento */}
      <section
        className="space-y-1 rounded-lg border p-4"
        style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
      >
        <h2 className="mb-2 text-sm font-semibold">Comportamiento</h2>
        <ToggleRow
          icon={Bell}
          title="Notificaciones"
          description="Avisos emergentes cuando terminan las operaciones."
          checked={notifications}
          onChange={(value) => setStoredPreference(PREF_NOTIFICATIONS, value)}
        />
      </section>

      {/* Densidad de interfaz (14.3.6) */}
      <section
        className="space-y-3 rounded-lg border p-4"
        style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
      >
        <h2 className="text-sm font-semibold">Densidad de interfaz</h2>
        <div className="flex flex-wrap gap-2">
          {DENSITY_OPTIONS.map((option) => {
            const active = density === option.value;
            return (
              <button
                key={option.value}
                type="button"
                aria-pressed={active}
                onClick={() => setStoredPreference(PREF_DENSITY, option.value)}
                className="flex items-center gap-2 rounded-md border px-3 py-2 text-sm transition-colors"
                style={{
                  background: active ? 'var(--pcs-accent)' : 'var(--pcs-surface-2)',
                  borderColor: active ? 'var(--pcs-accent)' : 'var(--pcs-border)',
                  color: active ? '#fff' : 'var(--pcs-text)',
                }}
              >
                <option.icon size={15} aria-hidden />
                {option.label}
              </button>
            );
          })}
        </div>
        <p className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
          «Compacto» reduce espaciado y tamaños; «Cómodo» usa valores estándar.
        </p>
      </section>

      {/* Actualizaciones (Task 17.3/17.4) */}
      <UpdatePanel />

      {/* Acerca de */}
      <section
        className="space-y-3 rounded-lg border p-4"
        style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
        aria-label="Acerca de PCSuite"
      >
        <h2 className="text-sm font-semibold">Acerca de</h2>

        <div className="flex items-center gap-3">
          <img src="./pcsuite-icon.svg" alt="" aria-hidden className="h-9 w-9 shrink-0" />
          <div className="min-w-0">
            <p className="text-sm font-medium">PCSuite</p>
            <p className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
              Utilidades de mantenimiento, seguridad y desarrollo para Windows.
            </p>
          </div>
        </div>

        <div
          className="flex flex-wrap items-center justify-between gap-x-4 gap-y-2 border-t pt-3"
          style={{ borderColor: 'var(--pcs-border)' }}
        >
          <p className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
            © {new Date().getFullYear()} {DEVELOPER_NAME} — desarrollado por{' '}
            <span className="font-medium" style={{ color: 'var(--pcs-text)' }}>
              {DEVELOPER_NAME}
            </span>
            .
          </p>
          <button
            type="button"
            onClick={() => void openDeveloperWebsite()}
            title={DEVELOPER_WEBSITE}
            className="flex shrink-0 cursor-pointer items-center gap-1.5 rounded-md border px-2.5 py-1.5 text-xs font-medium transition-colors hover:bg-(--pcs-surface-2)"
            style={{ borderColor: 'var(--pcs-border)', color: 'var(--pcs-accent)' }}
          >
            <Globe size={12} aria-hidden />
            aes-studio.dev
            <ExternalLink size={10} aria-hidden className="opacity-70" />
          </button>
        </div>
      </section>
    </div>
  );
}

function ToggleRow({
  icon: Icon,
  title,
  description,
  checked,
  onChange,
}: {
  icon: LucideIcon;
  title: string;
  description: string;
  checked: boolean;
  onChange: (value: boolean) => void;
}) {
  return (
    <div className="flex items-center gap-3 border-t py-3 first:border-t-0" style={{ borderColor: 'var(--pcs-border)' }}>
      <Icon size={16} aria-hidden style={{ color: 'var(--pcs-accent)', flexShrink: 0 }} />
      <div className="min-w-0 flex-1">
        <p className="text-sm font-medium">{title}</p>
        <p className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
          {description}
        </p>
      </div>
      <button
        type="button"
        role="switch"
        aria-checked={checked}
        aria-label={title}
        onClick={() => onChange(!checked)}
        className="h-5 w-9 shrink-0 rounded-full p-0.5 transition-colors"
        style={{
          background: checked ? 'var(--pcs-accent)' : 'var(--pcs-surface-2)',
          border: '1px solid var(--pcs-border)',
          cursor: 'pointer',
        }}
      >
        <span
          className="block h-3.5 w-3.5 rounded-full transition-transform"
          style={{ background: '#fff', transform: checked ? 'translateX(16px)' : 'none' }}
        />
      </button>
    </div>
  );
}
