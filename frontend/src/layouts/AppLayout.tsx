import type { ReactNode } from 'react';
import {
  Activity,
  Home,
  ShieldCheck,
  Wrench,
  Terminal,
  Puzzle,
  Sparkles,
  Package,
  Boxes,
  Bug,
  Settings,
  Star,
} from 'lucide-react';
import { pageKey, toggleFavorite, useFavorites } from '../favorites';
import { openHelp } from '../shortcuts';

export type PageId =
  | 'home'
  | 'tools'
  | 'cleaner'
  | 'security'
  | 'devtools'
  | 'plugins'
  | 'winget'
  | 'wsl'
  | 'clamav'
  | 'activity'
  | 'settings';

interface AppLayoutProps {
  children: ReactNode;
  page: PageId;
  /** toolId opcional: navega a la página indicada con esa tool seleccionada. */
  onNavigate: (page: PageId, toolId?: string) => void;
  readyNotice?: string | null;
}

interface NavItem {
  id: PageId;
  label: string;
  icon: typeof Home;
  enabled: boolean;
}

/**
 * Layout principal (plan §17.2): sidebar de navegación + área de contenido.
 * Solo las páginas implementadas están habilitadas.
 */
export const NAV_ITEMS: NavItem[] = [
  { id: 'home', label: 'Inicio', icon: Home, enabled: true },
  { id: 'tools', label: 'Herramientas', icon: Wrench, enabled: true },
  { id: 'winget', label: 'Paquetes', icon: Package, enabled: true },
  { id: 'wsl', label: 'WSL', icon: Boxes, enabled: true },
  { id: 'clamav', label: 'ClamAV', icon: Bug, enabled: true },
  { id: 'cleaner', label: 'Limpiador', icon: Sparkles, enabled: false },
  { id: 'security', label: 'Seguridad', icon: ShieldCheck, enabled: false },
  { id: 'devtools', label: 'Herramientas dev', icon: Terminal, enabled: true },
  { id: 'plugins', label: 'Plugins', icon: Puzzle, enabled: false },
  { id: 'activity', label: 'Actividad', icon: Activity, enabled: true },
  { id: 'settings', label: 'Ajustes', icon: Settings, enabled: true },
];

export function AppLayout({ children, page, onNavigate, readyNotice }: AppLayoutProps) {
  const favorites = useFavorites();

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

        <nav className="flex-1 space-y-1 overflow-y-auto px-2 pb-2">
          {NAV_ITEMS.map((item) => {
            const Icon = item.icon;
            const active = item.id === page;
            const starred = favorites.some((favorite) => favorite.key === pageKey(item.id));
            return (
              <div key={item.id} className="group/nav relative">
                <button
                  type="button"
                  disabled={!item.enabled}
                  aria-current={active ? 'page' : undefined}
                  onClick={() => item.enabled && onNavigate(item.id)}
                  className="flex w-full items-center gap-3 rounded-md px-3 py-2 text-left text-sm transition-colors disabled:opacity-40"
                  style={{
                    color: item.enabled
                      ? active
                        ? '#fff'
                        : 'var(--pcs-text)'
                      : 'var(--pcs-text-muted)',
                    cursor: item.enabled ? 'pointer' : 'not-allowed',
                    background: active ? 'var(--pcs-accent)' : 'transparent',
                    paddingRight: item.enabled ? '2rem' : undefined,
                  }}
                  onMouseEnter={(e) => {
                    if (item.enabled && !active) e.currentTarget.style.background = 'var(--pcs-surface-2)';
                  }}
                  onMouseLeave={(e) => {
                    if (!active) e.currentTarget.style.background = 'transparent';
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
                {item.enabled && (
                  <button
                    type="button"
                    aria-pressed={starred}
                    aria-label={
                      starred ? `Quitar ${item.label} de favoritos` : `Añadir ${item.label} a favoritos`
                    }
                    onClick={() =>
                      toggleFavorite({ key: pageKey(item.id), label: item.label, page: item.id })
                    }
                    className={`absolute right-1.5 top-1/2 -translate-y-1/2 rounded p-1 transition-opacity ${
                      starred ? 'opacity-100' : 'opacity-0 group-hover/nav:opacity-100'
                    }`}
                    style={{
                      color: starred
                        ? active
                          ? '#fff'
                          : 'var(--pcs-accent)'
                        : 'var(--pcs-text-muted)',
                      cursor: 'pointer',
                    }}
                  >
                    <Star size={13} fill={starred ? 'currentColor' : 'none'} aria-hidden />
                  </button>
                )}
              </div>
            );
          })}

          {/* Sección Favoritos (plan §19) */}
          <div
            className="px-3 pb-1 pt-4 text-xs font-semibold uppercase tracking-wide"
            style={{ color: 'var(--pcs-text-muted)' }}
          >
            Favoritos
          </div>
          {favorites.length === 0 ? (
            <p className="px-3 text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
              Pulsa la estrella de una página o herramienta para fijarla aquí.
            </p>
          ) : (
            favorites.map((favorite) => {
              const Icon = favorite.toolId
                ? Wrench
                : (NAV_ITEMS.find((item) => item.id === favorite.page)?.icon ?? Home);
              return (
                <div key={favorite.key} className="group/fav relative">
                  <button
                    type="button"
                    onClick={() => onNavigate(favorite.page, favorite.toolId)}
                    className="flex w-full items-center gap-3 rounded-md px-3 py-1.5 text-left text-sm transition-colors"
                    style={{ color: 'var(--pcs-text)', paddingRight: '2rem' }}
                    onMouseEnter={(e) => {
                      e.currentTarget.style.background = 'var(--pcs-surface-2)';
                    }}
                    onMouseLeave={(e) => {
                      e.currentTarget.style.background = 'transparent';
                    }}
                  >
                    <Icon
                      size={14}
                      aria-hidden
                      style={{ color: 'var(--pcs-accent)', flexShrink: 0 }}
                    />
                    <span className="truncate">{favorite.label}</span>
                  </button>
                  <button
                    type="button"
                    aria-label={`Quitar ${favorite.label} de favoritos`}
                    onClick={() => toggleFavorite(favorite)}
                    className="absolute right-1.5 top-1/2 -translate-y-1/2 rounded p-1"
                    style={{ color: 'var(--pcs-accent)', cursor: 'pointer' }}
                  >
                    <Star size={12} fill="currentColor" aria-hidden />
                  </button>
                </div>
              );
            })
          )}
        </nav>

        <footer
          className="flex items-center justify-between gap-2 px-4 py-3 text-xs"
          style={{ color: 'var(--pcs-text-muted)', borderTop: '1px solid var(--pcs-border)' }}
        >
          <span className="truncate">{readyNotice ?? 'Cargando…'}</span>
          <button
            type="button"
            aria-label="Ver atajos de teclado"
            onClick={openHelp}
            className="shrink-0 rounded border px-1.5 font-semibold"
            style={{
              borderColor: 'var(--pcs-border)',
              color: 'var(--pcs-text-muted)',
              cursor: 'pointer',
            }}
          >
            ?
          </button>
        </footer>
      </aside>

      {/* Contenido */}
      <main className="flex-1 overflow-y-auto">
        <div className="mx-auto max-w-6xl px-8 py-8">{children}</div>
      </main>
    </div>
  );
}
