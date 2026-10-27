import { useCallback, useEffect, useState } from 'react';
import {
  Code,
  Copy,
  Hash,
  KeyRound,
  Lock,
  Unlock,
} from 'lucide-react';
import { bridge } from '../services/pcsuiteBridge';
import { OperationIds } from '../types/bridge';

/**
 * Página "Cifrador" (Task 034): codificación de texto, hash y cifrado
 * de mensajes. Todo en proceso, sin red.
 */
export function Cipher() {
  const [tab, setTab] = useState<'encode' | 'hash' | 'encrypt'>('encode');

  return (
    <div className="space-y-6">
      <header>
        <h1 className="text-2xl font-semibold">Cifrador</h1>
        <p className="mt-1 text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
          Codificación, hash y cifrado de texto — todo local.
        </p>
      </header>

      <div className="flex gap-2">
        {(
          [
            { id: 'encode', label: 'Codificar', icon: Code },
            { id: 'hash', label: 'Hash', icon: Hash },
            { id: 'encrypt', label: 'Cifrar', icon: Lock },
          ] as const
        ).map(({ id, label, icon: Icon }) => (
          <button
            key={id}
            type="button"
            onClick={() => setTab(id)}
            className={`flex items-center gap-2 rounded-md px-4 py-2 text-sm font-medium transition-colors cursor-pointer ${
              tab === id ? '' : 'opacity-60 hover:opacity-90'
            }`}
            style={{
              background: tab === id ? 'var(--pcs-accent)' : 'var(--pcs-surface-2)',
              color: tab === id ? '#fff' : 'var(--pcs-text)',
            }}
          >
            <Icon size={14} aria-hidden />
            {label}
          </button>
        ))}
      </div>

      {tab === 'encode' && <EncodePanel />}
      {tab === 'hash' && <HashPanel />}
      {tab === 'encrypt' && <EncryptPanel />}
    </div>
  );
}

// --- Codificadores ---

const ALGORITHMS = [
  { value: 'base64', label: 'Base64' },
  { value: 'hex', label: 'Hex' },
  { value: 'url', label: 'URL' },
  { value: 'rot13', label: 'ROT13' },
  { value: 'caesar', label: 'César' },
  { value: 'binary', label: 'Binario' },
  { value: 'morse', label: 'Morse' },
] as const;

function EncodePanel() {
  const [algorithm, setAlgorithm] = useState('base64');
  const [input, setInput] = useState('');
  const [output, setOutput] = useState('');
  const [shift, setShift] = useState(3);
  const [error, setError] = useState<string | null>(null);

  const run = useCallback(async (direction: 'encode' | 'decode') => {
    if (!input.trim()) { setOutput(''); return; }
    setError(null);
    try {
      const result = await bridge.call<{ output: string }>(
        OperationIds.cipherEncode,
        {
          algorithm,
          input,
          shift: algorithm === 'caesar' ? shift : undefined,
          direction,
        },
      );
      setOutput(result.output);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'No se pudo procesar.');
      setOutput('');
    }
  }, [algorithm, input, shift]);

  const copyOutput = async () => {
    if (!output) return;
    try {
      await bridge.call(OperationIds.systemWriteClipboard, { text: output });
    } catch { /* best-effort */ }
  };

  return (
    <section
      className="space-y-4 rounded-lg border p-4"
      style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
    >
      <div className="flex flex-wrap gap-3">
        <select
          value={algorithm}
          onChange={(e) => setAlgorithm(e.target.value)}
          className="rounded-md border px-3 py-2 text-sm"
          style={{ background: 'var(--pcs-surface-2)', borderColor: 'var(--pcs-border)', color: 'var(--pcs-text)' }}
        >
          {ALGORITHMS.map(a => <option key={a.value} value={a.value}>{a.label}</option>)}
        </select>
        {algorithm === 'caesar' && (
          <input
            type="number"
            min={1}
            max={25}
            value={shift}
            onChange={(e) => setShift(Number(e.target.value) || 3)}
            className="w-20 rounded-md border px-3 py-2 text-sm"
            style={{ background: 'var(--pcs-surface-2)', borderColor: 'var(--pcs-border)', color: 'var(--pcs-text)' }}
            placeholder="Shift"
          />
        )}
      </div>

      <textarea
        value={input}
        onChange={(e) => setInput(e.target.value)}
        placeholder="Texto de entrada…"
        rows={4}
        className="w-full resize-y rounded-md border px-3 py-2 text-sm font-mono"
        style={{ background: 'var(--pcs-surface-2)', borderColor: 'var(--pcs-border)', color: 'var(--pcs-text)' }}
      />

      <div className="flex gap-2">
        <button
          type="button"
          onClick={() => void run('encode')}
          className="rounded-md px-4 py-2 text-sm font-medium cursor-pointer"
          style={{ background: 'var(--pcs-accent)', color: '#fff' }}
        >
          Codificar
        </button>
        <button
          type="button"
          onClick={() => void run('decode')}
          className="rounded-md px-4 py-2 text-sm font-medium cursor-pointer"
          style={{ background: 'var(--pcs-surface-2)', color: 'var(--pcs-text)', border: '1px solid var(--pcs-border)' }}
        >
          Decodificar
        </button>
        {output && (
          <button
            type="button"
            onClick={() => void copyOutput()}
            className="flex items-center gap-1.5 rounded-md px-3 py-2 text-sm cursor-pointer"
            style={{ background: 'var(--pcs-surface-2)', color: 'var(--pcs-text)' }}
          >
            <Copy size={12} aria-hidden /> Copiar
          </button>
        )}
      </div>

      {error && (
        <p className="text-xs" style={{ color: 'var(--pcs-danger)' }}>{error}</p>
      )}

      {output && (
        <textarea
          value={output}
          readOnly
          rows={4}
          className="w-full resize-y rounded-md border px-3 py-2 text-sm font-mono"
          style={{ background: 'var(--pcs-surface-2)', borderColor: 'var(--pcs-accent)', color: 'var(--pcs-text)' }}
        />
      )}
    </section>
  );
}

