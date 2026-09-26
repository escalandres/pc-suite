import type { LucideIcon } from 'lucide-react';
import { Bell, ClipboardCheck, Monitor, Moon, Sun } from 'lucide-react';
import { useTheme } from '../theme';
import type { ThemePreference } from '../theme';
import {
  PREF_CLIPBOARD_DETECTION,
  PREF_NOTIFICATIONS,
  setPreference as setStoredPreference,
  usePreference,
} from '../prefs';

/**
 * Página Ajustes (Task 010, parte 10.2): preferencias de UX sobre el
 * store local (prefs.ts / theme.ts). El tema usa el motor de 10.1; los
 * toggles los consumen después notificaciones (10.6) y la detección de
 * clipboard (10.7).
 */

const THEME_OPTIONS: { value: ThemePreference; label: string; icon: LucideIcon }[] = [
  { value: 'dark', label: 'Oscuro', icon: Moon },
  { value: 'light', label: 'Claro', icon: Sun },
  { value: 'system', label: 'Sistema', icon: Monitor },
];

export function Settings() {
  const { preference, setPreference } = useTheme();
  const notifications = usePreference<boolean>(PREF_NOTIFICATIONS, true);
  const clipboardDetection = usePreference<boolean>(PREF_CLIPBOARD_DETECTION, true);

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
        <ToggleRow
          icon={ClipboardCheck}
          title="Detección de clipboard"
          description="Sugerir herramientas al detectar JSON, JWT, claves SSH o PEM en el portapapeles. Nada de su contenido se guarda en disco."
          checked={clipboardDetection}
          onChange={(value) => setStoredPreference(PREF_CLIPBOARD_DETECTION, value)}
        />
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
