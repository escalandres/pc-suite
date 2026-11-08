import { useState } from 'react';
import { Globe } from 'lucide-react';
import { bridge, BridgeError } from '../services/pcsuiteBridge';
import { OperationIds } from '../types/bridge';
import type {
  WebUrlInspectionResult,
  WebUrlCleanResult,
  WebUrlCompareResult,
  WebTlsInspectionResult,
  WebHttpHeadersInspectionResult,
  WebUrlExpansionResult,
  WebDnsInspectionResult,
  WebEmailInspectionResult,
} from '../types/bridge';

type Tab = 'url' | 'tls' | 'headers' | 'dns' | 'email' | 'utils';

const TAB_SUBTITLES: Record<Tab, string> = {
  url: 'Analiza un dominio en busca de homógrafos, punycode y typosquatting (suplantación de marcas).',
  tls: 'Inspecciona el certificado TLS de un host: emisor, validez, SAN y expiración.',
  headers: 'Comprueba las cabeceras de seguridad HTTP de una URL (HSTS, CSP, X-Frame-Options, etc.).',
  dns: 'Consulta registros DNS (A, AAAA, MX, TXT, CNAME, NS) de un host.',
  email: 'Valida un correo: formato, MX, SPF, DMARC y DKIM.',
  utils: 'Limpia parámetros de tracking, compara dos URLs y expande enlaces cortos.',
};

const TABS: { id: Tab; label: string }[] = [
  { id: 'url', label: 'URL' },
  { id: 'tls', label: 'TLS' },
  { id: 'headers', label: 'Cabeceras' },
  { id: 'dns', label: 'DNS' },
  { id: 'email', label: 'Correo' },
  { id: 'utils', label: 'Utilidades' },
];

function errMessage(e: unknown, fallback: string): string {
  return e instanceof BridgeError ? e.message : fallback;
}

