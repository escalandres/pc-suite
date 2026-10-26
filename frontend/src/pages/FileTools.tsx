import { useState } from 'react';
import {
  FileSearch,
  Hash,
  Lock,
  ShieldAlert,
  Trash2,
  Unlock,
} from 'lucide-react';
import { bridge, BridgeError } from '../services/pcsuiteBridge';
import { OperationIds } from '../types/bridge';

/**
 * Página "Archivos" (Task 033): utilidades de seguridad de archivos —
 * encriptar con contraseña, borrado seguro, hash multi-algoritmo.
 * Esteganografía (33.2) llegará en una sub-tarea futura.
 */
export function FileTools() {
  const [tab, setTab] = useState<'encrypt' | 'hash' | 'shred'>('encrypt');

  return (
    <div className="space-y-6">
      <header>
        <h1 className="text-2xl font-semibold">Archivos</h1>
        <p className="mt-1 text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
          Utilidades de seguridad de archivos — todo local, sin red.
        </p>
      </header>

      {/* Tabs */}
      <div className="flex gap-2">
        {(
          [
            { id: 'encrypt', label: 'Encriptar', icon: Lock },
            { id: 'hash', label: 'Hash', icon: Hash },
            { id: 'shred', label: 'Borrado seguro', icon: Trash2 },
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

      {tab === 'encrypt' && <EncryptPanel />}
      {tab === 'hash' && <HashPanel />}
      {tab === 'shred' && <ShredPanel />}
    </div>
  );
}

// --- Encriptar / Desencriptar ---

function EncryptPanel() {
  const [mode, setMode] = useState<'encrypt' | 'decrypt'>('encrypt');
  const [inputPath, setInputPath] = useState('');
  const [password, setPassword] = useState('');
  const [deleteOriginal, setDeleteOriginal] = useState(false);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<{ kind: 'success' | 'error'; text: string } | null>(null);

  const pickFile = async () => {
    try {
      const result = await bridge.call<{ cancelled: boolean; paths: string[] }>(
        OperationIds.systemPickFiles,
        { multiselect: false },
        10 * 60_000,
      );
      if (!result.cancelled && result.paths.length > 0) {
        setInputPath(result.paths[0]);
      }
    } catch {
      // best-effort
    }
  };

  const runAction = async () => {
    if (!inputPath.trim() || !password || busy) return;
    setBusy(true);
    setNotice(null);
    try {
      if (mode === 'encrypt') {
        const result = await bridge.call<{ outputPath: string; sha256: string }>(
          OperationIds.filesEncrypt,
          { inputPath: inputPath.trim(), password, deleteOriginal: deleteOriginal || undefined },
          5 * 60_000,
        );
        setNotice({
          kind: 'success',
          text: `Encriptado → ${result.outputPath} (SHA-256: ${result.sha256.slice(0, 16)}…)`,
        });
      } else {
        const result = await bridge.call<{ outputPath: string; sha256: string }>(
          OperationIds.filesDecrypt,
          { inputPath: inputPath.trim(), password },
          5 * 60_000,
        );
        setNotice({
          kind: 'success',
          text: `Desencriptado → ${result.outputPath}`,
        });
      }
    } catch (e) {
      setNotice({
        kind: 'error',
        text: e instanceof BridgeError ? e.message : `No se pudo ${mode === 'encrypt' ? 'encriptar' : 'desencriptar'}.`,
      });
    } finally {
      setBusy(false);
    }
  };

  return (
    <section
      className="space-y-4 rounded-lg border p-4"
      style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
    >
      <div className="flex gap-2">
        <button
          type="button"
          onClick={() => setMode('encrypt')}
          className={`flex items-center gap-1.5 rounded-md px-3 py-1.5 text-xs font-medium cursor-pointer ${
            mode === 'encrypt' ? '' : 'opacity-50'
          }`}
          style={{ background: mode === 'encrypt' ? 'var(--pcs-accent)' : 'var(--pcs-surface-2)', color: mode === 'encrypt' ? '#fff' : 'var(--pcs-text)' }}
        >
          <Lock size={12} aria-hidden /> Encriptar
        </button>
        <button
          type="button"
          onClick={() => setMode('decrypt')}
          className={`flex items-center gap-1.5 rounded-md px-3 py-1.5 text-xs font-medium cursor-pointer ${
            mode === 'decrypt' ? '' : 'opacity-50'
          }`}
          style={{ background: mode === 'decrypt' ? 'var(--pcs-accent)' : 'var(--pcs-surface-2)', color: mode === 'decrypt' ? '#fff' : 'var(--pcs-text)' }}
        >
          <Unlock size={12} aria-hidden /> Desencriptar
        </button>
      </div>

      <div className="flex items-center gap-2">
        <input
          type="text"
          value={inputPath}
          onChange={(e) => setInputPath(e.target.value)}
          placeholder="Ruta del archivo…"
          className="flex-1 rounded-md border px-3 py-2 text-sm font-mono"
          style={{ background: 'var(--pcs-surface-2)', borderColor: 'var(--pcs-border)', color: 'var(--pcs-text)' }}
        />
        <button
          type="button"
          onClick={() => void pickFile()}
          className="shrink-0 cursor-pointer rounded-md border px-3 py-2 text-xs font-medium"
          style={{ borderColor: 'var(--pcs-border)', color: 'var(--pcs-text)' }}
        >
          <FileSearch size={14} aria-hidden className="mr-1" />
          Examinar…
        </button>
      </div>

      <input
        type="password"
        value={password}
        onChange={(e) => setPassword(e.target.value)}
        placeholder="Contraseña"
        autoComplete="new-password"
        className="w-full rounded-md border px-3 py-2 text-sm"
        style={{ background: 'var(--pcs-surface-2)', borderColor: 'var(--pcs-border)', color: 'var(--pcs-text)' }}
      />

      {mode === 'encrypt' && (
        <label className="flex cursor-pointer items-center gap-2 text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
          <input
            type="checkbox"
            checked={deleteOriginal}
            onChange={(e) => setDeleteOriginal(e.target.checked)}
            className="cursor-pointer"
          />
          Borrar el original de forma segura tras encriptar
        </label>
      )}

      <button
        type="button"
        onClick={() => void runAction()}
        disabled={!inputPath.trim() || !password || busy}
        className="flex items-center gap-2 rounded-md px-4 py-2 text-sm font-medium transition-colors disabled:cursor-not-allowed disabled:opacity-40"
        style={{ background: 'var(--pcs-accent)', color: '#fff' }}
      >
        {mode === 'encrypt' ? <Lock size={14} aria-hidden /> : <Unlock size={14} aria-hidden />}
        {busy ? 'Procesando…' : mode === 'encrypt' ? 'Encriptar archivo' : 'Desencriptar archivo'}
      </button>

      {notice && (
        <p
          className="text-xs break-words"
          style={{ color: notice.kind === 'success' ? 'var(--pcs-success)' : 'var(--pcs-danger)' }}
        >
          {notice.text}
        </p>
      )}

      <p className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
        AES-256-CBC con PBKDF2 (100k iteraciones, SHA-256). El archivo encriptado lleva
        extensión <code>.pcsuite-enc</code>.
      </p>
    </section>
  );
}

// --- Hash ---

function HashPanel() {
  const [inputPath, setInputPath] = useState('');
  const [busy, setBusy] = useState(false);
  const [hashes, setHashes] = useState<{ sha256: string; sha1: string; md5: string; crc32: number } | null>(null);

  const pickFile = async () => {
    try {
      const result = await bridge.call<{ cancelled: boolean; paths: string[] }>(
        OperationIds.systemPickFiles,
        { multiselect: false },
        10 * 60_000,
      );
      if (!result.cancelled && result.paths.length > 0) {
        setInputPath(result.paths[0]);
      }
    } catch { /* best-effort */ }
  };

  const runHash = async () => {
    if (!inputPath.trim() || busy) return;
    setBusy(true);
    setHashes(null);
    try {
      const result = await bridge.call<{ sha256: string; sha1: string; md5: string; crc32: number }>(
        OperationIds.filesHash,
        { path: inputPath.trim() },
        2 * 60_000,
      );
      setHashes(result);
    } catch {
      setHashes(null);
    } finally {
      setBusy(false);
    }
  };

  return (
    <section
      className="space-y-4 rounded-lg border p-4"
      style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
    >
      <div className="flex items-center gap-2">
        <input
          type="text"
          value={inputPath}
          onChange={(e) => setInputPath(e.target.value)}
          placeholder="Ruta del archivo…"
          className="flex-1 rounded-md border px-3 py-2 text-sm font-mono"
          style={{ background: 'var(--pcs-surface-2)', borderColor: 'var(--pcs-border)', color: 'var(--pcs-text)' }}
        />
        <button
          type="button"
          onClick={() => void pickFile()}
          className="shrink-0 cursor-pointer rounded-md border px-3 py-2 text-xs font-medium"
          style={{ borderColor: 'var(--pcs-border)', color: 'var(--pcs-text)' }}
        >
          <FileSearch size={14} aria-hidden className="mr-1" />
          Examinar…
        </button>
      </div>

      <button
        type="button"
        onClick={() => void runHash()}
        disabled={!inputPath.trim() || busy}
        className="flex items-center gap-2 rounded-md px-4 py-2 text-sm font-medium transition-colors disabled:cursor-not-allowed disabled:opacity-40"
        style={{ background: 'var(--pcs-accent)', color: '#fff' }}
      >
        <Hash size={14} aria-hidden />
        {busy ? 'Calculando…' : 'Calcular hashes'}
      </button>

      {hashes && (
        <div className="space-y-2">
          {(
            [
              ['SHA-256', hashes.sha256],
              ['SHA-1', hashes.sha1],
              ['MD5', hashes.md5],
              ['CRC32', hashes.crc32.toString(16).toUpperCase().padStart(8, '0')],
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
                onClick={() => void bridge.call(OperationIds.systemWriteClipboard, { text: value }).catch(() => {})}
                className="shrink-0 cursor-pointer rounded border px-1.5 py-0.5 text-xs"
                style={{ borderColor: 'var(--pcs-border)', color: 'var(--pcs-text-muted)' }}
                title="Copiar al portapapeles"
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

// --- Borrado seguro ---

function ShredPanel() {
  const [inputPath, setInputPath] = useState('');
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<{ kind: 'success' | 'error'; text: string } | null>(null);

  const pickFile = async () => {
    try {
      const result = await bridge.call<{ cancelled: boolean; paths: string[] }>(
        OperationIds.systemPickFiles,
        { multiselect: false },
        10 * 60_000,
      );
      if (!result.cancelled && result.paths.length > 0) {
        setInputPath(result.paths[0]);
      }
    } catch { /* best-effort */ }
  };

  const runShred = async () => {
    if (!inputPath.trim() || busy) return;
    setBusy(true);
    setNotice(null);
    try {
      await bridge.call(OperationIds.filesShred, { path: inputPath.trim() }, 2 * 60_000);
      setNotice({ kind: 'success', text: `Archivo borrado de forma segura: ${inputPath.trim()}` });
      setInputPath('');
    } catch (e) {
      setNotice({
        kind: 'error',
        text: e instanceof BridgeError ? e.message : 'No se pudo borrar el archivo.',
      });
    } finally {
      setBusy(false);
    }
  };

  return (
    <section
      className="space-y-4 rounded-lg border p-4"
      style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
    >
      <div
        className="flex items-start gap-2 rounded-md border px-3 py-2.5 text-xs"
        style={{
          borderColor: 'color-mix(in srgb, var(--pcs-danger) 55%, transparent)',
          background: 'color-mix(in srgb, var(--pcs-danger) 10%, transparent)',
          color: 'var(--pcs-danger)',
        }}
      >
        <ShieldAlert size={14} aria-hidden className="mt-0.5 shrink-0" />
        <div>
          <p className="font-semibold">IRREVERSIBLE</p>
          <p className="mt-0.5">El archivo se sobrescribe con datos aleatorios y ceros
          antes de borrarse. No se puede recuperar.</p>
        </div>
      </div>

      <div className="flex items-center gap-2">
        <input
          type="text"
          value={inputPath}
          onChange={(e) => setInputPath(e.target.value)}
          placeholder="Ruta del archivo a borrar…"
          className="flex-1 rounded-md border px-3 py-2 text-sm font-mono"
          style={{ background: 'var(--pcs-surface-2)', borderColor: 'var(--pcs-border)', color: 'var(--pcs-text)' }}
        />
        <button
          type="button"
          onClick={() => void pickFile()}
          className="shrink-0 cursor-pointer rounded-md border px-3 py-2 text-xs font-medium"
          style={{ borderColor: 'var(--pcs-border)', color: 'var(--pcs-text)' }}
        >
          <FileSearch size={14} aria-hidden className="mr-1" />
          Examinar…
        </button>
      </div>

      <button
        type="button"
        onClick={() => void runShred()}
        disabled={!inputPath.trim() || busy}
        className="flex items-center gap-2 rounded-md px-4 py-2 text-sm font-medium transition-colors disabled:cursor-not-allowed disabled:opacity-40"
        style={{ background: 'var(--pcs-danger)', color: '#fff' }}
      >
        <Trash2 size={14} aria-hidden />
        {busy ? 'Sobrescribiendo…' : 'Borrar de forma segura'}
      </button>

      {notice && (
        <p className="text-xs" style={{ color: notice.kind === 'success' ? 'var(--pcs-success)' : 'var(--pcs-danger)' }}>
          {notice.text}
        </p>
      )}

      <p className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
        3 pasadas: aleatorio → aleatorio → ceros → borrar. Estándar DoD 5220.22-M simplificado.
      </p>
    </section>
  );
}