// --- Hash ---

function HashPanel() {
  const [input, setInput] = useState('');
  const [hashes, setHashes] = useState<{ sha256: string; sha1: string; md5: string; sha512: string } | null>(null);

  // Hash on change — instantáneo
  useEffect(() => {
    if (!input) { setHashes(null); return; }
    let cancelled = false;
    bridge
      .call<{ sha256: string; sha1: string; md5: string; sha512: string }>(
        OperationIds.cipherHash, { input }, 10_000)
      .then(r => { if (!cancelled) setHashes(r); })
      .catch(() => { if (!cancelled) setHashes(null); });
    return () => { cancelled = true; };
  }, [input]);

  const copyHash = async (value: string) => {
    try { await bridge.call(OperationIds.systemWriteClipboard, { text: value }); } catch { /* */ }
  };

  return (
    <section
      className="space-y-4 rounded-lg border p-4"
      style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
    >
      <textarea
        value={input}
        onChange={(e) => setInput(e.target.value)}
        placeholder="Escribe o pega el texto a hashear…"
        rows={4}
        className="w-full resize-y rounded-md border px-3 py-2 text-sm font-mono"
        style={{ background: 'var(--pcs-surface-2)', borderColor: 'var(--pcs-border)', color: 'var(--pcs-text)' }}
      />

      {hashes && (
        <div className="space-y-2">
          {(
            [
              ['SHA-256', hashes.sha256],
              ['SHA-512', hashes.sha512],
              ['SHA-1', hashes.sha1],
              ['MD5', hashes.md5],
            ] as const
          ).map(([label, value]) => (
            <div key={label} className="flex items-center gap-2 text-xs">
              <span className="w-16 shrink-0 font-medium" style={{ color: 'var(--pcs-text-muted)' }}>{label}</span>
              <code
                className="min-w-0 flex-1 break-all rounded-md border px-2 py-1 font-mono"
                style={{ borderColor: 'var(--pcs-border)', color: 'var(--pcs-text)' }}
              >
                {value}
              </code>
              <button
                type="button"
                onClick={() => void copyHash(value)}
                className="shrink-0 cursor-pointer rounded border px-1.5 py-0.5 text-xs"
                style={{ borderColor: 'var(--pcs-border)', color: 'var(--pcs-text-muted)' }}
              >
                Copiar
              </button>
            </div>
          ))}
        </div>
      )}
    </section>
  );
}

// --- Cifrado AES / XOR ---

