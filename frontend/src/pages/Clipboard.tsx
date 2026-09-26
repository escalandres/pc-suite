import { useEffect, useMemo, useState } from 'react';
import { AlertTriangle, ClipboardPaste } from 'lucide-react';
import { bridge } from '../services/pcsuiteBridge';
import type { ToolCollectionResult, ToolInfo } from '../types/bridge';
import { usePreference, PREF_CLIPBOARD_DETECTION } from '../prefs';
import type { PageId } from '../layouts/AppLayout';

/**
 * Smart detection sobre el portapapeles (§20) con aviso de contenido sensible
 * (§21). El usuario pega con Ctrl+V (gesto manual → sin operación nativa ni
 * permisos): el análisis vive solo en el estado del componente, nada se
 * persiste ni se registra en Activity/toasts.
 */

interface Finding {
  message: string;
  /** Claves para emparejar contra el registro de tools. */
  keywords: string[];
  /** Presente si el contenido es sensible (§21). */
  sensitive?: string;
}

function detect(raw: string): Finding | null {
  const text = raw.trim();
  if (!text) return null;

  const pem = /^-----BEGIN ([A-Z0-9 ]+)-----/m.exec(text);
  if (pem) {
    const isPrivate = pem[1].includes('PRIVATE KEY');
    return {
      message: isPrivate ? 'Clave privada PEM detectada' : 'Datos PEM detectados',
      keywords: ['pem', 'private key'],
      sensitive: isPrivate ? 'una clave privada' : undefined,
    };
  }

  if (text.startsWith('eyJ') && /^[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]*$/.test(text)) {
    return {
      message: 'JWT detectado',
      keywords: ['jwt', 'token'],
      sensitive: 'un JWT (token de acceso)',
    };
  }

  if (/ssh-(rsa|ed25519|dss|ecdsa-[a-z0-9]+)\s+AAAA[A-Za-z0-9+/=]+/.test(text)) {
    return { message: 'Clave SSH pública detectada', keywords: ['ssh'] };
  }

  if (/\bAKIA[0-9A-Z]{16}\b/.test(text)) {
    return { message: 'Clave de acceso AWS detectada', keywords: [], sensitive: 'una clave de acceso AWS' };
  }

  if (/\b(?:ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9]{36,}\b|\bgithub_pat_[A-Za-z0-9_]{22,}\b/.test(text)) {
    return { message: 'Token de GitHub detectado', keywords: [], sensitive: 'un token de acceso' };
  }

  if (text.startsWith('{') || text.startsWith('[')) {
    try {
      JSON.parse(text);
      return { message: 'El portapapeles parece JSON', keywords: ['json'] };
    } catch {
      // parecía JSON pero no lo es
    }
  }

  return null;
}

function findTool(tools: ToolInfo[], keywords: string[]): ToolInfo | undefined {
  return tools.find((tool) => {
    const hay = [tool.name, tool.description, ...tool.keywords].join(' ').toLowerCase();
    return keywords.some((keyword) => hay.includes(keyword));
  });
}

interface ClipboardProps {
  onNavigate: (page: PageId, toolId?: string) => void;
}

export function Clipboard({ onNavigate }: ClipboardProps) {
  const [text, setText] = useState('');
  const detectionOn = usePreference(PREF_CLIPBOARD_DETECTION, true);
  const [tools, setTools] = useState<ToolInfo[]>([]);

  useEffect(() => {
    bridge
      .call<ToolCollectionResult>('tools.list')
      .then((result) => setTools(result.tools))
      .catch(() => setTools([]));
  }, []);

  const finding = useMemo(() => (detectionOn ? detect(text) : null), [text, detectionOn]);
  const match = useMemo(
    () => (finding && finding.keywords.length > 0 ? findTool(tools, finding.keywords) : undefined),
    [finding, tools],
  );

  return (
    <div className="max-w-3xl">
      <h1 className="mb-1 flex items-center gap-2 text-xl font-semibold">
        <ClipboardPaste size={20} aria-hidden /> Portapapeles
      </h1>
      <p className="mb-4 text-sm" style={{ color: 'var(--pcs-muted)' }}>
        Pega contenido con Ctrl+V y PCSuite lo analiza al instante para sugerirte la herramienta
        adecuada. Nada de lo que pegues se guarda en disco ni sale de este equipo.
      </p>

      <textarea
        value={text}
        onChange={(event) => setText(event.target.value)}
        rows={10}
        spellCheck={false}
        aria-label="Contenido del portapapeles a analizar"
        placeholder="Pega aquí el contenido a analizar…"
        className="w-full rounded-lg border p-3 font-mono text-sm"
        style={{
          background: 'var(--pcs-surface)',
          borderColor: 'var(--pcs-border)',
          color: 'var(--pcs-text)',
        }}
      />

      {!detectionOn && (
        <p className="mt-3 text-sm" style={{ color: 'var(--pcs-muted)' }}>
          La detección automática está desactivada (Ajustes → Detección de clipboard).
        </p>
      )}

      {finding && finding.sensitive && (
        <div
          className="mt-3 rounded-lg border p-3 text-sm"
          style={{ background: 'var(--pcs-surface)', borderColor: '#d29922', borderLeft: '3px solid #d29922' }}
        >
          <p className="flex items-center gap-2 font-semibold">
            <AlertTriangle size={16} style={{ color: '#d29922' }} aria-hidden />
            Contenido sensible detectado: {finding.sensitive}.
          </p>
          <p className="mt-1" style={{ color: 'var(--pcs-muted)' }}>
            {finding.message}. PCSuite no lo guarda: solo vive en memoria mientras está en este
            editor (nada a disco ni a logs).
          </p>
          <button
            type="button"
            className="mt-2 rounded-md border px-3 py-1.5 text-sm"
            style={{ borderColor: 'var(--pcs-border)' }}
            onClick={() => setText('')}
          >
            No guardar
          </button>
        </div>
      )}

      {finding && !finding.sensitive && (
        <div
          className="mt-3 flex flex-wrap items-center gap-3 rounded-lg border p-3 text-sm"
          style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
        >
          <span className="font-medium">{finding.message}</span>
          {match ? (
            <button
              type="button"
              className="rounded-md px-3 py-1.5 text-sm font-medium"
              style={{ background: 'var(--pcs-accent)', color: '#fff' }}
              onClick={() => onNavigate('tools', match.id)}
            >
              Usar en {match.name}
            </button>
          ) : (
            <button
              type="button"
              className="rounded-md border px-3 py-1.5 text-sm"
              style={{ borderColor: 'var(--pcs-border)' }}
              onClick={() => onNavigate('tools')}
            >
              Ver herramientas
            </button>
          )}
        </div>
      )}
    </div>
  );
}
