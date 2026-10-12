import { CheckCircle2, X, XCircle, RotateCcw } from 'lucide-react';
import { dismissToast, useToasts } from '../activity';

/**
 * Pila global de notificaciones (§36): esquina inferior derecha, se cierra
 * sola a los 5 s o con la X. Sin toasts = no renderiza nada.
 * 14.3.3 — Soporta acciones (retry, undo) en toasts.
 */
export function Toasts() {
  const toasts = useToasts();
  if (toasts.length === 0) return null;

  return (
    <div className="fixed right-4 bottom-4 z-50 flex w-96 max-w-[calc(100vw-2rem)] flex-col gap-2">
      {toasts.map((toast) => {
        const color = toast.kind === 'success' ? '#3fb950' : '#f85149';
        const Icon = toast.kind === 'success' ? CheckCircle2 : XCircle;
        return (
          <div
            key={toast.id}
            role="status"
            aria-live="polite"
            className="flex items-start gap-2 rounded-lg border px-3 py-2 text-sm shadow-lg"
            style={{
              background: 'var(--pcs-surface)',
              borderColor: 'var(--pcs-border)',
              borderLeft: `3px solid ${color}`,
            }}
          >
            <Icon size={16} style={{ color, marginTop: 2 }} aria-hidden />
            <span className="min-w-0 flex-1 break-words">{toast.message}</span>
            {toast.actions && toast.actions.length > 0 && (
              <div className="flex items-center gap-1.5 ml-2">
                {toast.actions.map((action, index) => (
                  <button
                    key={index}
                    type="button"
                    onClick={() => {
                      action.onClick();
                      dismissToast(toast.id);
                    }}
                    className="rounded px-2 py-1 text-xs font-medium transition-colors"
                    style={
                      action.variant === 'primary'
                        ? {
                            background: 'var(--pcs-accent)',
                            color: '#fff',
                          }
                        : {
                            background: 'var(--pcs-surface-2)',
                            color: 'var(--pcs-text)',
                            border: '1px solid var(--pcs-border)',
                          }
                    }
                  >
                    {action.variant === 'primary' && <RotateCcw size={10} aria-hidden className="mr-1" />}
                    {action.label}
                  </button>
                ))}
              </div>
            )}
            <button
              type="button"
              aria-label="Cerrar notificación"
              className="rounded p-0.5 opacity-70 hover:opacity-100 ml-2"
              onClick={() => dismissToast(toast.id)}
            >
              <X size={14} aria-hidden />
            </button>
          </div>
        );
      })}
    </div>
  );
}
