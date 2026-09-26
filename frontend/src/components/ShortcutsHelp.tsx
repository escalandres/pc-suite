import { useEffect } from 'react';
import { X } from 'lucide-react';
import { closeHelp, useHelpOpen, useShortcutList } from '../shortcuts';

/**
 * Overlay de ayuda con la lista de atajos registrados (Task 010 parte 10.5).
 * Se abre con «?» (o desde la paleta / el pie de la sidebar) y se cierra
 * con Esc, clic fuera o la X.
 */
export function ShortcutsHelp() {
  const open = useHelpOpen();
  const shortcuts = useShortcutList();

  useEffect(() => {
    if (!open) return;
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        closeHelp();
      }
    };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [open]);

  if (!open) return null;

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center"
      style={{ background: 'rgba(0, 0, 0, 0.55)' }}
      onClick={closeHelp}
      role="presentation"
    >
      <div
        className="w-full max-w-md rounded-lg border p-5 shadow-xl"
        style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
        onClick={(e) => e.stopPropagation()}
        role="dialog"
        aria-modal="true"
        aria-label="Atajos de teclado"
      >
        <div className="flex items-center justify-between">
          <h2 className="text-base font-semibold">Atajos de teclado</h2>
          <button
            type="button"
            aria-label="Cerrar ayuda"
            onClick={closeHelp}
            className="rounded p-1"
            style={{ color: 'var(--pcs-text-muted)', cursor: 'pointer' }}
          >
            <X size={16} aria-hidden />
          </button>
        </div>

        {shortcuts.length === 0 ? (
          <p className="mt-4 text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
            No hay atajos registrados.
          </p>
        ) : (
          <ul className="mt-4 space-y-2">
            {shortcuts.map((shortcut) => (
              <li key={shortcut.id} className="flex items-center justify-between gap-4 text-sm">
                <span>{shortcut.description}</span>
                <kbd
                  className="shrink-0 rounded px-1.5 py-0.5 text-xs"
                  style={{
                    border: '1px solid var(--pcs-border)',
                    background: 'var(--pcs-bg)',
                    color: 'var(--pcs-text)',
                  }}
                >
                  {shortcut.keys}
                </kbd>
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  );
}
