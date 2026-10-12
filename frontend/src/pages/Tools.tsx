import { useEffect, useState } from 'react';
import { Copy, Play, Search, Star, Wrench } from 'lucide-react';
import { bridge, BridgeError } from '../services/pcsuiteBridge';
import { OperationIds } from '../types/bridge';
import type { ToolCollectionResult, ToolExecuteResult, ToolInfo } from '../types/bridge';
import { ToolForm } from '../components/ToolForm';
import { toolKey, toggleFavorite, useFavorites } from '../favorites';

function errorMessage(error: unknown, fallback: string): string {
  return error instanceof BridgeError ? error.message : fallback;
}

/**
 * Representa la salida de una tool en el panel de resultado: las tools
 * devuelven el VALOR generado (string o lista de strings) — solo cae a
 * JSON cuando la salida es un objeto.
 */
function formatToolOutput(value: unknown): string {
  if (typeof value === 'string') return value;
  if (Array.isArray(value)) return value.map((item) => String(item)).join('\n');
  return JSON.stringify(value ?? null, null, 2);
}

/** Skeleton item para la lista virtualizada */
function ToolSkeleton() {
  return (
    <li className="group/li">
      <div className="relative">
        <div className="w-full rounded-lg border p-3" style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}>
          <div className="pcs-skeleton h-4 w-3/4 mb-2 rounded" />
          <div className="pcs-skeleton h-3 w-1/2 mb-2 rounded" />
          <div className="flex gap-1">
            <div className="pcs-skeleton h-4 w-16 rounded" />
            <div className="pcs-skeleton h-4 w-16 rounded" />
          </div>
        </div>
        <div className="pcs-skeleton h-6 w-6 absolute right-2 top-2 rounded-full" />
      </div>
    </li>
  );
}

/** Item de tool para lista */
interface ToolRowProps {
  tool: ToolInfo;
  active: boolean;
  starred: boolean;
  onSelect: () => void;
  onToggleFavorite: () => void;
}

function ToolRow({ tool, active, starred, onSelect, onToggleFavorite }: ToolRowProps) {
  return (
    <li key={tool.id} className="group/li pcs-stagger-in">
      <div className="relative">
        <button
          type="button"
          onClick={onSelect}
          className="w-full rounded-lg border p-3 text-left transition-colors pcs-press-feedback"
          style={{
            background: active ? 'var(--pcs-surface-2)' : 'var(--pcs-surface)',
            borderColor: active ? 'var(--pcs-accent)' : 'var(--pcs-border)',
            paddingRight: '2.25rem',
          }}
        >
            <div className="flex items-center gap-2">
              <Wrench size={14} aria-hidden style={{ color: 'var(--pcs-accent)' }} />
              <span className="text-sm font-medium">{tool.name}</span>
              <code
                className="ml-auto text-xs"
                style={{ color: 'var(--pcs-text-muted)' }}
              >
                {tool.id}
              </code>
            </div>
            <p className="mt-1 text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
              {tool.description}
            </p>
            <div className="mt-2 flex flex-wrap gap-1">
              {tool.keywords.slice(0, 5).map((keyword) => (
                <span
                  key={keyword}
                  className="rounded px-1.5 py-0.5 text-xs"
                  style={{
                    background: 'var(--pcs-bg)',
                    color: 'var(--pcs-text-muted)',
                    border: '1px solid var(--pcs-border)',
                  }}
                >
                  {keyword}
                </span>
              ))}
            </div>
          </button>
          <button
            type="button"
            aria-pressed={starred}
            aria-label={
              starred
                ? `Quitar ${tool.name} de favoritos`
                : `Añadir ${tool.name} a favoritos`
            }
            onClick={onToggleFavorite}
            className={`pcs-touch-target absolute right-2 top-2 rounded p-2 transition-opacity ${
              starred ? 'opacity-100' : 'opacity-0 group-hover/li:opacity-100'
            }`}
            style={{
              color: starred ? 'var(--pcs-accent)' : 'var(--pcs-text-muted)',
              cursor: 'pointer',
            }}
          >
            <Star size={14} fill={starred ? 'currentColor' : 'none'} aria-hidden />
          </button>
        </div>
      </li>
  );
}

