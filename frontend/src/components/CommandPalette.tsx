import { useEffect, useState } from 'react';
import { Keyboard, Search, Settings, Terminal, Wrench, type LucideIcon } from 'lucide-react';
import { NAV_ITEMS } from '../layouts/AppLayout';
import type { PageId } from '../layouts/AppLayout';
import { bridge } from '../services/pcsuiteBridge';
import { openHelp } from '../shortcuts';
import { setThemePreference } from '../theme';
import { OperationIds } from '../types/bridge';
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
  ['dark', 'Oscuro'],
  ['light', 'Claro'],
  ['system', 'Sistema'],
] as const;

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

  // Refresca el índice de tools/devs cada vez que se abre (best-effort:
  // sin host nativo las categorías simplemente quedan vacías).
  useEffect(() => {
    if (!open) return;
    setQuery('');
    setIndex(0);
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
      label: item.label,
      category: 'Página',
      tags: [item.id],
      icon: item.icon,
      run: () => {
        onNavigate(item.id);
        onClose();
      },
    })),
    ...tools.map((tool) => ({
      key: `tool:${tool.id}`,
      label: tool.name,
      category: 'Tool',
      detail: tool.id,
      tags: [tool.id, ...tool.keywords],
      icon: Wrench,
      run: () => {
        onNavigate('tools', tool.id);
        onClose();
      },
    })),
    ...devs.map((dev) => ({
      key: `dev:${dev.id}`,
      label: dev.name,
      category: 'Dev',
      detail: dev.isInstalled ? (dev.version ?? undefined) : 'No instalado',
      tags: [dev.id],
      icon: Terminal,
      run: () => {
        onNavigate('devtools');
        onClose();
      },
    })),
    ...THEME_ENTRIES.map(([value, name]) => ({
      key: `set:theme-${value}`,
      label: `Tema: ${name}`,
      category: 'Ajustes',
      tags: ['tema', 'theme', value],
      icon: Settings,
      run: () => {
        setThemePreference(value);
        onClose();
      },
    })),
    {
      key: 'help:shortcuts',
      label: 'Ver atajos de teclado',
      category: 'Ajustes',
      tags: ['atajos', 'shortcuts', 'ayuda', 'help'],
      icon: Keyboard,
      run: () => {
        openHelp();
        onClose();
      },
    },
  ];

  const results = entries
    .map((entry) => ({ entry, score: fuzzyScore(query, entry) }))
    .filter((hit): hit is { entry: PaletteEntry; score: number } => hit.score !== null)
    .sort((a, b) => b.score - a.score)
    .slice(0, 20)
    .map((hit) => hit.entry);

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
        aria-label="Búsqueda global"
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
            placeholder="Buscar páginas, herramientas, ajustes…"
            aria-label="Buscar en PCSuite"
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
            {results.map((entry, i) => {
              const Icon = entry.icon;
              const selected = i === active;
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
                      style={{ color: 'var(--pcs-accent)', flexShrink: 0 }}
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
