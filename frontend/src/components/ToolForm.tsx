import { useState } from 'react';
import type { ToolInfo, ToolParameter } from '../types/bridge';

/**
 * Formulario dinámico de una tool: se genera entero desde el esquema de
 * parámetros que declara el nativo (ninguna tool está hardcodeada aquí).
 * Al enviar construye el payload JSON y lo entrega al padre, que lo
 * ejecuta vía tool.execute.
 */

/** Estado editable: booleanos como boolean, el resto como string. */
type FieldValue = string | boolean;

function initialState(tool: ToolInfo): Record<string, FieldValue> {
  const state: Record<string, FieldValue> = {};
  for (const parameter of tool.parameters) {
    if (parameter.type === 'boolean') {
      state[parameter.name] = parameter.default === true;
    } else {
      state[parameter.name] =
        parameter.default === undefined || parameter.default === null
          ? ''
          : String(parameter.default);
    }
  }
  return state;
}

/**
 * Construye el payload desde el formulario. Los campos opcionales vacíos se
 * omiten (el nativo aplica su valor por defecto); devuelve los nombres de
 * los requeridos que faltan para marcarlos en rojo.
 */
function buildPayload(
  parameters: ToolParameter[],
  values: Record<string, FieldValue>,
): { payload: Record<string, unknown>; missing: string[] } {
  const payload: Record<string, unknown> = {};
  const missing: string[] = [];

  for (const parameter of parameters) {
    const value = values[parameter.name];

    if (parameter.type === 'boolean') {
      payload[parameter.name] = Boolean(value);
      continue;
    }

    const text = typeof value === 'string' ? value : '';
    if (text === '') {
      if (parameter.required) missing.push(parameter.name);
      continue;
    }

    if (parameter.type === 'number') {
      const parsed = Number(text);
      if (!Number.isFinite(parsed)) {
        missing.push(parameter.name);
        continue;
      }
      payload[parameter.name] = parsed;
    } else {
      payload[parameter.name] = text;
    }
  }

  return { payload, missing };
}

interface ToolFormProps {
  tool: ToolInfo;
  running: boolean;
  /** Recibe el payload validado del formulario. */
  onExecute: (payload: Record<string, unknown>) => void;
}

export function ToolForm({ tool, running, onExecute }: ToolFormProps) {
  const [values, setValues] = useState<Record<string, FieldValue>>(() => initialState(tool));
  const [missing, setMissing] = useState<Record<string, boolean>>({});

  const setValue = (name: string, value: FieldValue) => {
    setValues((previous) => ({ ...previous, [name]: value }));
    setMissing((previous) => (previous[name] ? { ...previous, [name]: false } : previous));
  };

  const submit = () => {
    const { payload, missing: required } = buildPayload(tool.parameters, values);
    setMissing(Object.fromEntries(required.map((name) => [name, true])));
    if (required.length === 0) {
      onExecute(payload);
    }
  };

  const inputStyle = (parameter: ToolParameter) => ({
    background: 'var(--pcs-bg)',
    borderColor: missing[parameter.name] ? 'var(--pcs-danger)' : 'var(--pcs-border)',
    color: 'var(--pcs-text)',
  });

  return (
    <form
      onSubmit={(event) => {
        event.preventDefault();
        submit();
      }}
      onKeyDown={(event) => {
        // Ctrl+Enter ejecuta desde cualquier campo del formulario.
        if ((event.ctrlKey || event.metaKey) && event.key === 'Enter') {
          event.preventDefault();
          submit();
        }
      }}
      noValidate
      className="space-y-4"
    >
      {tool.parameters.map((parameter) => (
        <div key={parameter.name} className="space-y-1">
          {parameter.type !== 'boolean' && (
            <label
              htmlFor={`tool-param-${parameter.name}`}
              className="text-sm font-medium"
            >
              {parameter.label}
              {parameter.required && (
                <span style={{ color: 'var(--pcs-danger)' }}> *</span>
              )}
            </label>
          )}

          {parameter.type === 'boolean' && (
            <label className="flex cursor-pointer items-center gap-2 text-sm font-medium">
              <input
                type="checkbox"
                checked={Boolean(values[parameter.name])}
                onChange={(event) => setValue(parameter.name, event.target.checked)}
                className="h-4 w-4 cursor-pointer accent-[var(--pcs-accent)]"
              />
              {parameter.label}
            </label>
          )}

          {(parameter.type === 'text' || parameter.type === 'number') && (
            <input
              id={`tool-param-${parameter.name}`}
              type={parameter.type === 'number' ? 'number' : 'text'}
              value={String(values[parameter.name] ?? '')}
              onChange={(event) => setValue(parameter.name, event.target.value)}
              placeholder={parameter.placeholder ?? undefined}
              min={parameter.type === 'number' ? (parameter.min ?? undefined) : undefined}
              max={parameter.type === 'number' ? (parameter.max ?? undefined) : undefined}
              disabled={running}
              className="w-full rounded-md border px-3 py-2 text-sm outline-none transition-colors disabled:opacity-50"
              style={inputStyle(parameter)}
            />
          )}

          {parameter.type === 'textArea' && (
            <textarea
              id={`tool-param-${parameter.name}`}
              value={String(values[parameter.name] ?? '')}
              onChange={(event) => setValue(parameter.name, event.target.value)}
              placeholder={parameter.placeholder ?? undefined}
              rows={5}
              spellCheck={false}
              disabled={running}
              className="w-full resize-y rounded-md border p-3 font-mono text-xs outline-none transition-colors disabled:opacity-50"
              style={inputStyle(parameter)}
            />
          )}

          {parameter.type === 'select' && (
            <select
              id={`tool-param-${parameter.name}`}
              value={String(values[parameter.name] ?? parameter.options[0] ?? '')}
              onChange={(event) => setValue(parameter.name, event.target.value)}
              disabled={running}
              className="w-full cursor-pointer rounded-md border px-3 py-2 text-sm outline-none transition-colors disabled:opacity-50"
              style={inputStyle(parameter)}
            >
              {parameter.options.map((option) => (
                <option key={option} value={option}>
                  {option}
                </option>
              ))}
            </select>
          )}

          {missing[parameter.name] && (
            <p className="text-xs" style={{ color: 'var(--pcs-danger)' }}>
              Este campo es obligatorio.
            </p>
          )}

          {!missing[parameter.name] && parameter.helpText && (
            <p className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
              {parameter.helpText}
            </p>
          )}
        </div>
      ))}

      <button
        type="submit"
        disabled={running}
        className="flex items-center gap-2 rounded-md px-3 py-2 text-sm font-medium transition-colors disabled:opacity-50"
        style={{ background: 'var(--pcs-accent)', color: '#fff' }}
      >
        {running ? 'Ejecutando…' : 'Ejecutar'}
      </button>
    </form>
  );
}
