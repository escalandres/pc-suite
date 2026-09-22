import type { ReactNode } from 'react';
import {
  Home,
  ShieldCheck,
  Wrench,
  Terminal,
  Puzzle,
} from 'lucide-react';

interface AppLayoutProps {
  children: ReactNode;
  readyNotice?: string | null;
}

interface NavItem {
  id: string;
  label: string;
  icon: typeof Home;
  enabled: boolean;
}

/**
 * Layout principal (plan §17.2): sidebar de navegación + área de contenido.
 * Los módulos no implementados quedan deshabilitados hasta su task.
 */
const NAV_ITEMS: NavItem[] = [
  { id: 'home', label: 'Inicio', icon: Home, enabled: true },
  { id: 'cleaner', label: 'Limpiador', icon: Wrench, enabled: false },
  { id: 'security', label: 'Seguridad', icon: ShieldCheck, enabled: false },
  { id: 'devtools', label: 'Herramientas dev', icon: Terminal, enabled: false },
  { id: 'plugins', label: 'Plugins', icon: Puzzle, enabled: false },
];

export function AppLayout({ children, readyNotice }: AppLayoutProps) {
  return (
    <div className="flex h-full min-h-screen">
      {/* Sidebar */}
      <aside
        className="flex w-56 shrink-0 flex-col border-r"
        style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
      >
        <div className="flex items-center gap-2 px-4 py-4">
          <div
            className="flex h-8 w-8 items-center justify-center rounded-lg text-sm font-bold"
            style={{ background: 'var(--pcs-accent)', color: '#fff' }}
          >
            PC
          </div>
          <span className="text-sm font-semibold tracking-wide">PCSuite</span>
        </div>

        <nav className="flex-1 space-y-1 px-2">
          {NAV_ITEMS.map((item) => {
            const Icon = item.icon;
            return (
              <button
                key={item.id}
                type="button"
                disabled={!item.enabled}
                aria-current={item.id === 'home' ? 'page' : undefined}
                className="flex w-full items-center gap-3 rounded-md px-3 py-2 text-left text-sm transition-colors disabled:opacity-40"
                style={
                  item.enabled
                    ? { color: 'var(--pcs-text)' }
                    : { color: 'var(--pcs-text-muted)', cursor: 'not-allowed' }
                }
                onMouseEnter={(e) => {
                  if (item.enabled) e.currentTarget.style.background = 'var(--pcs-surface-2)';
                }}
                onMouseLeave={(e) => {
                  e.currentTarget.style.background = 'transparent';
                }}
              >
                <Icon size={16} aria-hidden />
                {item.label}
                {!item.enabled && (
                  <span className="ml-auto text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
                    pronto
                  </span>
                )}
              </button>
            );
          })}
        </nav>

        <footer
          className="px-4 py-3 text-xs"
          style={{ color: 'var(--pcs-text-muted)', borderTop: '1px solid var(--pcs-border)' }}
        >
          {readyNotice ?? 'Cargando…'}
        </footer>
      </aside>

      {/* Contenido */}
      <main className="flex-1 overflow-y-auto">
        <div className="mx-auto max-w-4xl px-8 py-8">{children}</div>
      </main>
    </div>
  );
}
