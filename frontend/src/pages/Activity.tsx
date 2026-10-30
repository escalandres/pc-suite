import { CheckCircle2, Loader2, XCircle } from 'lucide-react';
import { useActivity } from '../activity';
import type { ActivityEntry } from '../activity';
import { useTranslation } from '../hooks/useTranslation';

function formatTime(entry: ActivityEntry): string {
  return new Date(entry.startedAt).toLocaleTimeString();
}

function formatDuration(entry: ActivityEntry): string {
  if (!entry.finishedAt) return '';
  const seconds = (entry.finishedAt - entry.startedAt) / 1000;
  return `${seconds.toFixed(1)} s`;
}

function EntryRow({ entry }: { entry: ActivityEntry }) {
  const icon =
    entry.status === 'running' ? (
      <Loader2 size={16} className="animate-spin" style={{ color: '#58a6ff' }} aria-hidden />
    ) : entry.status === 'ok' ? (
      <CheckCircle2 size={16} style={{ color: '#3fb950' }} aria-hidden />
    ) : (
      <XCircle size={16} style={{ color: '#f85149' }} aria-hidden />
    );

  return (
    <li className="flex items-start gap-3 rounded-lg border px-3 py-2"
      style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
    >
      <span className="mt-0.5">{icon}</span>
      <span className="min-w-0 flex-1">
        <span className="block break-all font-mono text-sm">{entry.operation}</span>
        {entry.errorMessage && (
          <span className="mt-0.5 block text-xs" style={{ color: '#f85149' }}>
            {entry.errorMessage}
          </span>
        )}
      </span>
      <span className="shrink-0 text-right text-xs" style={{ color: 'var(--pcs-muted)' }}>
        {formatTime(entry)}
        {formatDuration(entry) && <span className="block">{formatDuration(entry)}</span>}
      </span>
    </li>
  );
}

function Section({ title, entries }: { title: string; entries: ActivityEntry[] }) {
  if (entries.length === 0) return null;
  return (
    <section className="mb-6">
      <h2 className="mb-2 text-sm font-semibold" style={{ color: 'var(--pcs-muted)' }}>
        {title}
      </h2>
      <ul className="flex flex-col gap-2">
        {entries.map((entry) => (
          <EntryRow key={entry.id} entry={entry} />
        ))}
      </ul>
    </section>
  );
}

/**
 * Activity center (§37): operaciones en curso y completadas con su estado y
 * detalle de error (mensaje estructurado del puente, nunca stack traces).
 */
export function ActivityPage() {
  const { t } = useTranslation();
  const entries = useActivity();
  const running = entries.filter((entry) => entry.status === 'running');
  const completed = entries.filter((entry) => entry.status !== 'running');

  return (
    <div>
      <h1 className="mb-4 text-xl font-semibold">{t('sidebar.activity')}</h1>
      {entries.length === 0 ? (
        <p className="text-sm" style={{ color: 'var(--pcs-muted)' }}>
          {t('pages.activity.none')}
        </p>
      ) : (
        <>
          <Section title={t('pages.activity.running')} entries={running} />
          <Section title={t('pages.activity.completed')} entries={completed} />
        </>
      )}
    </div>
  );
}
