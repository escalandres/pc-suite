import { ChevronRight, Home } from 'lucide-react';
import { NAV_ITEMS } from '../layouts/AppLayout';
import type { PageId } from '../layouts/AppLayout';
import { useTranslation } from '../hooks/useTranslation';

interface BreadcrumbsProps {
  /** Página actual */
  currentPage: PageId;
  /** toolId opcional para drill-down (ej. tools/tool-id) */
  toolId?: string;
  /** toolName opcional para mostrar en el breadcrumb */
  toolName?: string;
  /** onNavigate para navegar al hacer click */
  onNavigate: (page: PageId, toolId?: string) => void;
}

/**
 * Breadcrumbs (Task 14.3.1): navegación jerárquica para páginas con drill-down.
 * Se muestra en páginas: Tools (cuando hay tool seleccionada), DevTools, Plugins.
 */
export function Breadcrumbs({ currentPage, toolId, toolName, onNavigate }: BreadcrumbsProps) {
  const { t } = useTranslation();
  const pageItem = NAV_ITEMS.find((item) => item.id === currentPage);

  // Páginas que no necesitan breadcrumb (raíz)
  const rootPages: PageId[] = ['home', 'settings', 'activity', 'cleaner', 'security', 'winget', 'wsl', 'clamav'];
  if (rootPages.includes(currentPage) && !toolId) return null;

  const crumbs: { label: string; page?: PageId; toolId?: string; isLast: boolean }[] = [
    { label: t('sidebar.home'), page: 'home', isLast: false },
  ];

  if (pageItem) {
    crumbs.push({ label: t(pageItem.labelKey), page: currentPage, isLast: !toolId });
  }

  if (toolId && toolName) {
    crumbs.push({ label: toolName, toolId, isLast: true });
  }

  if (crumbs.length <= 1) return null;

  function renderCrumb(crumb: typeof crumbs[0], index: number) {
    const isFirst = index === 0;
    const separator = isFirst ? null : <ChevronRight size={12} aria-hidden className="shrink-0" />;

    if (crumb.isLast) {
      return (
        <li key={`${crumb.page ?? 'tool'}-${crumb.toolId ?? index}`} className="flex items-center gap-1.5">
          {separator}
          <span className="font-medium truncate max-w-[200px]" style={{ color: 'var(--pcs-text)' }}>
            {crumb.label}
          </span>
        </li>
      );
    }

    if (crumb.page === 'home') {
      return (
        <li key="home" className="flex items-center gap-1.5">
          {separator}
          <button
            type="button"
            onClick={() => onNavigate('home')}
            className="flex items-center gap-1 hover:underline"
            style={{ color: 'var(--pcs-text-muted)', cursor: 'pointer' }}
          >
            <Home size={12} aria-hidden />
            <span className="sr-only">{t('sidebar.home')}</span>
          </button>
        </li>
      );
    }

    if (crumb.page) {
      return (
        <li key={crumb.page} className="flex items-center gap-1.5">
          {separator}
          <button
            type="button"
            onClick={() => onNavigate(crumb.page!)}
            className="hover:underline"
            style={{ color: 'var(--pcs-text-muted)', cursor: 'pointer' }}
          >
            {crumb.label}
          </button>
        </li>
      );
    }

    return (
      <li key={`tool-${crumb.toolId ?? index}`} className="flex items-center gap-1.5">
        {separator}
        <span className="font-medium truncate max-w-[200px]" style={{ color: 'var(--pcs-text)' }}>
          {crumb.label}
        </span>
      </li>
    );
  }

  return (
    <nav
      className="mb-6 flex flex-wrap items-center gap-1.5 text-sm"
      aria-label={t('common.breadcrumbs')}
      style={{ color: 'var(--pcs-text-muted)' }}
    >
      <ol className="flex items-center gap-1.5" role="list">
        {crumbs.map(renderCrumb)}
      </ol>
    </nav>
  );
}