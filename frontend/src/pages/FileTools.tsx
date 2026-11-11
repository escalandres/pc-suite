import { useState, useEffect } from 'react';
import {
  FileSearch,
  FolderOpen,
  Hash,
  Image as ImageIcon,
  KeyRound,
  Lock,
  ShieldAlert,
  Trash2,
  Unlock,
} from 'lucide-react';
import { bridge, BridgeError } from '../services/pcsuiteBridge';
import { OperationIds } from '../types/bridge';
import { useTranslation } from '../hooks/useTranslation';

/**
 * Página "Archivos" (Task 033): utilidades de seguridad de archivos —
 * encriptar con contraseña, borrado seguro, hash multi-algoritmo.
 * Esteganografía (33.2) llegará en una sub-tarea futura.
 */
function passwordEntropy(pwd: string): number {
  if (!pwd) return 0;
  let charset = 0;
  if (/[a-z]/.test(pwd)) charset += 26;
  if (/[A-Z]/.test(pwd)) charset += 26;
  if (/[0-9]/.test(pwd)) charset += 10;
  if (/[^a-zA-Z0-9]/.test(pwd)) charset += 32;
  if (charset === 0) return 0;
  return pwd.length * Math.log2(charset);
}

function passwordStrength(pwd: string): { label: string; color: string } {
  const e = passwordEntropy(pwd);
  if (e < 28) return { label: 'Contraseña débil', color: 'var(--pcs-danger)' };
  if (e < 36) return { label: 'Contraseña aceptable', color: 'var(--pcs-warning)' };
  if (e < 60) return { label: 'Contraseña fuerte', color: 'var(--pcs-success)' };
  return { label: 'Contraseña muy fuerte', color: 'var(--pcs-success)' };
}

export function FileTools() {
  const { t } = useTranslation();
  const [tab, setTab] = useState<'encrypt' | 'hash' | 'shred' | 'stego' | 'fido2'>('encrypt');

  return (
    <div className="space-y-6">
      <header>
        <h1 className="text-2xl font-semibold">{t('sidebar.files')}</h1>
        <p className="mt-1 text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
          {t('pages.files.subtitle')}
        </p>
      </header>

      {/* Tabs */}
      <div className="flex gap-2">
        {(
          [
            { id: 'encrypt', labelKey: 'pages.files.tabEncrypt', icon: Lock },
            { id: 'hash', labelKey: 'pages.files.tabHash', icon: Hash },
            { id: 'shred', labelKey: 'pages.files.tabShred', icon: Trash2 },
            { id: 'stego', labelKey: 'pages.files.tabStego', icon: ImageIcon },
  { id: 'fido2', labelKey: 'pages.files.tabFido2', icon: KeyRound },
          ] as const
        ).map(({ id, labelKey, icon: Icon }) => (
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
            {t(labelKey)}
          </button>
        ))}
      </div>

      {tab === 'encrypt' && <EncryptPanel />}
      {tab === 'hash' && <HashPanel />}
      {tab === 'shred' && <ShredPanel />}
      {tab === 'stego' && <StegoPanel />}
      {tab === 'fido2' && <Fido2Panel />}
    </div>
  );
}

// --- Encriptar / Desencriptar ---

