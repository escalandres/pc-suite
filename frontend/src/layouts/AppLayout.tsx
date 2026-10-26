import type { ReactNode } from 'react';
import { useState, useEffect } from 'react';
import {
  Activity,
  Binary,
  FolderOpen,
  Home,
  MonitorSmartphone,
  ShieldCheck,
  Wrench,
  Terminal,
  Puzzle,
  Sparkles,
  Package,
  Boxes,
  Bug,
  FileKey,
  Settings,
  Star,
  ChevronLeft,
  ChevronRight,
} from 'lucide-react';
import { pageKey, toggleFavorite, useFavorites } from '../favorites';
import { openHelp } from '../shortcuts';
import { bridge } from '../services/pcsuiteBridge';
import { OperationIds } from '../types/bridge';
import type { AppUpdateCheckResult } from '../types/bridge';
import { Breadcrumbs } from '../components/Breadcrumbs';
import { UpdateCenter, UpdateStartupModal } from '../components/UpdateCenter';

export type PageId =
  | 'home'
  | 'tools'
  | 'localshare'
  | 'cleaner'
  | 'security'
  | 'devtools'
  | 'plugins'
  | 'winget'
  | 'wsl'
  | 'clamav'
  | 'certificates'
  | 'files'
  | 'cipher'
  | 'activity'
  | 'settings';

interface AppLayoutProps {
  children: ReactNode;
  page: PageId;
  /** toolId opcional: navega a la página indicada con esa tool seleccionada. */
  onNavigate: (page: PageId, toolId?: string) => void;
  readyNotice?: string | null;
  /** toolId actual para breadcrumbs (drill-down) */
  currentToolId?: string;
  /** Nombre de la tool para breadcrumbs */
  currentToolName?: string;
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
  { id: 'localshare', label: 'Local Share', icon: MonitorSmartphone, enabled: true },
  { id: 'winget', label: 'Paquetes', icon: Package, enabled: true },
  { id: 'wsl', label: 'WSL', icon: Boxes, enabled: true },
  { id: 'clamav', label: 'ClamAV', icon: Bug, enabled: true },
  { id: 'cleaner', label: 'Limpiador', icon: Sparkles, enabled: true },
  { id: 'certificates', label: 'Certificados', icon: FileKey, enabled: true },
  { id: 'files', label: 'Archivos', icon: FolderOpen, enabled: true },
  { id: 'cipher', label: 'Cifrador', icon: Binary, enabled: true },
  { id: 'security', label: 'Seguridad', icon: ShieldCheck, enabled: true },
  { id: 'devtools', label: 'Herramientas dev', icon: Terminal, enabled: true },
  { id: 'plugins', label: 'Plugins', icon: Puzzle, enabled: true },
  { id: 'activity', label: 'Actividad', icon: Activity, enabled: true },
  { id: 'settings', label: 'Ajustes', icon: Settings, enabled: true },
];

