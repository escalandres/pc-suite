import { useEffect, useState } from 'react';
import { Keyboard, Search, Settings, Terminal, Wrench, Clock, type LucideIcon } from 'lucide-react';
import { NAV_ITEMS } from '../layouts/AppLayout';
import type { PageId } from '../layouts/AppLayout';
import { bridge } from '../services/pcsuiteBridge';
import { openHelp } from '../shortcuts';
import { setThemePreference } from '../theme';
import { OperationIds } from '../types/bridge';
import { useTranslation } from '../hooks/useTranslation';
import type {
  DeveloperCollectionResult,
  DeveloperToolInfo,
  ToolCollectionResult,
  ToolInfo,
} from '../types/bridge';

/**
 * Búsqueda global / command palette (plan §18, Task 010 parte 10.4).
 * Ctrl+K la abre: fuzzy sobre páginas, tools, devs de desarrollador y
 * ajustes (tema); Enter ejecuta la selección, Esc cierra.
 * 14.3.4 — Recency/frecency: muestra items recientes al abrir sin query.
 */

interface PaletteEntry {
  key: string;
  label: string;
  category: string;
  detail?: string;
  tags: string[];
  icon: LucideIcon;
  run: () => void;
}

/** Puntuación fuzzy sencilla: subcadena > tags > subsecuencia. null = sin match. */
function fuzzyScore(query: string, entry: PaletteEntry): number | null {
  const q = query.trim().toLowerCase();
  if (!q) return 0;
  const label = entry.label.toLowerCase();
  const at = label.indexOf(q);
  if (at >= 0) return 100 - at;
  const tagAt = entry.tags.findIndex((tag) => tag.includes(q));
  if (tagAt >= 0) return 60 - tagAt;
  let i = 0;
  for (const char of label) {
    if (char === q[i]) i += 1;
    if (i === q.length) return 30;
  }
  return null;
}

const THEME_ENTRIES = [
  ['dark', 'theme.dark'],
  ['light', 'theme.light'],
  ['system', 'theme.system'],
] as const;

const RECENT_KEY = 'pcsuite.commandPalette.recent';
const MAX_RECENT = 8;

interface RecentEntry {
  key: string;
  timestamp: number;
  count: number;
}

function getRecent(): RecentEntry[] {
  try {
    const raw = localStorage.getItem(RECENT_KEY);
    if (raw) return JSON.parse(raw) as RecentEntry[];
  } catch {
    // ignore
  }
  return [];
}

function addRecent(key: string): void {
  try {
    const recent = getRecent();
    const existing = recent.findIndex((r) => r.key === key);
    const now = Date.now();
    if (existing >= 0) {
      recent[existing] = { ...recent[existing], timestamp: now, count: recent[existing].count + 1 };
    } else {
      recent.unshift({ key, timestamp: now, count: 1 });
    }
    recent.sort((a, b) => b.count - a.count || b.timestamp - a.timestamp);
    localStorage.setItem(RECENT_KEY, JSON.stringify(recent.slice(0, MAX_RECENT)));
  } catch {
    // ignore
  }
}

interface CommandPaletteProps {
  open: boolean;
  onClose: () => void;
  onNavigate: (page: PageId, toolId?: string) => void;
}