function EncryptPanel() {
  const [mode, setMode] = useState<'aes' | 'xor'>('aes');
  const [direction, setDirection] = useState<'encrypt' | 'decrypt'>('encrypt');
  const [input, setInput] = useState('');
  const [password, setPassword] = useState('');
  const [output, setOutput] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const run = async () => {
    if (!input.trim() || !password || busy) return;
    setBusy(true);
    setError(null);
    setOutput('');
    try {
      let result: string;
      if (mode === 'aes') {
        if (direction === 'encrypt') {
          const r = await bridge.call<{ output: string }>(
            OperationIds.cipherEncryptAes, { input, password }, 30_000);
          result = r.output;
        } else {
          const r = await bridge.call<{ output: string }>(
            OperationIds.cipherDecryptAes, { input, password }, 30_000);
          result = r.output;
        }
      } else {
        const r = await bridge.call<{ output: string }>(
          OperationIds.cipherXor,
          { input, key: password, direction },
          30_000,
        );
        result = r.output;
      }
      setOutput(result);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'No se pudo procesar.');
    } finally {
      setBusy(false);
    }
  };

  const copyOutput = async () => {
    if (!output) return;
    try { await bridge.call(OperationIds.systemWriteClipboard, { text: output }); } catch { /* */ }
  };

  return (
    <section
      className="space-y-4 rounded-lg border p-4"
      style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
    >
      <div className="flex flex-wrap gap-2">
        <select
          value={mode}
          onChange={(e) => setMode(e.target.value as 'aes' | 'xor')}
          className="rounded-md border px-3 py-2 text-sm"
          style={{ background: 'var(--pcs-surface-2)', borderColor: 'var(--pcs-border)', color: 'var(--pcs-text)' }}
        >
          <option value="aes">AES-256-GCM</option>
          <option value="xor">XOR</option>
        </select>
        <button
          type="button"
          onClick={() => setDirection('encrypt')}
          className={`flex items-center gap-1.5 rounded-md px-3 py-2 text-xs font-medium cursor-pointer ${
            direction === 'encrypt' ? '' : 'opacity-50'
          }`}
          style={{ background: direction === 'encrypt' ? 'var(--pcs-accent)' : 'var(--pcs-surface-2)', color: direction === 'encrypt' ? '#fff' : 'var(--pcs-text)' }}
        >
          <Lock size={12} aria-hidden /> Cifrar
        </button>
        <button
          type="button"
          onClick={() => setDirection('decrypt')}
          className={`flex items-center gap-1.5 rounded-md px-3 py-2 text-xs font-medium cursor-pointer ${
            direction === 'decrypt' ? '' : 'opacity-50'
          }`}
          style={{ background: direction === 'decrypt' ? 'var(--pcs-accent)' : 'var(--pcs-surface-2)', color: direction === 'decrypt' ? '#fff' : 'var(--pcs-text)' }}
        >
          <Unlock size={12} aria-hidden /> Descifrar
        </button>
      </div>

      <textarea
        value={input}
        onChange={(e) => setInput(e.target.value)}
        placeholder={direction === 'encrypt' ? 'Texto a cifrar…' : 'Ciphertext base64…'}
        rows={4}
        className="w-full resize-y rounded-md border px-3 py-2 text-sm font-mono"
        style={{ background: 'var(--pcs-surface-2)', borderColor: 'var(--pcs-border)', color: 'var(--pcs-text)' }}
      />

      <input
        type="password"
        value={password}
        onChange={(e) => setPassword(e.target.value)}
        placeholder={mode === 'aes' ? 'Contraseña' : 'Clave XOR'}
        autoComplete="off"
        className="w-full rounded-md border px-3 py-2 text-sm"
        style={{ background: 'var(--pcs-surface-2)', borderColor: 'var(--pcs-border)', color: 'var(--pcs-text)' }}
      />

      <button
        type="button"
        onClick={() => void run()}
        disabled={!input.trim() || !password || busy}
        className="flex items-center gap-2 rounded-md px-4 py-2 text-sm font-medium transition-colors disabled:cursor-not-allowed disabled:opacity-40"
        style={{ background: 'var(--pcs-accent)', color: '#fff' }}
      >
        <KeyRound size={14} aria-hidden />
        {busy ? 'Procesando…' : direction === 'encrypt' ? 'Cifrar' : 'Descifrar'}
      </button>

      {error && <p className="text-xs" style={{ color: 'var(--pcs-danger)' }}>{error}</p>}

      {output && (
        <div className="space-y-2">
          <textarea
            value={output}
            readOnly
            rows={4}
            className="w-full resize-y rounded-md border px-3 py-2 text-sm font-mono"
            style={{ background: 'var(--pcs-surface-2)', borderColor: 'var(--pcs-accent)', color: 'var(--pcs-text)' }}
          />
          <button
            type="button"
            onClick={() => void copyOutput()}
            className="flex items-center gap-1.5 rounded-md px-3 py-1.5 text-xs cursor-pointer"
            style={{ background: 'var(--pcs-surface-2)', color: 'var(--pcs-text)' }}
          >
            <Copy size={12} aria-hidden /> Copiar resultado
          </button>
        </div>
      )}

      <p className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
        {mode === 'aes'
          ? 'AES-256-GCM con PBKDF2 (100k iteraciones). La salida es base64.'
          : 'XOR con clave repetida. La salida es base64 (auto-inversible con la misma clave).'}
      </p>
    </section>
  );
}