export function Web() {
  const [tab, setTab] = useState<Tab>('url');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // URL tab
  const [urlInput, setUrlInput] = useState('');
  const [urlResult, setUrlResult] = useState<WebUrlInspectionResult | null>(null);
  // TLS
  const [tlsHost, setTlsHost] = useState('');
  const [tlsResult, setTlsResult] = useState<WebTlsInspectionResult | null>(null);
  // Headers
  const [headersUrl, setHeadersUrl] = useState('');
  const [headersResult, setHeadersResult] = useState<WebHttpHeadersInspectionResult | null>(null);
  // DNS
  const [dnsHost, setDnsHost] = useState('');
  const [dnsType, setDnsType] = useState('A');
  const [dnsResult, setDnsResult] = useState<WebDnsInspectionResult | null>(null);
  // Email
  const [emailInput, setEmailInput] = useState('');
  const [emailResult, setEmailResult] = useState<WebEmailInspectionResult | null>(null);
  // Utils
  const [cleanInput, setCleanInput] = useState('');
  const [cleanResult, setCleanResult] = useState<WebUrlCleanResult | null>(null);
  const [cmpA, setCmpA] = useState('');
  const [cmpB, setCmpB] = useState('');
  const [cmpResult, setCmpResult] = useState<WebUrlCompareResult | null>(null);
  const [expInput, setExpInput] = useState('');
  const [expResult, setExpResult] = useState<WebUrlExpansionResult | null>(null);

  const run = async <T,>(fn: () => Promise<T>, onOk: (v: T) => void) => {
    setBusy(true);
    setError(null);
    try {
      const v = await fn();
      onOk(v);
    } catch (e) {
      setError(errMessage(e, 'Error'));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-2">
        <Globe size={18} aria-hidden />
        <h1 className="text-lg font-semibold">Inspector web</h1>
      </div>

      <div className="flex flex-wrap gap-1">
        {TABS.map((t) => (
          <button
            key={t.id}
            type="button"
            onClick={() => setTab(t.id)}
            className="rounded-md border px-3 py-1 text-xs font-medium"
            style={{
              borderColor: 'var(--pcs-border)',
              background: tab === t.id ? 'var(--pcs-accent)' : 'var(--pcs-surface)',
              color: tab === t.id ? '#fff' : 'var(--pcs-text)',
            }}
          >
            {t.label}
          </button>
        ))}
      </div>

      <p className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
        {TAB_SUBTITLES[tab]}
      </p>

      {error && (
        <div className="rounded-md border px-3 py-2 text-xs" style={{ borderColor: 'var(--pcs-danger)', color: 'var(--pcs-danger)' }}>
          {error}
        </div>
      )}

      {tab === 'url' && (
        <section className="space-y-3 rounded-lg border p-4" style={{ borderColor: 'var(--pcs-border)' }}>
          <div className="flex gap-2">
            <input
              className="flex-1 rounded-md border px-2 py-1 text-sm"
              style={{ borderColor: 'var(--pcs-border)', background: 'var(--pcs-surface)', color: 'var(--pcs-text)' }}
              placeholder="https://ejemplo.com"
              value={urlInput}
              onChange={(e) => setUrlInput(e.target.value)}
            />
            <button
              type="button"
              disabled={busy || !urlInput.trim()}
              onClick={() => run(
                () => bridge.call<WebUrlInspectionResult>(OperationIds.webInspectUrl, { url: urlInput.trim() }),
                setUrlResult,
              )}
              className="rounded-md px-3 py-1 text-xs font-medium text-white"
              style={{ background: 'var(--pcs-accent)' }}
            >
              Inspeccionar
            </button>
          </div>
          {urlResult && (
            <div className="space-y-2 text-sm">
              <div><strong>Host:</strong> {urlResult.host}</div>
              <div><strong>Veredicto:</strong> {urlResult.verdict}</div>
              <div><strong>Punycode:</strong> {String(urlResult.punycode)}</div>
              <div><strong>Scripts mixtos:</strong> {String(urlResult.mixedScripts)}</div>
              {urlResult.suspicious.length > 0 && (
                <div><strong>Caracteres sospechosos:</strong> {urlResult.suspicious.map((s) => `${s.char}(${s.script})`).join(', ')}</div>
              )}
              {urlResult.typosquatCandidates.length > 0 && (
                <div><strong>Typosquatting:</strong> {urlResult.typosquatCandidates.join(', ')}</div>
              )}
            </div>
          )}
        </section>
      )}

      {tab === 'tls' && (
        <section className="space-y-3 rounded-lg border p-4" style={{ borderColor: 'var(--pcs-border)' }}>
          <div className="flex gap-2">
            <input
              className="flex-1 rounded-md border px-2 py-1 text-sm"
              style={{ borderColor: 'var(--pcs-border)', background: 'var(--pcs-surface)', color: 'var(--pcs-text)' }}
              placeholder="example.com"
              value={tlsHost}
              onChange={(e) => setTlsHost(e.target.value)}
            />
            <button
              type="button"
              disabled={busy || !tlsHost.trim()}
              onClick={() => run(
                () => bridge.call<WebTlsInspectionResult>(OperationIds.webCheckTls, { host: tlsHost.trim() }),
                setTlsResult,
              )}
              className="rounded-md px-3 py-1 text-xs font-medium text-white"
              style={{ background: 'var(--pcs-accent)' }}
            >
              Comprobar TLS
            </button>
          </div>
          {tlsResult && (
            <div className="space-y-1 text-sm">
              <div><strong>Veredicto:</strong> {tlsResult.verdict}</div>
              <div><strong>Emisor:</strong> {tlsResult.issuer ?? '—'}</div>
              <div><strong>Sujeto:</strong> {tlsResult.subject ?? '—'}</div>
              <div><strong>Válido desde:</strong> {tlsResult.validFrom ?? '—'}</div>
              <div><strong>Válido hasta:</strong> {tlsResult.validTo ?? '—'}</div>
              <div><strong>Expirado:</strong> {String(tlsResult.expired)}</div>
              <div><strong>SAN:</strong> {tlsResult.subjectAlternativeNames.join(', ') || '—'}</div>
              {tlsResult.detail && <div><strong>Detalle:</strong> {tlsResult.detail}</div>}
            </div>
          )}
        </section>
      )}

      {tab === 'headers' && (
        <section className="space-y-3 rounded-lg border p-4" style={{ borderColor: 'var(--pcs-border)' }}>
          <div className="flex gap-2">
            <input
              className="flex-1 rounded-md border px-2 py-1 text-sm"
              style={{ borderColor: 'var(--pcs-border)', background: 'var(--pcs-surface)', color: 'var(--pcs-text)' }}
              placeholder="https://ejemplo.com"
              value={headersUrl}
              onChange={(e) => setHeadersUrl(e.target.value)}
            />
            <button
              type="button"
              disabled={busy || !headersUrl.trim()}
              onClick={() => run(
                () => bridge.call<WebHttpHeadersInspectionResult>(OperationIds.webCheckHeaders, { url: headersUrl.trim() }),
                setHeadersResult,
              )}
              className="rounded-md px-3 py-1 text-xs font-medium text-white"
              style={{ background: 'var(--pcs-accent)' }}
            >
              Comprobar cabeceras
            </button>
          </div>
          {headersResult && (
            <div className="space-y-1 text-sm">
              <div><strong>Estado:</strong> {headersResult.statusCode} · {headersResult.verdict}</div>
              <div><strong>HSTS:</strong> {headersResult.hsts ?? '—'}</div>
              <div><strong>CSP:</strong> {headersResult.contentSecurityPolicy ?? '—'}</div>
              <div><strong>X-Frame-Options:</strong> {headersResult.xFrameOptions ?? '—'}</div>
              <div><strong>X-Content-Type-Options:</strong> {headersResult.xContentTypeOptions ?? '—'}</div>
              {headersResult.cookies.length > 0 && <div><strong>Cookies:</strong> {headersResult.cookies.join('; ')}</div>}
              {headersResult.detail && <div><strong>Detalle:</strong> {headersResult.detail}</div>}
            </div>
          )}
        </section>
      )}

      {tab === 'dns' && (
        <section className="space-y-3 rounded-lg border p-4" style={{ borderColor: 'var(--pcs-border)' }}>
          <div className="flex gap-2">
            <input
              className="flex-1 rounded-md border px-2 py-1 text-sm"
              style={{ borderColor: 'var(--pcs-border)', background: 'var(--pcs-surface)', color: 'var(--pcs-text)' }}
              placeholder="ejemplo.com"
              value={dnsHost}
              onChange={(e) => setDnsHost(e.target.value)}
            />
            <select
              value={dnsType}
              onChange={(e) => setDnsType(e.target.value)}
              className="rounded-md border px-2 py-1 text-xs"
              style={{ borderColor: 'var(--pcs-border)', background: 'var(--pcs-surface)', color: 'var(--pcs-text)' }}
            >
              {['A', 'AAAA', 'MX', 'TXT', 'CNAME', 'NS'].map((t) => (
                <option key={t} value={t}>{t}</option>
              ))}
            </select>
            <button
              type="button"
              disabled={busy || !dnsHost.trim()}
              onClick={() => run(
                () => bridge.call<WebDnsInspectionResult>(OperationIds.webResolveDns, { host: dnsHost.trim(), types: [dnsType] }),
                setDnsResult,
              )}
              className="rounded-md px-3 py-1 text-xs font-medium text-white"
              style={{ background: 'var(--pcs-accent)' }}
            >
              Consultar
            </button>
          </div>
          {dnsResult && (
            <div className="space-y-1 text-sm">
              {dnsResult.records.length === 0 && <div>Sin registros.</div>}
              {dnsResult.records.map((r, i) => (
                <div key={i} className="font-mono text-xs">{r.type} {r.value} (ttl {r.ttl})</div>
              ))}
              {dnsResult.detail && <div><strong>Detalle:</strong> {dnsResult.detail}</div>}
            </div>
          )}
        </section>
      )}

      {tab === 'email' && (
        <section className="space-y-3 rounded-lg border p-4" style={{ borderColor: 'var(--pcs-border)' }}>
          <div className="flex gap-2">
            <input
              className="flex-1 rounded-md border px-2 py-1 text-sm"
              style={{ borderColor: 'var(--pcs-border)', background: 'var(--pcs-surface)', color: 'var(--pcs-text)' }}
              placeholder="usuario@ejemplo.com"
              value={emailInput}
              onChange={(e) => setEmailInput(e.target.value)}
            />
            <button
              type="button"
              disabled={busy || !emailInput.trim()}
              onClick={() => run(
                () => bridge.call<WebEmailInspectionResult>(OperationIds.webCheckEmail, { email: emailInput.trim() }),
                setEmailResult,
              )}
              className="rounded-md px-3 py-1 text-xs font-medium text-white"
              style={{ background: 'var(--pcs-accent)' }}
            >
              Validar
            </button>
          </div>
          {emailResult && (
            <div className="space-y-1 text-sm">
              <div><strong>Veredicto:</strong> {emailResult.verdict}</div>
              <div><strong>Formato válido:</strong> {String(emailResult.formatValid)}</div>
              <div><strong>Dominio:</strong> {emailResult.domain ?? '—'}</div>
              <div><strong>MX:</strong> {emailResult.mxRecords.join(', ') || '—'}</div>
              <div><strong>SPF:</strong> {emailResult.spf ?? '—'}</div>
              <div><strong>DMARC:</strong> {emailResult.dmarc ?? '—'}</div>
              <div><strong>DKIM:</strong> {emailResult.dkimSelector ?? '—'}</div>
              {emailResult.detail && <div><strong>Detalle:</strong> {emailResult.detail}</div>}
            </div>
          )}
        </section>
      )}

      {tab === 'utils' && (
        <section className="space-y-4 rounded-lg border p-4" style={{ borderColor: 'var(--pcs-border)' }}>
          <div className="space-y-2">
            <div className="text-xs font-semibold">Limpiar tracking</div>
            <div className="flex gap-2">
              <input
                className="flex-1 rounded-md border px-2 py-1 text-sm"
                style={{ borderColor: 'var(--pcs-border)', background: 'var(--pcs-surface)', color: 'var(--pcs-text)' }}
                placeholder="https://ejemplo.com/?utm_source=x&fbclid=y"
                value={cleanInput}
                onChange={(e) => setCleanInput(e.target.value)}
              />
              <button
                type="button"
                disabled={busy || !cleanInput.trim()}
                onClick={() => run(
                  () => bridge.call<WebUrlCleanResult>(OperationIds.webCleanTracking, { url: cleanInput.trim() }),
                  setCleanResult,
                )}
                className="rounded-md px-3 py-1 text-xs font-medium text-white"
                style={{ background: 'var(--pcs-accent)' }}
              >
                Limpiar
              </button>
            </div>
            {cleanResult && <div className="text-xs font-mono break-all">{cleanResult.cleanedUrl}</div>}
          </div>

          <div className="space-y-2">
            <div className="text-xs font-semibold">Comparar URLs</div>
            <div className="flex gap-2">
              <input
                className="flex-1 rounded-md border px-2 py-1 text-sm"
                style={{ borderColor: 'var(--pcs-border)', background: 'var(--pcs-surface)', color: 'var(--pcs-text)' }}
                placeholder="URL A"
                value={cmpA}
                onChange={(e) => setCmpA(e.target.value)}
              />
              <input
                className="flex-1 rounded-md border px-2 py-1 text-sm"
                style={{ borderColor: 'var(--pcs-border)', background: 'var(--pcs-surface)', color: 'var(--pcs-text)' }}
                placeholder="URL B"
                value={cmpB}
                onChange={(e) => setCmpB(e.target.value)}
              />
              <button
                type="button"
                disabled={busy || !cmpA.trim() || !cmpB.trim()}
                onClick={() => run(
                  () => bridge.call<WebUrlCompareResult>(OperationIds.webCompareUrls, { urlA: cmpA.trim(), urlB: cmpB.trim() }),
                  setCmpResult,
                )}
                className="rounded-md px-3 py-1 text-xs font-medium text-white"
                style={{ background: 'var(--pcs-accent)' }}
              >
                Comparar
              </button>
            </div>
            {cmpResult && (
              <ul className="list-disc pl-5 text-xs">
                {cmpResult.differences.map((d, i) => <li key={i}>{d}</li>)}
              </ul>
            )}
          </div>

          <div className="space-y-2">
            <div className="text-xs font-semibold">Expandir URL (redirecciones)</div>
            <div className="flex gap-2">
              <input
                className="flex-1 rounded-md border px-2 py-1 text-sm"
                style={{ borderColor: 'var(--pcs-border)', background: 'var(--pcs-surface)', color: 'var(--pcs-text)' }}
                placeholder="https://bit.ly/..."
                value={expInput}
                onChange={(e) => setExpInput(e.target.value)}
              />
              <button
                type="button"
                disabled={busy || !expInput.trim()}
                onClick={() => run(
                  () => bridge.call<WebUrlExpansionResult>(OperationIds.webExpandUrl, { url: expInput.trim() }),
                  setExpResult,
                )}
                className="rounded-md px-3 py-1 text-xs font-medium text-white"
                style={{ background: 'var(--pcs-accent)' }}
              >
                Expandir
              </button>
            </div>
            {expResult && (
              <div className="text-xs space-y-1">
                <div><strong>Final:</strong> {expResult.finalUrl}</div>
                {expResult.chain.length > 1 && <div><strong>Cadena:</strong> {expResult.chain.join(' → ')}</div>}
                {expResult.detail && <div><strong>Detalle:</strong> {expResult.detail}</div>}
              </div>
            )}
          </div>
        </section>
      )}
    </div>
  );
}