export function CommandPalette({ open, onClose, onNavigate }: CommandPaletteProps) {
  const [query, setQuery] = useState('');
  const [index, setIndex] = useState(0);
  const [tools, setTools] = useState<ToolInfo[]>([]);
  const [devs, setDevs] = useState<DeveloperToolInfo[]>([]);
  const [recent, setRecent] = useState<RecentEntry[]>([]);
  const { t } = useTranslation();

  // Refresca el índice de tools/devs cada vez que se abre (best-effort:
  // sin host nativo las categorías simplemente quedan vacías).
  useEffect(() => {
    if (!open) return;
    setQuery('');
    setIndex(0);
    setRecent(getRecent());
    let cancelled = false;
    void (async () => {
      const [toolResult, devResult] = await Promise.all([
        bridge.call<ToolCollectionResult>(OperationIds.toolsList).catch(() => null),
        bridge.call<DeveloperCollectionResult>(OperationIds.developerList).catch(() => null),
      ]);
      if (cancelled) return;
      if (toolResult) setTools(toolResult.tools);
      if (devResult) setDevs(devResult.tools);
    })();
    return () => {
      cancelled = true;
    };
  }, [open]);

  if (!open) return null;

  const entries: PaletteEntry[] = [
    ...NAV_ITEMS.filter((item) => item.enabled).map((item) => ({
      key: `page:${item.id}`,
      label: t(item.labelKey),
      category: t('common.page'),
      tags: [item.id],
      icon: item.icon,
      run: () => {
        onNavigate(item.id);
        onClose();
        addRecent(`page:${item.id}`);
      },
    })),
    ...tools.map((tool) => ({
      key: `tool:${tool.id}`,
      label: tool.name,
      category: t('common.tool'),
      detail: tool.id,
      tags: [tool.id, ...tool.keywords],
      icon: Wrench,
      run: () => {
        onNavigate('tools', tool.id);
        onClose();
        addRecent(`tool:${tool.id}`);
      },
    })),
    ...devs.map((dev) => ({
      key: `dev:${dev.id}`,
      label: dev.name,
      category: t('common.dev'),
      detail: dev.isInstalled ? (dev.version ?? undefined) : t('common.notInstalled'),
      tags: [dev.id],
      icon: Terminal,
      run: () => {
        onNavigate('devtools');
        onClose();
        addRecent(`dev:${dev.id}`);
      },
    })),
    ...THEME_ENTRIES.map(([value, name]) => ({
      key: `set:theme-${value}`,
      label: t(name as any),
      category: t('common.settings'),
      tags: ['tema', 'theme', value],
      icon: Settings,
      run: () => {
        setThemePreference(value);
        onClose();
        addRecent(`set:theme-${value}`);
      },
    })),
    {
      key: 'help:shortcuts',
      label: t('common.shortcuts'),
      category: t('common.settings'),
      tags: ['atajos', 'shortcuts', 'ayuda', 'help'],
      icon: Keyboard,
      run: () => {
        openHelp();
        onClose();
        addRecent('help:shortcuts');
      },
    },
  ];

  const recentEntries = recent
    .map((r) => entries.find((e) => e.key === r.key))
    .filter((e): e is PaletteEntry => !!e);

  const results = query
    ? entries
        .map((entry) => ({ entry, score: fuzzyScore(query, entry) }))
        .filter((hit): hit is { entry: PaletteEntry; score: number } => hit.score !== null)
        .sort((a, b) => b.score - a.score)
        .slice(0, 20)
        .map((hit) => hit.entry)
    : recentEntries.length > 0
    ? recentEntries
    : entries.slice(0, 20);

  const active = Math.min(index, Math.max(results.length - 1, 0));

  return (
    <div
      className="fixed inset-0 z-50 flex items-start justify-center pt-[15vh]"
      style={{ background: 'rgba(0, 0, 0, 0.55)' }}
      onClick={onClose}
      role="presentation"
    >
      <div
        className="w-full max-w-xl overflow-hidden rounded-lg border shadow-xl"
        style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
        onClick={(e) => e.stopPropagation()}
        role="dialog"
        aria-modal="true"
        aria-label={t('common.globalSearch')}
      >
        <div
          className="flex items-center gap-2 border-b px-3"
          style={{ borderColor: 'var(--pcs-border)' }}
        >
          <Search size={15} aria-hidden style={{ color: 'var(--pcs-text-muted)' }} />
          <input
            autoFocus
            value={query}
            onChange={(e) => {
              setQuery(e.target.value);
              setIndex(0);
            }}
            onKeyDown={(e) => {
              if (e.key === 'ArrowDown') {
                e.preventDefault();
                setIndex((i) => (results.length ? (i + 1) % results.length : 0));
              } else if (e.key === 'ArrowUp') {
                e.preventDefault();
                setIndex((i) => (results.length ? (i - 1 + results.length) % results.length : 0));
              } else if (e.key === 'Enter') {
                e.preventDefault();
                results[active]?.run();
              } else if (e.key === 'Escape') {
                e.preventDefault();
                onClose();
              }
            }}
            placeholder={t('common.searchPlaceholder')}
            aria-label={t('common.searchInPCSuite')}
            className="w-full bg-transparent py-3 text-sm outline-none"
            style={{ color: 'var(--pcs-text)' }}
          />
          <kbd
            className="shrink-0 rounded px-1.5 py-0.5 text-xs"
            style={{ border: '1px solid var(--pcs-border)', color: 'var(--pcs-text-muted)' }}
          >
            Esc
          </kbd>
        </div>

        {results.length === 0 ? (
          <p
            className="px-4 py-6 text-center text-sm"
            style={{ color: 'var(--pcs-text-muted)' }}
          >
            Sin resultados para «{query}».
          </p>
        ) : (
          <ul className="max-h-72 overflow-y-auto py-1" role="listbox" aria-label="Resultados">
            {!query && recentEntries.length > 0 && (
              <li className="px-3 py-1.5 text-xs font-semibold uppercase tracking-wide" style={{ color: 'var(--pcs-text-muted)' }}>
                <Clock size={10} aria-hidden className="inline mr-1" />
                Recientes
              </li>
            )}
            {results.map((entry, i) => {
              const Icon = entry.icon;
              const selected = i === active;
              const isRecent = !query && recentEntries.some((r) => r.key === entry.key);
              return (
                <li key={entry.key} role="option" aria-selected={selected}>
                  <button
                    type="button"
                    onClick={entry.run}
                    onMouseEnter={() => setIndex(i)}
                    className="flex w-full items-center gap-3 px-3 py-2 text-left text-sm"
                    style={{
                      background: selected ? 'var(--pcs-surface-2)' : 'transparent',
                      color: 'var(--pcs-text)',
                    }}
                  >
                    <Icon
                      size={15}
                      aria-hidden
                      style={{ color: isRecent ? 'var(--pcs-warning)' : 'var(--pcs-accent)', flexShrink: 0 }}
                    />
                    <span className="truncate">{entry.label}</span>
                    {entry.detail && (
                      <span
                        className="truncate text-xs"
                        style={{ color: 'var(--pcs-text-muted)' }}
                      >
                        {entry.detail}
                      </span>
                    )}
                    {isRecent && (
                      <Clock size={10} aria-hidden className="ml-auto shrink-0" style={{ color: 'var(--pcs-warning)' }} />
                    )}
                    <span
                      className="ml-auto shrink-0 rounded px-1.5 py-0.5 text-xs"
                      style={{
                        background: 'var(--pcs-bg)',
                        border: '1px solid var(--pcs-border)',
                        color: 'var(--pcs-text-muted)',
                      }}
                    >
                      {entry.category}
                    </span>
                  </button>
                </li>
              );
            })}
          </ul>
        )}
      </div>
    </div>
  );
}