/**
 * Página de herramientas (Task 002): la UI descubre las tools por
 * metadatos (tools.list / tools.search) y las ejecuta (tool.execute)
 * sin tener ninguna tool hardcodeada.
 *
 * El editor de entrada es genérico: un JSON precargado con el
 * exampleInput de cada tool.
 */
export function Tools({ focus }: { focus?: { toolId: string } | null }) {
  const [tools, setTools] = useState<ToolInfo[]>([]);
  const [query, setQuery] = useState('');
  const [loading, setLoading] = useState(true);
  const [listError, setListError] = useState<string | null>(null);
  const favorites = useFavorites();

  const [selected, setSelected] = useState<ToolInfo | null>(null);
  const [inputText, setInputText] = useState('');
  const [output, setOutput] = useState<string | null>(null);
  const [runError, setRunError] = useState<string | null>(null);
  const [running, setRunning] = useState(false);
  const [copied, setCopied] = useState(false);

  // Carga inicial + búsqueda con debounce (250 ms).
  useEffect(() => {
    let cancelled = false;

    const load = async (search: string) => {
      setLoading(true);
      setListError(null);
      try {
        const result = search
          ? await bridge.call<ToolCollectionResult>(OperationIds.toolsSearch, { query: search })
          : await bridge.call<ToolCollectionResult>(OperationIds.toolsList);
        if (!cancelled) {
          setTools(result.tools ?? []);
        }
      } catch (error) {
        if (!cancelled) {
          setListError(errorMessage(error, 'No se pudieron cargar las herramientas.'));
        }
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    };

    const trimmed = query.trim();
    if (!trimmed) {
      void load('');
      return () => {
        cancelled = true;
      };
    }

    const timer = setTimeout(() => void load(trimmed), 250);
    return () => {
      cancelled = true;
      clearTimeout(timer);
    };
  }, [query]);

  const selectTool = (tool: ToolInfo) => {
    setSelected(tool);
    setInputText(
      tool.exampleInput !== undefined ? JSON.stringify(tool.exampleInput, null, 2) : '{}',
    );
    setOutput(null);
    setRunError(null);
  };

  // Apertura desde un favorito (10.3): lista completa + selección.
  useEffect(() => {
    if (focus) setQuery('');
  }, [focus]);

  useEffect(() => {
    if (!focus) return;
    const match = tools.find((tool) => tool.id === focus.toolId);
    if (match && selected?.id !== match.id) selectTool(match);
  }, [focus, tools]);

  /** Formatea JSON en el textarea */
  const formatJson = () => {
    try {
      const parsed = JSON.parse(inputText);
      setInputText(JSON.stringify(parsed, null, 2));
    } catch {
      // Silencioso: JSON inválido
    }
  };

  // 14.3.2 / 14.3.5 — Atajos de teclado del editor JSON (solo fallback
  // para tools sin esquema de parámetros): Ctrl+Enter ejecutar,
  // Ctrl+Shift+F formatear. El formulario tiene su propio Ctrl+Enter.
  useEffect(() => {
    if (!selected || selected.parameters.length > 0) return;
    const onKeyDown = (e: KeyboardEvent) => {
      const target = e.target as HTMLElement;
      if (target.tagName !== 'TEXTAREA') return;
      if ((e.ctrlKey || e.metaKey) && e.key === 'Enter') {
        e.preventDefault();
        void executeFromText();
      } else if ((e.ctrlKey || e.metaKey) && e.shiftKey && e.key === 'F') {
        e.preventDefault();
        formatJson();
      }
    };
    document.addEventListener('keydown', onKeyDown);
    return () => document.removeEventListener('keydown', onKeyDown);
  }, [selected, inputText, running]);

  /** Ejecuta la tool con el payload indicado (formulario o editor JSON). */
  const executePayload = async (input: unknown) => {
    if (!selected) return;
    setRunning(true);
    setOutput(null);
    setRunError(null);
    try {
      const result = await bridge.call<ToolExecuteResult>(OperationIds.toolExecute, {
        toolId: selected.id,
        input,
      });
      setOutput(formatToolOutput(result.output));
    } catch (error) {
      setRunError(errorMessage(error, 'No se pudo ejecutar la herramienta.'));
    } finally {
      setRunning(false);
    }
  };

  /** Editor JSON (fallback para tools sin esquema de parámetros). */
  const executeFromText = () => {
    let parsed: unknown;
    try {
      parsed = JSON.parse(inputText);
    } catch {
      setRunError('La entrada no es JSON válido.');
      return;
    }
    void executePayload(parsed);
  };

  /** Copia el resultado al portapapeles (acción local de la UI). */
  const copyOutput = async () => {
    if (output === null) return;
    try {
      await navigator.clipboard.writeText(output);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      // Portapapeles no disponible: sin feedback, no rompe nada.
    }
  };

  return (
    <div className="space-y-6">
      <header>
        <h1 className="text-2xl font-semibold">Herramientas</h1>
        <p className="mt-1 text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
          Las herramientas se descubren desde el nativo por metadatos — ninguna está hardcodeada
          en esta UI.
        </p>
      </header>

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
        {/* Lista + búsqueda */}
        <section className="space-y-3">
          <div
            className="flex items-center gap-2 rounded-md border px-3"
            style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
          >
            <Search size={15} aria-hidden style={{ color: 'var(--pcs-text-muted)' }} />
            <input
              type="search"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder="Buscar herramientas…"
              className="w-full bg-transparent py-2 text-sm outline-none"
              style={{ color: 'var(--pcs-text)' }}
            />
          </div>

          {listError && (
            <div
              className="rounded-md border px-4 py-3 text-sm"
              style={{ borderColor: 'var(--pcs-danger)', color: 'var(--pcs-danger)' }}
              role="alert"
            >
              {listError}
            </div>
          )}

          {!loading && !listError && tools.length === 0 && (
            <div className="flex flex-col items-center justify-center py-12 px-4 text-center">
              <Wrench size={48} aria-hidden className="mb-4 opacity-30" style={{ color: 'var(--pcs-accent)' }} />
              <h3 className="text-lg font-medium mb-1">
                {query ? 'Sin resultados' : 'No hay herramientas disponibles'}
              </h3>
              <p className="text-sm mb-4" style={{ color: 'var(--pcs-text-muted)' }}>
                {query
                  ? `No se encontraron herramientas para «{query}».`
                  : 'El host nativo no ha registrado herramientas aún.'}
              </p>
              {!query && (
                <button
                  type="button"
                  onClick={() => window.location.reload()}
                  className="rounded-md px-3 py-2 text-sm font-medium"
                  style={{ background: 'var(--pcs-accent)', color: '#fff' }}
                >
                  Recargar
                </button>
              )}
            </div>
          )}

          {loading ? (
            <ul className="space-y-2 pcs-stagger-in" role="list" aria-busy="true">
              {[...Array(6)].map((_, i) => (
                <ToolSkeleton key={i} />
              ))}
            </ul>
          ) : (
            <ul className="space-y-2 pcs-stagger-in" role="list">
              {tools.map((tool) => {
                const active = selected?.id === tool.id;
                const starred = favorites.some((favorite) => favorite.key === toolKey(tool.id));
                return (
                  <ToolRow
                    key={tool.id}
                    tool={tool}
                    active={active}
                    starred={starred}
                    onSelect={() => selectTool(tool)}
                    onToggleFavorite={() =>
                      toggleFavorite({
                        key: toolKey(tool.id),
                        label: tool.name,
                        page: 'tools',
                        toolId: tool.id,
                      })
                    }
                  />
                );
              })}
            </ul>
          )}
        </section>

        {/* Configuración + ejecución */}
        <section>
          {!selected ? (
            <div
              className="flex h-full min-h-64 items-center justify-center rounded-lg border border-dashed p-6 text-center text-sm"
              style={{ borderColor: 'var(--pcs-border)', color: 'var(--pcs-text-muted)' }}
            >
              Selecciona una herramienta para configurarla y ejecutarla.
            </div>
          ) : (
            <div
              className="space-y-3 rounded-lg border p-4"
              style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
            >
              <div>
                <h2 className="font-medium">{selected.name}</h2>
                <p className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
                  {selected.parameters.length > 0
                    ? 'Configura los parámetros y ejecuta — Ctrl+Enter también funciona.'
                    : 'Entrada JSON — edítala según lo que necesites:'}
                </p>
              </div>

              {selected.parameters.length > 0 ? (
                <ToolForm
                  key={selected.id}
                  tool={selected}
                  running={running}
                  onExecute={(payload) => void executePayload(payload)}
                />
              ) : (
                <>
                  <textarea
                    value={inputText}
                    onChange={(e) => setInputText(e.target.value)}
                    spellCheck={false}
                    rows={8}
                    className="w-full resize-y rounded-md border p-3 font-mono text-xs outline-none"
                    style={{
                      background: 'var(--pcs-bg)',
                      borderColor: 'var(--pcs-border)',
                      color: 'var(--pcs-text)',
                    }}
                  />

                  {/* 14.3.2 — Helper text para JSON textarea */}
                  <p className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
                    Formato: <code className="font-mono">&#123; &#34;clave&#34;: &#34;valor&#34; &#125;</code> |
                    Atajos: <kbd className="px-1 rounded border" style={{ borderColor: 'var(--pcs-border)', background: 'var(--pcs-bg)' }}>Ctrl+Enter</kbd> Ejecutar,
                    <kbd className="px-1 rounded border" style={{ borderColor: 'var(--pcs-border)', background: 'var(--pcs-bg)' }}>Ctrl+Shift+F</kbd> Formatear
                  </p>

                  <button
                    type="button"
                    onClick={() => void executeFromText()}
                    disabled={running}
                    className="flex items-center gap-2 rounded-md px-3 py-2 text-sm font-medium transition-colors disabled:opacity-50"
                    style={{ background: 'var(--pcs-accent)', color: '#fff' }}
                  >
                    <Play size={14} aria-hidden />
                    {running ? 'Ejecutando…' : 'Ejecutar'}
                  </button>
                </>
              )}

              {runError && (
                <div
                  className="rounded-md border px-3 py-2 text-sm"
                  style={{ borderColor: 'var(--pcs-danger)', color: 'var(--pcs-danger)' }}
                  role="alert"
                >
                  {runError}
                </div>
              )}

              {output !== null && (
                <div>
                  <div className="mb-1 flex items-center justify-between gap-2">
                    <span
                      className="text-xs uppercase tracking-wide"
                      style={{ color: 'var(--pcs-text-muted)' }}
                    >
                      Resultado
                    </span>
                    <button
                      type="button"
                      onClick={() => void copyOutput()}
                      className="flex shrink-0 cursor-pointer items-center gap-1.5 rounded-md border px-2 py-1 text-xs font-medium transition-colors hover:bg-(--pcs-surface-2)"
                      style={{ borderColor: 'var(--pcs-border)', color: 'var(--pcs-text)' }}
                    >
                      <Copy size={12} aria-hidden />
                      {copied ? '¡Copiado!' : 'Copiar'}
                    </button>
                  </div>
                  <pre
                    className="overflow-auto rounded-md border p-3 font-mono text-xs whitespace-pre-wrap"
                    style={{
                      background: 'var(--pcs-bg)',
                      borderColor: 'var(--pcs-border)',
                      color: 'var(--pcs-success)',
                    }}
                  >
                    {output}
                  </pre>
                </div>
              )}
            </div>
          )}
        </section>
      </div>
    </div>
  );
}