function EncryptPanel() {
  const [mode, setMode] = useState<'encrypt' | 'decrypt'>('encrypt');
  const [inputPath, setInputPath] = useState('');
  const [isFolder, setIsFolder] = useState(false);
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
        setIsFolder(false);
      }
    } catch {
      // best-effort
    }
  };

  // Task 033.5: la MISMA op protege carpetas — el backend empaqueta el
  // árbol completo en un contenedor .pcsuite-enc único.
  const pickFolder = async () => {
    try {
      const result = await bridge.call<{ cancelled: boolean; path?: string | null }>(
        OperationIds.systemPickFolder,
        undefined,
        10 * 60_000,
      );
      if (!result.cancelled && result.path) {
        setInputPath(result.path);
        setIsFolder(true);
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
        const result = await bridge.call<{ outputPath: string; sha256: string; kind?: string }>(
          OperationIds.filesEncrypt,
          { inputPath: inputPath.trim(), password, deleteOriginal: deleteOriginal || undefined },
          5 * 60_000,
        );
        setNotice({
          kind: 'success',
          text: result.kind === 'folder'
            ? `Carpeta encriptada → ${result.outputPath} (todo el árbol en un solo contenedor)`
            : `Encriptado → ${result.outputPath} (SHA-256: ${result.sha256.slice(0, 16)}…)`,
        });
      } else {
        const result = await bridge.call<{ outputPath: string; sha256: string; kind?: string }>(
          OperationIds.filesDecrypt,
          { inputPath: inputPath.trim(), password },
          5 * 60_000,
        );
        setNotice({
          kind: 'success',
          text: result.kind === 'folder'
            ? `Carpeta restaurada en → ${result.outputPath}`
            : `Desencriptado → ${result.outputPath}`,
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
          onClick={() => {
            setMode('decrypt');
            // Una carpeta seleccionada en modo encriptar es inútil aquí:
            // el contenedor siempre es un ARCHIVO .pcsuite-enc.
            if (isFolder) {
              setIsFolder(false);
              setInputPath('');
            }
          }}
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
          Archivo…
        </button>
        {mode === 'encrypt' && (
          <button
            type="button"
            onClick={() => void pickFolder()}
            className={`shrink-0 cursor-pointer rounded-md border px-3 py-2 text-xs font-medium ${
              isFolder ? '' : 'opacity-70 hover:opacity-100'
            }`}
            style={{
              borderColor: isFolder ? 'var(--pcs-accent)' : 'var(--pcs-border)',
              color: 'var(--pcs-text)',
            }}
            title="Proteger una carpeta completa (subdirectorios incluidos)"
          >
            <FolderOpen size={14} aria-hidden className="mr-1" />
            Carpeta…
          </button>
        )}
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

      {password.length > 0 && (
        <p className="text-xs" style={{ color: passwordStrength(password).color }}>
          {passwordStrength(password).label} ({Math.round(passwordEntropy(password))} bits)
        </p>
      )}
      {mode === 'encrypt' && !isFolder && (
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
      {mode === 'encrypt' && isFolder && (
        <p className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
          Protegerás la carpeta completa (subdirectorios incluidos) en un solo
          contenedor. El borrado seguro de carpetas no está disponible todavía:
          verificá la desencriptación y borrá la carpeta a mano.
        </p>
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
        AES-256-CBC con PBKDF2 (100k iteraciones, SHA-256). La salida lleva extensión{' '}
        <code>.pcsuite-enc</code> — archivos y carpetas por igual; una carpeta se
        restaura completa en su sitio (nunca sobrescribe: aterriza como "(1)" si ya existe).
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


function StegoPanel() {
  const { t } = useTranslation();
  const [mode, setMode] = useState<'embed' | 'extract' | 'verify'>('embed');
  const [imagePath, setImagePath] = useState('');
  const [textPayload, setTextPayload] = useState('');
  const [filePayloadPath, setFilePayloadPath] = useState('');
  const [password, setPassword] = useState('');
  const [deleteOriginal, setDeleteOriginal] = useState(false);
  const [outputPath, setOutputPath] = useState('');
  const [payloadMode, setPayloadMode] = useState<'text' | 'file'>('text');
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<{ kind: 'success' | 'error'; text: string } | null>(null);
  const [extractedText, setExtractedText] = useState<string | null>(null);
  const [extractedFile, setExtractedFile] = useState<string | null>(null);
  const [verifyResult, setVerifyResult] = useState<{ has: boolean; size: number; isText: boolean } | null>(null);

  // Limpiar inputs al cambiar de modo.
  useEffect(() => {
    setImagePath('');
    setTextPayload('');
    setFilePayloadPath('');
    setPassword('');
    setDeleteOriginal(false);
    setOutputPath('');
    setExtractedText(null);
    setExtractedFile(null);
    setVerifyResult(null);
    setNotice(null);
  }, [mode]);

  const pickImage = async () => {
    try {
      const result = await bridge.call<{ cancelled: boolean; paths: string[] }>(OperationIds.systemPickFiles, undefined, 60_000);
      if (result.cancelled || result.paths.length === 0) return;
      setImagePath(result.paths[0]);
      setNotice(null);
      setExtractedText(null);
      setExtractedFile(null);
      setVerifyResult(null);
    } catch (e) {
      setNotice({ kind: 'error', text: e instanceof BridgeError ? e.message : t('common.errorFetch') });
    }
  };

  const pickPayloadFile = async () => {
    try {
      const result = await bridge.call<{ cancelled: boolean; paths: string[] }>(OperationIds.systemPickFiles, undefined, 60_000);
      if (result.cancelled || result.paths.length === 0) return;
      setFilePayloadPath(result.paths[0]);
    } catch (e) {
      setNotice({ kind: 'error', text: e instanceof BridgeError ? e.message : t('common.errorFetch') });
    }
  };

  const pickOutputFile = async () => {
    try {
      const result = await bridge.call<{ cancelled: boolean; paths: string[] }>(OperationIds.systemPickFiles, undefined, 60_000);
      if (result.cancelled || result.paths.length === 0) return;
      setOutputPath(result.paths[0]);
    } catch (e) {
      setNotice({ kind: 'error', text: e instanceof BridgeError ? e.message : t('common.errorFetch') });
    }
  };

  const doEmbed = async () => {
    if (!imagePath) { setNotice({ kind: 'error', text: t('pages.files.stegoImage') + ' requerido.' }); return; }
    setBusy(true); setNotice(null);
    try {
      const result = await bridge.call<{ outputPath: string }>(OperationIds.filesStegoEmbed, {
        imagePath,
        textPayload: payloadMode === 'text' ? textPayload : undefined,
        filePayloadPath: payloadMode === 'file' ? filePayloadPath : undefined,
        password: password || undefined,
        outputPath: outputPath || undefined,
        deleteOriginal,
      });
      setNotice({ kind: 'success', text: t('pages.files.stegoSuccess', { path: result.outputPath }) });
    } catch (e) {
      setNotice({ kind: 'error', text: e instanceof BridgeError ? e.message : t('common.errorFetch') });
    } finally { setBusy(false); }
  };

  const doExtract = async () => {
    if (!imagePath) { setNotice({ kind: 'error', text: t('pages.files.stegoImage') + ' requerido.' }); return; }
    setBusy(true); setNotice(null); setExtractedText(null); setExtractedFile(null);
    try {
      const result = await bridge.call<{ text?: string | null; outputFilePath?: string | null; hasFilePayload: boolean }>(
        OperationIds.filesStegoExtract, { imagePath, password: password || undefined });
      if (result.hasFilePayload && result.outputFilePath) {
        setExtractedFile(result.outputFilePath);
        setNotice({ kind: 'success', text: t('pages.files.stegoExtractedFile', { path: result.outputFilePath }) });
      } else if (result.text != null) {
        setExtractedText(result.text);
        setNotice({ kind: 'success', text: t('pages.files.stegoExtractedText') });
      } else {
        setNotice({ kind: 'error', text: t('common.noData') });
      }
    } catch (e) {
      setNotice({ kind: 'error', text: e instanceof BridgeError ? e.message : t('common.errorFetch') });
    } finally { setBusy(false); }
  };

  const doVerify = async () => {
    if (!imagePath) { setNotice({ kind: 'error', text: t('pages.files.stegoImage') + ' requerido.' }); return; }
    setBusy(true); setNotice(null); setVerifyResult(null);
    try {
      const result = await bridge.call<{ hasHiddenData: boolean; estimatedSizeBytes: number; isText: boolean }>(
        OperationIds.filesStegoVerify, { imagePath });
      setVerifyResult({ has: result.hasHiddenData, size: result.estimatedSizeBytes, isText: result.isText });
    } catch (e) {
      setNotice({ kind: 'error', text: e instanceof BridgeError ? e.message : t('common.errorFetch') });
    } finally { setBusy(false); }
  };

  return (
    <div className="space-y-6">
      <div className="flex gap-2">
        {(['embed', 'extract', 'verify'] as const).map((m) => (
          <button key={m} type="button" onClick={() => setMode(m)}
            className={`rounded-md px-4 py-2 text-sm font-medium transition-colors cursor-pointer ${mode === m ? '' : 'opacity-60 hover:opacity-90'}`}
            style={{ background: mode === m ? 'var(--pcs-accent)' : 'var(--pcs-surface-2)', color: mode === m ? '#fff' : 'var(--pcs-text)' }}>
            {m === 'embed' ? t('pages.files.stegoHide') : m === 'extract' ? t('pages.files.stegoExtract') : t('pages.files.stegoVerify')}
          </button>
        ))}
      </div>

      <div className="space-y-4 rounded-lg border p-4" style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}>
        <div className="space-y-2">
          <label className="text-sm font-medium">{t('pages.files.stegoImage')}</label>
          <div className="flex gap-2">
            <input type="text" value={imagePath} onChange={(e) => setImagePath(e.target.value)} placeholder="/ruta/imagen.png"
              className="flex-1 rounded-md border px-3 py-2 text-sm"
              style={{ background: 'var(--pcs-bg)', color: 'var(--pcs-text)', borderColor: 'var(--pcs-border)' }} />
            <button type="button" onClick={() => void pickImage()} className="rounded-md border px-3 py-2 text-sm cursor-pointer" style={{ borderColor: 'var(--pcs-border)' }}>
              {t('pages.files.stegoPickImage')}
            </button>
          </div>
        </div>

        {mode === 'embed' && (
          <>
            <div className="flex gap-2">
              <button type="button" onClick={() => setPayloadMode('text')} className={`rounded-md px-3 py-1.5 text-sm ${payloadMode === 'text' ? 'font-semibold' : 'opacity-60'}`} style={{ background: payloadMode === 'text' ? 'var(--pcs-accent)' : 'var(--pcs-surface-2)', color: payloadMode === 'text' ? '#fff' : 'var(--pcs-text)' }}>
                {t('pages.files.stegoTextMode')}
              </button>
              <button type="button" onClick={() => setPayloadMode('file')} className={`rounded-md px-3 py-1.5 text-sm ${payloadMode === 'file' ? 'font-semibold' : 'opacity-60'}`} style={{ background: payloadMode === 'file' ? 'var(--pcs-accent)' : 'var(--pcs-surface-2)', color: payloadMode === 'file' ? '#fff' : 'var(--pcs-text)' }}>
                {t('pages.files.stegoFileMode')}
              </button>
            </div>

            {payloadMode === 'text' ? (
              <div className="space-y-2">
                <label className="text-sm font-medium">{t('pages.files.stegoPayloadText')}</label>
                <textarea value={textPayload} onChange={(e) => setTextPayload(e.target.value)} rows={4}
                  className="w-full rounded-md border px-3 py-2 text-sm"
                  style={{ background: 'var(--pcs-bg)', color: 'var(--pcs-text)', borderColor: 'var(--pcs-border)' }} />
              </div>
            ) : (
              <div className="space-y-2">
                <label className="text-sm font-medium">{t('pages.files.stegoPayloadFile')}</label>
                <div className="flex gap-2">
                  <input type="text" value={filePayloadPath} onChange={(e) => setFilePayloadPath(e.target.value)}
                    className="flex-1 rounded-md border px-3 py-2 text-sm"
                    style={{ background: 'var(--pcs-bg)', color: 'var(--pcs-text)', borderColor: 'var(--pcs-border)' }} />
                  <button type="button" onClick={() => void pickPayloadFile()} className="rounded-md border px-3 py-2 text-sm cursor-pointer" style={{ borderColor: 'var(--pcs-border)' }}>
                    {t('pages.files.stegoPickFile')}
                  </button>
                </div>
              </div>
            )}

            <div className="space-y-2">
              <label className="text-sm font-medium">{t('pages.files.stegoPassword')}</label>
              <input type="password" value={password} onChange={(e) => setPassword(e.target.value)}
                className="w-full rounded-md border px-3 py-2 text-sm"
                style={{ background: 'var(--pcs-bg)', color: 'var(--pcs-text)', borderColor: 'var(--pcs-border)' }} />
              {password.length > 0 && (
                <p className="text-xs" style={{ color: password.length < 8 ? 'var(--pcs-danger)' : password.length < 12 ? 'var(--pcs-warning)' : 'var(--pcs-success)' }}>
                  {password.length < 8 ? 'Contraseña débil (mín. 8 caracteres)' : password.length < 12 ? 'Contraseña aceptable' : 'Contraseña fuerte'}
                </p>
              )}
            </div>

            <div className="space-y-2">
              <label className="flex items-center gap-2 text-sm cursor-pointer">
                <input type="checkbox" checked={deleteOriginal} onChange={(e) => setDeleteOriginal(e.target.checked)} />
                Borrar el original tras ocultar
              </label>
            </div>

            <div className="rounded-md border px-3 py-2 text-xs" style={{ borderColor: 'var(--pcs-warning)', color: 'var(--pcs-warning)', background: 'color-mix(in srgb, var(--pcs-warning) 10%, transparent)' }}>
              Advertencia: guarda la imagen de salida como PNG. Si la recomprimes (JPEG/WebP) se destruyen los datos ocultos.
            </div>

            <div className="space-y-2">
              <label className="text-sm font-medium">{t('pages.files.stegoOutput')}</label>
              <div className="flex gap-2">
                <input type="text" value={outputPath} onChange={(e) => setOutputPath(e.target.value)} placeholder="auto (.stego.png)"
                  className="flex-1 rounded-md border px-3 py-2 text-sm"
                  style={{ background: 'var(--pcs-bg)', color: 'var(--pcs-text)', borderColor: 'var(--pcs-border)' }} />
                <button type="button" onClick={() => void pickOutputFile()} className="rounded-md border px-3 py-2 text-sm cursor-pointer" style={{ borderColor: 'var(--pcs-border)' }}>
                  {t('pages.files.stegoPickImage')}
                </button>
              </div>
            </div>

            <button type="button" onClick={() => void doEmbed()} disabled={busy} className="rounded-md px-4 py-2 text-sm font-medium cursor-pointer disabled:opacity-50" style={{ background: 'var(--pcs-accent)', color: '#fff' }}>
              {busy ? t('pages.files.stegoHiding') : t('pages.files.stegoHide')}
            </button>
          </>
        )}

        {mode === 'extract' && (
          <>
            <div className="space-y-2">
              <label className="text-sm font-medium">{t('pages.files.stegoPassword')}</label>
              <input type="password" value={password} onChange={(e) => setPassword(e.target.value)}
                className="w-full rounded-md border px-3 py-2 text-sm"
                style={{ background: 'var(--pcs-bg)', color: 'var(--pcs-text)', borderColor: 'var(--pcs-border)' }} />
            </div>

            <button type="button" onClick={() => void doExtract()} disabled={busy} className="rounded-md px-4 py-2 text-sm font-medium cursor-pointer disabled:opacity-50" style={{ background: 'var(--pcs-accent)', color: '#fff' }}>
              {busy ? t('pages.files.stegoExtracting') : t('pages.files.stegoExtract')}
            </button>

            {extractedText != null && (
              <div className="space-y-2">
                <p className="text-sm font-medium">{t('pages.files.stegoExtractedText')}</p>
                <textarea value={extractedText} readOnly rows={6}
                  className="w-full rounded-md border px-3 py-2 text-sm"
                  style={{ background: 'var(--pcs-bg)', color: 'var(--pcs-text)', borderColor: 'var(--pcs-border)' }} />
              </div>
            )}
            {extractedFile && (
              <p className="text-sm" style={{ color: 'var(--pcs-success)' }}>{t('pages.files.stegoExtractedFile', { path: extractedFile })}</p>
            )}
          </>
        )}

        {mode === 'verify' && (
          <>
            <button type="button" onClick={() => void doVerify()} disabled={busy} className="rounded-md px-4 py-2 text-sm font-medium cursor-pointer disabled:opacity-50" style={{ background: 'var(--pcs-accent)', color: '#fff' }}>
              {busy ? t('pages.files.stegoVerifying') : t('pages.files.stegoVerify')}
            </button>
            {verifyResult && (
              <p className="text-sm" style={{ color: verifyResult.has ? 'var(--pcs-success)' : 'var(--pcs-text-muted)' }}>
                {verifyResult.has
                  ? t('pages.files.stegoHasHidden', { size: verifyResult.size, type: verifyResult.isText ? 'texto' : 'archivo' })
                  : t('pages.files.stegoNoHidden')}
              </p>
            )}
          </>
        )}

        {notice && (
          <p className="text-sm" style={{ color: notice.kind === 'success' ? 'var(--pcs-success)' : 'var(--pcs-danger)' }} role="status">
            {notice.text}
          </p>
        )}
      </div>
    </div>
  );
}


// --- FIDO2 (llave de hardware) ---

function bufToBase64(buf: ArrayBuffer): string {
  const bytes = new Uint8Array(buf);
  let binary = '';
  for (let i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i]);
  return btoa(binary);
}

function base64ToBuf(b64: string): ArrayBuffer {
  const binary = atob(b64);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  return bytes.buffer;
}

function Fido2Panel() {
  const [supported, setSupported] = useState<boolean | null>(null);
  const [credentialId, setCredentialId] = useState<string | null>(() => {
    try { return localStorage.getItem('pcsuite.fido2.credentialId'); } catch { return null; }
  });

  const log = (msg: string) => {
    console.log(msg);
    bridge.call(OperationIds.systemAppendLog, { message: `[FIDO2] ${msg}` }).catch(() => { /* noop */ });
  };
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<{ kind: 'success' | 'error'; text: string } | null>(null);
  const [inputPath, setInputPath] = useState('');
  const [mode, setMode] = useState<'encrypt' | 'decrypt'>('encrypt');
  const [deleteOriginal, setDeleteOriginal] = useState(false);

  useEffect(() => {
    setSupported(
      typeof window !== 'undefined' &&
      !!window.PublicKeyCredential &&
      typeof navigator?.credentials?.create === 'function',
    );
  }, []);

  const register = async () => {
    if (!supported) return;
    setBusy(true);
    setNotice(null);
    log('register: start');
    try {
      const challenge = crypto.getRandomValues(new Uint8Array(32));
      const userId = crypto.getRandomValues(new Uint8Array(16));
      log(`register: challenge=${bufToBase64(challenge.buffer)} userId=${bufToBase64(userId.buffer)}`);
      const cred = (await navigator.credentials.create({
        publicKey: {
          rp: { name: 'PCSuite', id: window.location.hostname || 'localhost' },
          user: { id: userId, name: 'pcsuite-user', displayName: 'PCSuite user' },
          challenge,
          pubKeyCredParams: [{ type: 'public-key', alg: -7 }, { type: 'public-key', alg: -257 }],
          authenticatorSelection: { userVerification: 'preferred', residentKey: 'preferred' },
          timeout: 60_000,
          extensions: { hmacCreateSecret: true } as any,
        },
      })) as PublicKeyCredential | null;

      if (!cred) throw new Error('No se obtuvo credencial.');
      const ext = cred.getClientExtensionResults() as any;
      log(`register: credId=${bufToBase64(cred.rawId)} ext=${JSON.stringify(ext)}`);
      if (!ext?.hmacCreateSecret) {
        throw new Error('La llave no soporta hmac-secret (se requiere YubiKey 5+ o Windows Hello).');
      }
      const id = bufToBase64(cred.rawId);
      localStorage.setItem('pcsuite.fido2.credentialId', id);
      setCredentialId(id);
      setNotice({ kind: 'success', text: 'Llave FIDO2 registrada correctamente.' });
      log('register: success');
    } catch (e) {
      log(`register: ERROR ${e instanceof Error ? e.name + ': ' + e.message : String(e)}`);
      setNotice({ kind: 'error', text: e instanceof Error ? e.message : 'Error al registrar.' });
    } finally {
      setBusy(false);
    }
  };

  const deriveKey = async (): Promise<string | null> => {
    if (!credentialId) return null;
    const salt = new TextEncoder().encode('PCSuite FIDO2 file encryption v1');
    log(`deriveKey: credentialId=${credentialId.slice(0, 12)}... salt=${bufToBase64(salt.buffer)}`);
    let cred: PublicKeyCredential | null = null;
    try {
      cred = (await navigator.credentials.get({
        publicKey: {
          challenge: crypto.getRandomValues(new Uint8Array(32)),
          allowCredentials: [{ type: 'public-key', id: base64ToBuf(credentialId) }],
          userVerification: 'preferred',
          timeout: 60_000,
          extensions: { hmacGetSecret: { salt1: salt.buffer.slice(salt.byteOffset, salt.byteOffset + salt.byteLength) } } as any,
        },
      })) as PublicKeyCredential | null;
    } catch (e) {
      log(`deriveKey: credentials.get ERROR ${e instanceof Error ? e.name + ': ' + e.message : String(e)}`);
      return null;
    }
    if (!cred) { log('deriveKey: no cred'); return null; }
    const ext = cred.getClientExtensionResults() as any;
    log(`deriveKey: ext=${JSON.stringify(ext)}`);
    const secret =
      ext?.hmacGetSecret?.results?.hmac ||
      ext?.hmacGetSecret?.hmac ||
      ext?.results?.hmac;
    if (!secret) {
      log('deriveKey: no hmac secret in extension results');
      return null;
    }
    const key = bufToBase64(secret);
    log(`deriveKey: derived key length=${secret.byteLength} base64=${key.slice(0, 16)}...`);
    return key;
  };

  const runAction = async () => {
    if (!inputPath.trim() || busy || !credentialId) return;
    setBusy(true);
    setNotice(null);
    log(`runAction: mode=${mode} path=${inputPath.trim()}`);
    try {
      const key = await deriveKey();
      if (!key) { log('runAction: deriveKey returned null'); throw new Error('No se pudo derivar la clave de la llave FIDO2.'); }
      log(`runAction: derived key (base64 prefix)=${key.slice(0,16)}...`);
      if (mode === 'encrypt') {
        const result = await bridge.call<{ outputPath: string; sha256: string; kind?: string }>(
          OperationIds.filesFido2Encrypt,
          { inputPath: inputPath.trim(), password: key, deleteOriginal: deleteOriginal || undefined },
          5 * 60_000,
        );
        log(`runAction: encrypt OK outputPath=${result.outputPath}`);
        setNotice({ kind: 'success', text: `Encriptado con FIDO2 → ${result.outputPath}` });
      } else {
        const result = await bridge.call<{ outputPath: string; sha256: string; kind?: string }>(
          OperationIds.filesFido2Decrypt,
          { inputPath: inputPath.trim(), password: key },
          5 * 60_000,
        );
        log(`runAction: decrypt OK outputPath=${result.outputPath}`);
        setNotice({ kind: 'success', text: `Desencriptado con FIDO2 → ${result.outputPath}` });
      }
    } catch (e) {
      log(`runAction: ERROR ${e instanceof Error ? e.name + ': ' + e.message : String(e)}`);
      setNotice({ kind: 'error', text: e instanceof Error ? e.message : 'Error FIDO2.' });
    } finally {
      setBusy(false);
    }
  };
  const forget = () => {
    localStorage.removeItem('pcsuite.fido2.credentialId');
    setCredentialId(null);
    setNotice({ kind: 'success', text: 'Llave olvidada. Registra de nuevo para usar.' });
  };

  return (
    <section className="space-y-4 rounded-lg border p-4" style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}>
      <div className="flex items-center gap-2">
        <KeyRound size={16} aria-hidden style={{ color: 'var(--pcs-accent)' }} />
        <h2 className="text-sm font-semibold">FIDO2 (llave de hardware)</h2>
      </div>

      {supported === false && (
        <p className="text-xs" style={{ color: 'var(--pcs-danger)' }}>
          Este navegador/host no soporta WebAuthn. Usa un navegador moderno o Windows Hello.
        </p>
      )}

      {supported && (
        <>
          <div className="flex flex-wrap items-center gap-2">
            {!credentialId ? (
              <button
                type="button"
                disabled={busy}
                onClick={register}
                className="rounded-md px-3 py-1.5 text-xs font-medium text-white"
                style={{ background: 'var(--pcs-accent)' }}
              >
                {busy ? 'Registrando…' : 'Registrar llave FIDO2'}
              </button>
            ) : (
              <>
                <span className="text-xs" style={{ color: 'var(--pcs-success)' }}>
                  Llave registrada: {credentialId.slice(0, 12)}…
                </span>
                <button
                  type="button"
                  onClick={forget}
                  className="rounded-md px-3 py-1.5 text-xs font-medium"
                  style={{ background: 'var(--pcs-surface-2)', color: 'var(--pcs-text)' }}
                >
                  Olvidar llave
                </button>
              </>
            )}
          </div>

          {credentialId && (
            <div className="space-y-3">
              <div className="flex gap-2">
                <button
                  type="button"
                  onClick={() => setMode('encrypt')}
                  className="rounded-md border px-3 py-1 text-xs font-medium"
                  style={{
                    borderColor: 'var(--pcs-border)',
                    background: mode === 'encrypt' ? 'var(--pcs-accent)' : 'var(--pcs-surface)',
                    color: mode === 'encrypt' ? '#fff' : 'var(--pcs-text)',
                  }}
                >
                  Encriptar
                </button>
                <button
                  type="button"
                  onClick={() => setMode('decrypt')}
                  className="rounded-md border px-3 py-1 text-xs font-medium"
                  style={{
                    borderColor: 'var(--pcs-border)',
                    background: mode === 'decrypt' ? 'var(--pcs-accent)' : 'var(--pcs-surface)',
                    color: mode === 'decrypt' ? '#fff' : 'var(--pcs-text)',
                  }}
                >
                  Desencriptar
                </button>
              </div>

              <div className="flex gap-2">
                <input
                  className="flex-1 rounded-md border px-2 py-1 text-sm"
                  style={{ borderColor: 'var(--pcs-border)', background: 'var(--pcs-surface)', color: 'var(--pcs-text)' }}
                  placeholder="Ruta del archivo"
                  value={inputPath}
                  onChange={(e) => setInputPath(e.target.value)}
                />
                <button
                  type="button"
                  onClick={async () => {
                    try {
                      const result = await bridge.call<{ cancelled: boolean; paths: string[] }>(
                        OperationIds.systemPickFiles,
                        { multiselect: false },
                        10 * 60_000,
                      );
                      if (!result.cancelled && result.paths.length > 0) setInputPath(result.paths[0]);
                    } catch { /* best-effort */ }
                  }}
                  className="rounded-md border px-3 py-1 text-xs font-medium"
                  style={{ borderColor: 'var(--pcs-border)', color: 'var(--pcs-text)', background: 'var(--pcs-surface)' }}
                >
                  Elegir archivo
                </button>
              </div>

              {mode === 'encrypt' && (
                <label className="flex items-center gap-2 text-xs">
                  <input
                    type="checkbox"
                    checked={deleteOriginal}
                    onChange={(e) => setDeleteOriginal(e.target.checked)}
                  />
                  Borrar original tras encriptar
                </label>
              )}

              <button
                type="button"
                disabled={busy || !inputPath.trim()}
                onClick={runAction}
                className="rounded-md px-3 py-1.5 text-xs font-medium text-white"
                style={{ background: 'var(--pcs-accent)' }}
              >
                {busy ? 'Procesando…' : mode === 'encrypt' ? 'Encriptar con FIDO2' : 'Desencriptar con FIDO2'}
              </button>
            </div>
          )}
        </>
      )}

      {notice && (
        <div
          className="rounded-md border px-3 py-2 text-xs"
          style={{
            borderColor: notice.kind === 'success' ? 'var(--pcs-success)' : 'var(--pcs-danger)',
            color: notice.kind === 'success' ? 'var(--pcs-success)' : 'var(--pcs-danger)',
          }}
        >
          {notice.text}
        </div>
      )}

      <p className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
        Usa la extensión hmac-secret de FIDO2 para derivar una clave AES-256 a partir de la llave de hardware.
        Requiere una llave compatible (YubiKey 5+, Windows Hello). La clave derivada se usa como contraseña del
        cifrado AES-256 existente (.pcsuite-enc).
      </p>
    </section>
  );
}
