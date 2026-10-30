import { Sparkles } from 'lucide-react';
import { usePreference, setPreference, PREF_FIRST_RUN } from '../prefs';
import { t } from '../lib/i18n';

/**
 * Bienvenida en el primer arranque (10.8): overlay modal que solo se cierra
 * con «{t('pages.welcome.getStarted')}», que fija la preferencia first-run. Sin sección de blueprint —
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
          <Sparkles size={20} aria-hidden /> {t('pages.welcome.title')}
        </h2>
        <p className="mb-4 text-sm" style={{ color: 'var(--pcs-muted)' }}>
          {t('pages.welcome.subtitle')}
        </p>

        <ul className="mb-5 space-y-2 text-sm">
          <li>
            <strong>{t('pages.welcome.secureByDesign')}:</strong>{' '}
            {t('pages.welcome.secureByDesignNote')}
          </li>
          <li>
            <strong>Ctrl + K</strong> {t('pages.welcome.shortcuts')} <strong>?</strong>{' '}
            {t('pages.welcome.shortcutsNote')}
          </li>
          <li>
            <strong>{t('pages.welcome.favorites')}:</strong>{' '}
            {t('pages.welcome.favoritesNote')}
          </li>
        </ul>

        <div className="flex justify-end">
          <button
            type="button"
            className="rounded-md px-4 py-2 text-sm font-medium"
            style={{ background: 'var(--pcs-accent)', color: '#fff' }}
            onClick={() => setPreference(PREF_FIRST_RUN, true)}
          >
            {t('pages.welcome.getStarted')}
          </button>
        </div>
      </div>
    </div>
  );
}
