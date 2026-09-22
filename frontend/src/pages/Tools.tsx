import { useEffect, useState } from 'react';
import { Search, Play, Wrench } from 'lucide-react';
import { bridge, BridgeError } from '../services/pcsuiteBridge';
import { OperationIds } from '../types/bridge';
import type { ToolCollectionResult, ToolExecuteResult, ToolInfo } from '../types/bridge';

function errorMessage(error: unknown, fallback: string): string {
  return error instanceof BridgeError ? error.message : fallback;
}

/**
 * Página de herramientas (Task 002): la UI descubre las tools por
 * metadatos (tools.list / tools.search) y las ejecuta (tool.execute)
 * sin tener ninguna tool hardcodeada.
 *
 * El editor de entrada es genérico: un JSON precargado con el
 * exampleInput de cada tool.
 */
export function Tools() {
  const [tools, setTools] = useState<ToolInfo[]>([]);
  const [query, setQuery] = useState('');
  const [loading, setLoading] = useState(true);
  const [listError, setListError] = useState<string | null>(null);

  const [selected, setSelected] = useState<ToolInfo | null>(null);
  const [inputText, setInputText] = useState('');
  const [output, setOutput] = useState<string | null>(null);
  const [runError, setRunError] = useState<string | null>(null);
  const [running, setRunning] = useState(false);

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

  const execute = async () => {
    if (!selected) return;

    let parsed: unknown;
    try {
      parsed = JSON.parse(inputText);
    } catch {
      setRunError('La entrada no es JSON válido.');
      return;
    }

    setRunning(true);
    setOutput(null);
    setRunError(null);
    try {
      const result = await bridge.call<ToolExecuteResult>(OperationIds.toolExecute, {
        toolId: selected.id,
        input: parsed,
      });
      setOutput(JSON.stringify(result.output ?? null, null, 2));
    } catch (error) {
      setRunError(errorMessage(error, 'No se pudo ejecutar la herramienta.'));
    } finally {
      setRunning(false);
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

          {loading && (
            <p className="text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
              Buscando…
            </p>
          )}

          {!loading && !listError && tools.length === 0 && (
            <p className="text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
              Sin resultados para «{query}».
            </p>
          )}

          <ul className="space-y-2">
            {tools.map((tool) => {
              const active = selected?.id === tool.id;
              return (
                <li key={tool.id}>
                  <button
                    type="button"
                    onClick={() => selectTool(tool)}
                    className="w-full rounded-lg border p-3 text-left transition-colors"
                    style={{
                      background: active ? 'var(--pcs-surface-2)' : 'var(--pcs-surface)',
                      borderColor: active ? 'var(--pcs-accent)' : 'var(--pcs-border)',
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
                </li>
              );
            })}
          </ul>
        </section>

        {/* Ejecución genérica */}
        <section>
          {!selected ? (
            <div
              className="flex h-full min-h-64 items-center justify-center rounded-lg border border-dashed p-6 text-center text-sm"
              style={{ borderColor: 'var(--pcs-border)', color: 'var(--pcs-text-muted)' }}
            >
              Selecciona una herramienta para ejecutarla.
            </div>
          ) : (
            <div
              className="space-y-3 rounded-lg border p-4"
              style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
            >
              <div>
                <h2 className="font-medium">{selected.name}</h2>
                <p className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
                  Entrada JSON — edítala según lo que necesites:
                </p>
              </div>

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

              <button
                type="button"
                onClick={() => void execute()}
                disabled={running}
                className="flex items-center gap-2 rounded-md px-3 py-2 text-sm font-medium transition-colors disabled:opacity-50"
                style={{ background: 'var(--pcs-accent)', color: '#fff' }}
              >
                <Play size={14} aria-hidden />
                {running ? 'Ejecutando…' : 'Ejecutar'}
              </button>

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
                  <div className="mb-1 text-xs uppercase tracking-wide" style={{ color: 'var(--pcs-text-muted)' }}>
                    Resultado
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
