import { Sparkles } from 'lucide-react';
import { usePreference, setPreference, PREF_FIRST_RUN } from '../prefs';

/**
 * Bienvenida en el primer arranque (10.8): overlay modal que solo se cierra
 * con «Empezar», que fija la preferencia first-run. Sin sección de blueprint —
 * es la única pieza autodefinida de la Task 010.
 */
export function Welcome() {
  const seen = usePreference(PREF_FIRST_RUN, false);
  if (seen) return null;

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center p-6"
      style={{ background: 'rgba(0, 0, 0, 0.6)' }}
      role="dialog"
      aria-modal="true"
      aria-labelledby="pcsuite-welcome-title"
    >
      <div
        className="w-full max-w-lg rounded-xl border p-6"
        style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
      >
        <h2
          id="pcsuite-welcome-title"
          className="mb-1 flex items-center gap-2 text-xl font-semibold"
        >
          <Sparkles size={20} aria-hidden /> Bienvenido a PCSuite
        </h2>
        <p className="mb-4 text-sm" style={{ color: 'var(--pcs-muted)' }}>
          Tu suite de utilidades de Windows en una sola app: herramientas de desarrollador,
          paquetes, WSL, ClamAV y más.
        </p>

        <ul className="mb-5 space-y-2 text-sm">
          <li>
            <strong>Seguro por diseño:</strong> la interfaz envía solo datos estructurados y la
            app nativa construye los comandos — nunca se ejecuta nada arbitrario.
          </li>
          <li>
            <strong>Ctrl + K</strong> abre la búsqueda global; <strong>?</strong> muestra todos
            los atajos de teclado.
          </li>
          <li>
            <strong>Estrellas</strong> en el menú fijan tus páginas y herramientas favoritas, y
            en Ajustes cambias tema, notificaciones y detección de portapapeles.
          </li>
        </ul>

        <div className="flex justify-end">
          <button
            type="button"
            className="rounded-md px-4 py-2 text-sm font-medium"
            style={{ background: 'var(--pcs-accent)', color: '#fff' }}
            onClick={() => setPreference(PREF_FIRST_RUN, true)}
          >
            Empezar
          </button>
        </div>
      </div>
    </div>
  );
}
