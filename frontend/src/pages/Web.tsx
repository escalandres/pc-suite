import { useState } from 'react';
import { ShieldAlert, ShieldCheck } from 'lucide-react';
import { useTranslation } from '../hooks/useTranslation';

function scriptOf(ch: string): string {
  const code = ch.codePointAt(0)!;
  // Latin
  if ((code >= 0x0041 && code <= 0x005A) || (code >= 0x0061 && code <= 0x007A) ||
      (code >= 0x00C0 && code <= 0x024F) || (code >= 0x1E00 && code <= 0x1EFF)) return 'Latin';
  // Cyrillic
  if (code >= 0x0400 && code <= 0x04FF) return 'Cyrillic';
  // Greek
  if (code >= 0x0370 && code <= 0x03FF) return 'Greek';
  // Armenian
  if (code >= 0x0530 && code <= 0x058F) return 'Armenian';
  // Hebrew
  if (code >= 0x0590 && code <= 0x05FF) return 'Hebrew';
  // Arabic
  if (code >= 0x0600 && code <= 0x06FF) return 'Arabic';
  // Hangul
  if (code >= 0xAC00 && code <= 0xD7AF) return 'Hangul';
  // CJK
  if (code >= 0x4E00 && code <= 0x9FFF) return 'CJK';
  // Digits / punctuation / symbols
  if (code >= 0x0030 && code <= 0x0039) return 'Digit';
  if (code <= 0x007F) return 'ASCII';
  return 'Other';
}

export function Web() {
  const { t } = useTranslation();
  const [url, setUrl] = useState('');
  const [result, setResult] = useState<{
    host: string;
    scripts: string[];
    suspicious: { char: string; script: string }[];
    punycode: boolean;
    verdict: 'safe' | 'suspicious' | 'invalid';
  } | null>(null);

  const check = () => {
    let parsed: URL;
    try {
      parsed = new URL(url.trim());
    } catch {
      setResult({ host: '', scripts: [], suspicious: [], punycode: false, verdict: 'invalid' });
      return;
    }
    const host = parsed.hostname;
    const punycode = host.startsWith('xn--') || host.includes('.xn--');

    const chars = [...host];
    const scriptCounts: Record<string, number> = {};
    for (const ch of chars) {
      const sc = scriptOf(ch);
      scriptCounts[sc] = (scriptCounts[sc] || 0) + 1;
    }
    const scripts = Object.keys(scriptCounts);
    // Dominant script (excluding ASCII/digit/punctuation)
    const meaningful = scripts.filter((s) => s !== 'ASCII' && s !== 'Digit');
    const dominant = meaningful.length > 0
      ? meaningful.reduce((a, b) => (scriptCounts[a] >= scriptCounts[b] ? a : b))
      : 'ASCII';

    const suspicious = chars
      .map((ch) => ({ char: ch, script: scriptOf(ch) }))
      .filter((c) => c.script !== 'ASCII' && c.script !== 'Digit' && c.script !== dominant);

    const mixedScripts = meaningful.length > 1;
    const verdict = mixedScripts || punycode ? 'suspicious' : 'safe';

    setResult({ host, scripts, suspicious, punycode, verdict });
  };

  return (
    <div className="space-y-6">
      <header>
        <h1 className="text-2xl font-semibold">{t('sidebar.web')}</h1>
        <p className="mt-1 text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
          {t('pages.web.subtitle')}
        </p>
      </header>

      <div className="space-y-4 rounded-lg border p-4" style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}>
        <div className="space-y-2">
          <label className="text-sm font-medium">{t('pages.web.urlLabel')}</label>
          <div className="flex gap-2">
            <input
              type="text"
              value={url}
              onChange={(e) => setUrl(e.target.value)}
              placeholder="https://example.com"
              className="flex-1 rounded-md border px-3 py-2 text-sm"
              style={{ background: 'var(--pcs-bg)', color: 'var(--pcs-text)', borderColor: 'var(--pcs-border)' }}
            />
            <button type="button" onClick={check} className="rounded-md px-4 py-2 text-sm font-medium cursor-pointer" style={{ background: 'var(--pcs-accent)', color: '#fff' }}>
              {t('pages.web.check')}
            </button>
          </div>
        </div>

        {result && (
          <div className="space-y-3">
            {result.verdict === 'invalid' && (
              <p className="text-sm" style={{ color: 'var(--pcs-danger)' }}>{t('pages.web.invalidUrl')}</p>
            )}
            {result.verdict !== 'invalid' && (
              <>
                <div className="flex items-center gap-2">
                  {result.verdict === 'safe' ? (
                    <ShieldCheck size={20} style={{ color: 'var(--pcs-success)' }} />
                  ) : (
                    <ShieldAlert size={20} style={{ color: 'var(--pcs-danger)' }} />
                  )}
                  <span className="font-medium" style={{ color: result.verdict === 'safe' ? 'var(--pcs-success)' : 'var(--pcs-danger)' }}>
                    {result.verdict === 'safe' ? t('pages.web.safe') : t('pages.web.suspicious')}
                  </span>
                </div>

                <dl className="grid grid-cols-1 gap-2 text-sm">
                  <div className="flex justify-between gap-4">
                    <dt style={{ color: 'var(--pcs-text-muted)' }}>{t('pages.web.host')}</dt>
                    <dd className="text-right font-mono break-all">{result.host}</dd>
                  </div>
                  <div className="flex justify-between gap-4">
                    <dt style={{ color: 'var(--pcs-text-muted)' }}>{t('pages.web.scripts')}</dt>
                    <dd className="text-right">{result.scripts.join(', ')}</dd>
                  </div>
                  {result.punycode && (
                    <div className="flex justify-between gap-4">
                      <dt style={{ color: 'var(--pcs-text-muted)' }}>{t('pages.web.punycode')}</dt>
                      <dd className="text-right" style={{ color: 'var(--pcs-warning)' }}>{t('pages.web.punycodeYes')}</dd>
                    </div>
                  )}
                </dl>

                {result.suspicious.length > 0 && (
                  <div className="space-y-1">
                    <p className="text-sm font-medium">{t('pages.web.suspiciousChars')}</p>
                    <div className="flex flex-wrap gap-1">
                      {result.suspicious.map((c, i) => (
                        <span key={i} className="rounded px-2 py-0.5 text-xs font-mono" style={{ background: 'color-mix(in srgb, var(--pcs-danger) 15%, transparent)', color: 'var(--pcs-danger)' }}>
                          {c.char} ({c.script})
                        </span>
                      ))}
                    </div>
                  </div>
                )}

                {result.verdict === 'suspicious' && (
                  <p className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
                    {t('pages.web.explanation')}
                  </p>
                )}
              </>
            )}
          </div>
        )}
      </div>
    </div>
  );
}