export function AppLayout({ children, page, onNavigate, readyNotice, currentToolId, currentToolName }: AppLayoutProps) {
  const favorites = useFavorites();

  // 14.3.6 — Sidebar colapsable (icon-only mode) con persistencia
  const [collapsed, setCollapsed] = useState(() => {
    try {
      const stored = localStorage.getItem('pcsuite.sidebar.collapsed');
      return stored === 'true';
    } catch {
      return false;
    }
  });

  // Auto-colapsar SOLO al entrar en pantallas < 768px (mobile/tablet).
  // Nunca fuerza la expansión: la preferencia persistida manda en
  // escritorio (si no, cualquier resize reabriría la sidebar que el
  // usuario cerró a mano y el estado guardado jamás se respetaría).
  useEffect(() => {
    const handleResize = () => {
      if (window.innerWidth < 768) setCollapsed(true);
    };
    // Initial check
    handleResize();
    window.addEventListener('resize', handleResize);
    return () => window.removeEventListener('resize', handleResize);
  }, []);

  useEffect(() => {
    try {
      localStorage.setItem('pcsuite.sidebar.collapsed', String(collapsed));
    } catch {
      // ignore
    }
  }, [collapsed]);

  // Task 17.3/027 — check de actualizaciones al ARRANCAR, aquí en la raíz:
  // corre aunque el sidebar esté colapsado (antes vivía dentro del badge y
  // colapsarlo mataba el check de un golpe). El resultado alimenta al badge
  // y al modal de versión nueva (una vez por sesión). Sin red o fallo: la
  // app funciona igual — sin drama.
  const [updateCheck, setUpdateCheck] = useState<AppUpdateCheckResult | null>(null);
  useEffect(() => {
    let cancelled = false;
    bridge
      .call<AppUpdateCheckResult>(OperationIds.updateCheck, undefined, 10_000)
      .then((result) => { if (!cancelled) setUpdateCheck(result); })
      .catch(() => { /* best-effort */ });
    return () => { cancelled = true; };
  }, []);

  const sidebarWidth = collapsed ? 'w-12' : 'w-56';

  return (
    <div className="flex h-full min-h-screen">
      {/* 14.1.1 — Skip link para navegación por teclado */}
      <a href="#main-content" className="pcs-skip-link">
        Saltar al contenido principal
      </a>

      {/* Sidebar */}
      <aside
        className={`flex shrink-0 flex-col border-r border-(--pcs-border) bg-(--pcs-surface) transition-[width] duration-200 ease-out ${sidebarWidth}`}
      >
        {/* Header: en modo colapsado (w-12 = 48px) el logo (32px) y el
            toggle (24px) no caben en una fila con px-3 — se apilan
            centrados para no comprimir ni desbordar el panel. */}
        <div className={`flex items-center px-3 py-3 ${collapsed ? 'flex-col gap-1' : 'gap-2'}`}>
          <img src="./pcsuite-icon.svg" alt="" aria-hidden className="h-8 w-8 shrink-0" />
          {!collapsed && (
            <span className="text-sm font-semibold tracking-wide truncate">PCSuite</span>
          )}
          <button
            type="button"
            aria-label={collapsed ? 'Expandir menú' : 'Colapsar menú'}
            aria-expanded={!collapsed}
            onClick={() => setCollapsed((c) => !c)}
            className={`flex h-6 w-6 shrink-0 items-center justify-center rounded bg-(--pcs-surface-2) text-(--pcs-text-muted) transition hover:brightness-110 hover:text-(--pcs-text) cursor-pointer ${collapsed ? '' : 'ml-auto'}`}
          >
            {collapsed ? <ChevronRight size={14} aria-hidden /> : <ChevronLeft size={14} aria-hidden />}
          </button>
        </div>

        {/* Task 17.3 — badge de actualización disponible (el check vive en
            AppLayout: el panel completo vive en Ajustes › Actualizaciones). */}
        {!collapsed && <UpdateCenter check={updateCheck} />}

        <nav className="pcs-scroll-thin flex-1 space-y-0.5 overflow-y-auto px-2 pb-2">
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
                  title={collapsed ? item.label : undefined}
                  onClick={() => item.enabled && onNavigate(item.id)}
                  className={[
                    'flex w-full items-center gap-3 rounded-md py-1.5 text-left text-sm transition-colors',
                    collapsed ? 'justify-center px-0' : 'px-3',
                    // Solo expandido: reserva el hueco de la estrella para
                    // que el label nunca quede debajo de su hit area.
                    !collapsed && item.enabled ? 'pr-8' : '',
                    item.enabled ? 'cursor-pointer' : 'cursor-not-allowed',
                    active
                      ? 'bg-(--pcs-accent) text-white'
                      : item.enabled
                        ? 'text-(--pcs-text) hover:bg-(--pcs-surface-2) focus-visible:bg-(--pcs-surface-2)'
                        : 'text-(--pcs-text-muted)',
                    item.enabled ? '' : 'disabled:opacity-40',
                  ]
                    .filter(Boolean)
                    .join(' ')}
                >
                  <Icon size={16} className="shrink-0" aria-hidden />
                  {!collapsed && <span className="truncate">{item.label}</span>}
                  {!item.enabled && !collapsed && (
                    <span className="ml-auto text-xs text-(--pcs-text-muted)">pronto</span>
                  )}
                </button>
                {item.enabled && !collapsed && (
                  <button
                    type="button"
                    aria-pressed={starred}
                    aria-label={
                      starred ? `Quitar ${item.label} de favoritos` : `Añadir ${item.label} a favoritos`
                    }
                    onClick={() =>
                      toggleFavorite({ key: pageKey(item.id), label: item.label, page: item.id })
                    }
                    className={`pcs-touch-target absolute right-1.5 top-1/2 -translate-y-1/2 rounded p-2 transition-opacity cursor-pointer ${
                      starred
                        ? 'opacity-100'
                        : // Oculta la estrella sin robar clics ni dejarla
                          // invisible en el orden de tabulación: solo es
                          // interactiva con hover de fila o foco de teclado.
                          'opacity-0 pointer-events-none group-hover/nav:opacity-100 group-hover/nav:pointer-events-auto focus-visible:opacity-100 focus-visible:pointer-events-auto'
                    }`}
                    style={{
                      color: starred
                        ? active
                          ? '#fff'
                          : 'var(--pcs-accent)'
                        : 'var(--pcs-text-muted)',
                    }}
                  >
                    <Star size={13} fill={starred ? 'currentColor' : 'none'} aria-hidden />
                  </button>
                )}
              </div>
            );
          })}

          {/* Sección Favoritos (plan §19) */}
          {!collapsed && (
            <>
              <div className="px-3 pb-1 pt-4 text-xs font-semibold uppercase tracking-wide text-(--pcs-text-muted)">
                Favoritos
              </div>
              {favorites.length === 0 ? (
                <p className="px-3 text-xs text-(--pcs-text-muted)">
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
                        className="flex w-full items-center gap-3 rounded-md px-3 py-1 pr-8 text-left text-sm text-(--pcs-text) transition-colors cursor-pointer hover:bg-(--pcs-surface-2) focus-visible:bg-(--pcs-surface-2)"
                      >
                        <Icon size={14} className="shrink-0 text-(--pcs-accent)" aria-hidden />
                        <span className="truncate">{favorite.label}</span>
                      </button>
                      <button
                        type="button"
                        aria-label={`Quitar ${favorite.label} de favoritos`}
                        onClick={() => toggleFavorite(favorite)}
                        className="pcs-touch-target absolute right-1.5 top-1/2 -translate-y-1/2 rounded p-2 text-(--pcs-accent) transition-opacity cursor-pointer opacity-0 pointer-events-none group-hover/fav:opacity-100 group-hover/fav:pointer-events-auto focus-visible:opacity-100 focus-visible:pointer-events-auto"
                      >
                        <Star size={12} fill="currentColor" aria-hidden />
                      </button>
                    </div>
                  );
                })
              )}
            </>
          )}

          {collapsed && favorites.length > 0 && (
            <button
              type="button"
              aria-label={`${favorites.length} favoritos fijados — expandir menú`}
              title="Favoritos"
              onClick={() => setCollapsed(false)}
              className="mx-auto mt-2 flex items-center justify-center rounded-md bg-(--pcs-surface-2) p-2 text-(--pcs-accent) transition-colors cursor-pointer hover:brightness-110"
            >
              <Star size={14} fill="currentColor" aria-hidden />
            </button>
          )}
        </nav>

        <footer
          className={`flex items-center gap-2 border-t border-(--pcs-border) px-3 py-3 text-xs text-(--pcs-text-muted) ${collapsed ? 'justify-center' : 'justify-between'}`}
        >
          {!collapsed && <span className="truncate">{readyNotice ?? 'Cargando…'}</span>}
          <button
            type="button"
            aria-label="Ver atajos de teclado"
            onClick={openHelp}
            className="shrink-0 cursor-pointer rounded border border-(--pcs-border) px-1.5 font-semibold transition-colors hover:bg-(--pcs-surface-2) hover:text-(--pcs-text)"
          >
            ?
          </button>
        </footer>
      </aside>

      {/* Contenido */}
      <main id="main-content" className="flex-1 overflow-y-auto">
        <div className="mx-auto max-w-6xl px-8 py-8">
          <Breadcrumbs
            currentPage={page}
            toolId={currentToolId}
            toolName={currentToolName}
            onNavigate={onNavigate}
          />
          {children}
        </div>
      </main>

      {/* Task 027 — modal de versión nueva (una vez por sesión, sobre todo
          el layout: sidebar incluido). */}
      <UpdateStartupModal check={updateCheck} />
    </div>
  );
}
