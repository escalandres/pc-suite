import { useCallback, useEffect, useState } from 'react';
import {
  FileKey,
  FolderOpen,
  FileSearch,
  RefreshCw,
  PenLine,
} from 'lucide-react';
import { bridge, BridgeError } from '../services/pcsuiteBridge';
import { OperationIds } from '../types/bridge';
import type {
  CertificateConvertResult,
  CertificateInspectResult,
  CsrGenerateResult,
  PgpExportKeyResult,
  PgpKeyItem,
  PgpListKeysResult,
  PgpSignFileResult,
  SystemPickFilesResult,
  X509GenerateResult,
} from '../types/bridge';

/**
 * Página "Certificados" (Task 019): X.509 autofirmado, inspección universal,
 * conversión PEM↔DER y firma PGP de archivos — todo BouncyCastle en proceso,
 * sin elevación y sin procesos externos.
 *
 * Seguridad: la clave privada NUNCA viaja por el bridge en claro; el backend
 * la escribe a Desktop\PCSuite-Certificados y aquí solo se muestra la ruta.
 * El passphrase de firma se envía una vez por llamada y no se registra.
 */

const READ_TIMEOUT = 30_000;
const X509_TIMEOUT = 120_000; // RSA 4096 tarda unos segundos en generar
const PICK_TIMEOUT = 10 * 60_000; // el diálogo nativo puede estar abierto minutos

type Tab = 'x509' | 'csr' | 'inspect' | 'convert' | 'sign';

interface Notice {
  kind: 'success' | 'error';
  text: string;
}

const X509_ALGORITHMS: { value: string; label: string }[] = [
  { value: 'rsa2048', label: 'RSA 2048' },
  { value: 'rsa4096', label: 'RSA 4096' },
  { value: 'ecdsa-p256', label: 'ECDSA P-256' },
  { value: 'ecdsa-p384', label: 'ECDSA P-384' },
  { value: 'ed25519', label: 'Ed25519' },
];

function errorMessage(error: unknown, fallback: string): string {
  return error instanceof BridgeError ? error.message : fallback;
}

/** Abre el Explorador con el archivo/carpeta resaltado. */
async function openInExplorer(path: string): Promise<void> {
  try {
    await bridge.call(OperationIds.explorerOpenPath, { path }, READ_TIMEOUT);
  } catch {
    // best-effort: no rompe nada si el explorador falla
  }
}

const inputStyle: React.CSSProperties = {
  background: 'var(--pcs-bg)',
  color: 'var(--pcs-text)',
  borderColor: 'var(--pcs-border)',
};

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label className="block text-sm">
      <span className="mb-1 block font-medium">{label}</span>
      {children}
    </label>
  );
}

export function Certificates() {
  const [tab, setTab] = useState<Tab>('x509');
  const [notice, setNotice] = useState<Notice | null>(null);

  // ------------------- X.509 -------------------
  const [algorithm, setAlgorithm] = useState('rsa4096');
  const [commonName, setCommonName] = useState('');
  const [organization, setOrganization] = useState('');
  const [country, setCountry] = useState('');
  const [sans, setSans] = useState('');
  const [validityDays, setValidityDays] = useState(365);
  const [generating, setGenerating] = useState(false);
  const [lastCert, setLastCert] = useState<X509GenerateResult | null>(null);

  const generateX509 = async () => {
    if (!commonName.trim() || generating) return;
    setGenerating(true);
    setNotice(null);
    try {
      const sanList = sans
        .split(',')
        .map((san) => san.trim())
        .filter((san) => san.length > 0);
      const result = await bridge.call<X509GenerateResult>(
        OperationIds.certX509Generate,
        {
          algorithm,
          commonName: commonName.trim(),
          organization: organization.trim() || undefined,
          country: country.trim() || undefined,
          subjectAltNames: sanList.length > 0 ? sanList : undefined,
          validityDays,
        },
        X509_TIMEOUT,
      );
      setLastCert(result);
      setNotice({ kind: 'success', text: 'Certificado generado y guardado en el escritorio.' });
    } catch (error) {
      setNotice({ kind: 'error', text: errorMessage(error, 'No se pudo generar el certificado.') });
    } finally {
      setGenerating(false);
    }
  };

  // ------------------- CSR (Task 19.5) -------------------
  const [csrAlgorithm, setCsrAlgorithm] = useState('rsa2048');
  const [csrCommonName, setCsrCommonName] = useState('');
  const [csrOrganization, setCsrOrganization] = useState('');
  const [csrCountry, setCsrCountry] = useState('');
  const [csrSans, setCsrSans] = useState('');
  const [csrBusy, setCsrBusy] = useState(false);
  const [lastCsr, setLastCsr] = useState<CsrGenerateResult | null>(null);

  const generateCsr = async () => {
    if (!csrCommonName.trim() || csrBusy) return;
    setCsrBusy(true);
    setNotice(null);
    try {
      const sanList = csrSans
        .split(',')
        .map((san) => san.trim())
        .filter((san) => san.length > 0);
      const result = await bridge.call<CsrGenerateResult>(
        OperationIds.certCsrGenerate,
        {
          commonName: csrCommonName.trim(),
          organization: csrOrganization.trim() || undefined,
          country: csrCountry.trim() || undefined,
          algorithm: csrAlgorithm,
          subjectAltNames: sanList.length > 0 ? sanList : undefined,
        },
        X509_TIMEOUT,
      );
      setLastCsr(result);
      setNotice({
        kind: 'success',
        text: 'CSR generado. Envíalo a tu CA; la clave privada quedó en el escritorio.',
      });
    } catch (error) {
      setNotice({ kind: 'error', text: errorMessage(error, 'No se pudo generar el CSR.') });
    } finally {
      setCsrBusy(false);
    }
  };

  // ------------------- Inspección -------------------
  const [inspectPath, setInspectPath] = useState('');
  const [inspecting, setInspecting] = useState(false);
  const [inspectResult, setInspectResult] = useState<CertificateInspectResult | null>(null);

  const pickAndInspect = async () => {
    setNotice(null);
    try {
      const picked = await bridge.call<SystemPickFilesResult>(
        OperationIds.systemPickFiles, undefined, PICK_TIMEOUT);
      if (picked.cancelled || picked.paths.length === 0) return;
      const path = picked.paths[0];
      setInspectPath(path);
      setInspectResult(null);
      setInspecting(true);
      const result = await bridge.call<CertificateInspectResult>(
        OperationIds.certInspect, { path }, READ_TIMEOUT);
      setInspectResult(result);
    } catch (error) {
      setNotice({ kind: 'error', text: errorMessage(error, 'No se pudo inspeccionar el archivo.') });
    } finally {
      setInspecting(false);
    }
  };

  // ------------------- Conversión -------------------
  const [convertPath, setConvertPath] = useState('');
  const [convertDirection, setConvertDirection] = useState<string>('pem2der');
  const [convertInputPassword, setConvertInputPassword] = useState('');
  const [convertOutputPassword, setConvertOutputPassword] = useState('');
  const [converting, setConverting] = useState(false);
  const [convertResult, setConvertResult] = useState<CertificateConvertResult | null>(null);

  /** Direcciones soportadas por cert.convert (inputFormat → outputFormat). */
  const CONVERT_DIRECTIONS: { value: string; label: string; from: string; to: string; needsOutPassword: boolean; needsInPassword: boolean }[] = [
    { value: 'pem2der', label: 'PEM → DER (binario)', from: 'pem', to: 'der', needsOutPassword: false, needsInPassword: false },
    { value: 'der2pem', label: 'DER → PEM (texto)', from: 'der', to: 'pem', needsOutPassword: false, needsInPassword: false },
    { value: 'pem2pfx', label: 'PEM (clave+cert) → PFX/PKCS#12', from: 'pem', to: 'pfx', needsOutPassword: true, needsInPassword: false },
    { value: 'pfx2pem', label: 'PFX/PKCS#12 → PEM (clave+cert)', from: 'pfx', to: 'pem', needsOutPassword: false, needsInPassword: true },
    { value: 'openssh2pem', label: 'Clave OpenSSH → PEM (PKCS#8)', from: 'openssh', to: 'pem', needsOutPassword: false, needsInPassword: false },
  ];
  const convertNeedsOutPassword = CONVERT_DIRECTIONS.find((d) => d.value === convertDirection)?.needsOutPassword ?? false;
  const convertNeedsInPassword = CONVERT_DIRECTIONS.find((d) => d.value === convertDirection)?.needsInPassword ?? false;

  const pickAndConvert = async () => {
    setNotice(null);
    try {
      const picked = await bridge.call<SystemPickFilesResult>(
        OperationIds.systemPickFiles, undefined, PICK_TIMEOUT);
      if (picked.cancelled || picked.paths.length === 0) return;
      setConvertPath(picked.paths[0]);
      setConvertResult(null);
    } catch (error) {
      setNotice({ kind: 'error', text: errorMessage(error, 'No se pudo abrir el selector.') });
    }
  };

  const runConvert = async () => {
    if (!convertPath || converting) return;
    setConverting(true);
    setNotice(null);
    try {
      const direction = CONVERT_DIRECTIONS.find((d) => d.value === convertDirection);
      if (!direction) return;
      const result = await bridge.call<CertificateConvertResult>(
        OperationIds.certConvert,
        {
          inputPath: convertPath,
          inputFormat: direction.from,
          outputFormat: direction.to,
          inputPassword: convertNeedsInPassword && convertInputPassword ? convertInputPassword : undefined,
          outputPassword: convertNeedsOutPassword && convertOutputPassword ? convertOutputPassword : undefined,
        },
        READ_TIMEOUT,
      );
      setConvertResult(result);
      setNotice({ kind: 'success', text: 'Conversión completada.' });
    } catch (error) {
      setNotice({ kind: 'error', text: errorMessage(error, 'La conversión falló.') });
    } finally {
      setConverting(false);
    }
  };

  // ------------------- Firma PGP -------------------
  const [keys, setKeys] = useState<PgpKeyItem[]>([]);
  const [signFingerprint, setSignFingerprint] = useState('');
  const [signPath, setSignPath] = useState('');
  const [signPassphrase, setSignPassphrase] = useState('');
  const [signing, setSigning] = useState(false);
  const [signResult, setSignResult] = useState<PgpSignFileResult | null>(null);

  // Generación de clave de firma (pgp.generateKey): el tab es autosuficiente —
  // el usuario crea su clave aquí mismo, sin salir de Certificados.
  const [genOpen, setGenOpen] = useState(false);
  const [genName, setGenName] = useState('');
  const [genEmail, setGenEmail] = useState('');
  const [genPassphrase, setGenPassphrase] = useState('');
  const [genBusy, setGenBusy] = useState(false);

  // Export de la clave pública (pgp.exportKey, includePrivate: false SIEMPRE).
  const [exporting, setExporting] = useState(false);
  const [publicArmor, setPublicArmor] = useState<string | null>(null);

  const loadKeys = useCallback(async () => {
    try {
      const result = await bridge.call<PgpListKeysResult>(
        OperationIds.pgpListKeys, undefined, READ_TIMEOUT);
      const privateKeys = (result.keys ?? []).filter((key) => key.hasPrivate);
      setKeys(privateKeys);
      setSignFingerprint((current) =>
        current || (privateKeys.length > 0 ? privateKeys[0].fingerprint : ''),
      );
    } catch {
      // keyring vacío o error: el tab muestra su propio aviso
      setKeys([]);
    }
  }, []);

  useEffect(() => {
    void loadKeys();
  }, [loadKeys]);

  const generateSigningKey = async () => {
    if (!genName.trim() || !genEmail.trim() || genBusy) return;
    setGenBusy(true);
    setNotice(null);
    try {
      const created = await bridge.call<PgpKeyItem>(
        OperationIds.pgpGenerateKey,
        {
          name: genName.trim(),
          email: genEmail.trim(),
          algorithm: 'ed25519',
          passphrase: genPassphrase || undefined,
        },
        X509_TIMEOUT,
      );
      setGenPassphrase('');
      setGenOpen(false);
      setPublicArmor(null);
      await loadKeys();
      setSignFingerprint(created.fingerprint);
      setNotice({
        kind: 'success',
        text: `Clave creada (${created.algorithm}): ${created.fingerprint}`,
      });
    } catch (error) {
      setNotice({ kind: 'error', text: errorMessage(error, 'No se pudo generar la clave.') });
    } finally {
      setGenBusy(false);
    }
  };

  const exportPublicKey = async () => {
    if (!signFingerprint || exporting) return;
    setExporting(true);
    setNotice(null);
    try {
      const result = await bridge.call<PgpExportKeyResult>(
        OperationIds.pgpExportKey,
        { fingerprint: signFingerprint, includePrivate: false },
        READ_TIMEOUT,
      );
      setPublicArmor(result.armoredKey);
      setNotice({
        kind: 'success',
        text: 'Clave pública exportada — comparte este bloque; quien la importe podrá verificar tus firmas.',
      });
    } catch (error) {
      setNotice({ kind: 'error', text: errorMessage(error, 'No se pudo exportar la clave.') });
    } finally {
      setExporting(false);
    }
  };

  const pickAndSign = async () => {
    setNotice(null);
    try {
      const picked = await bridge.call<SystemPickFilesResult>(
        OperationIds.systemPickFiles, undefined, PICK_TIMEOUT);
      if (picked.cancelled || picked.paths.length === 0) return;
      setSignPath(picked.paths[0]);
      setSignResult(null);
    } catch (error) {
      setNotice({ kind: 'error', text: errorMessage(error, 'No se pudo abrir el selector.') });
    }
  };

  const runSign = async () => {
    if (!signFingerprint || !signPath || signing) return;
    setSigning(true);
    setNotice(null);
    try {
      const result = await bridge.call<PgpSignFileResult>(
        OperationIds.certPgpSignFile,
        {
          fingerprint: signFingerprint,
          inputPath: signPath,
          passphrase: signPassphrase || undefined,
        },
        READ_TIMEOUT,
      );
      setSignResult(result);
      setSignPassphrase('');
      setNotice({ kind: 'success', text: 'Archivo firmado. La firma quedó junto al original.' });
    } catch (error) {
      setNotice({ kind: 'error', text: errorMessage(error, 'La firma falló.') });
    } finally {
      setSigning(false);
    }
  };

  const tabs: { id: Tab; label: string }[] = [
    { id: 'x509', label: 'X.509 autofirmado' },
    { id: 'csr', label: 'CSR (PKCS#10)' },
    { id: 'inspect', label: 'Inspeccionar' },
    { id: 'convert', label: 'Convertir' },
    { id: 'sign', label: 'Firmar con PGP' },
  ];

  return (
    <div className="space-y-6">
      <header className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold">Certificados</h1>
          <p className="mt-1 text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
            Genera certificados X.509, inspecciona cualquier formato, convierte
            PEM↔DER y firma archivos con tus claves PGP — todo en proceso, sin
            elevación.
          </p>
        </div>
        <FileKey size={24} aria-hidden style={{ color: 'var(--pcs-accent)' }} />
      </header>

      {/* Tabs */}
      <nav className="flex gap-1 border-b" style={{ borderColor: 'var(--pcs-border)' }} role="tablist">
        {tabs.map((item) => (
          <button
            key={item.id}
            type="button"
            role="tab"
            aria-selected={tab === item.id}
            onClick={() => setTab(item.id)}
            className="rounded-t-md border-b-2 px-4 py-2 text-sm font-medium transition-colors"
            style={
              tab === item.id
                ? {
                    borderColor: 'var(--pcs-accent)',
                    color: 'var(--pcs-accent)',
                  }
                : {
                    borderColor: 'transparent',
                    color: 'var(--pcs-text-muted)',
                  }
            }
          >
            {item.label}
          </button>
        ))}
      </nav>

      {notice && (
        <div
          className="rounded-md border px-4 py-3 text-sm"
          style={
            notice.kind === 'success'
              ? { borderColor: 'var(--pcs-success)', color: 'var(--pcs-success)' }
              : { borderColor: 'var(--pcs-danger)', color: 'var(--pcs-danger)' }
          }
          role={notice.kind === 'error' ? 'alert' : 'status'}
        >
          {notice.text}
        </div>
      )}

      {/* ============ X.509 ============ */}
      {tab === 'x509' && (
        <section
          className="rounded-lg border p-4"
          style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
        >
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Algoritmo">
              <select
                value={algorithm}
                onChange={(event) => setAlgorithm(event.target.value)}
                className="w-full rounded-md border px-3 py-2 text-sm"
                style={inputStyle}
              >
                {X509_ALGORITHMS.map((option) => (
                  <option key={option.value} value={option.value}>
                    {option.label}
                  </option>
                ))}
              </select>
            </Field>
            <Field label="Common Name *">
              <input
                type="text"
                value={commonName}
                onChange={(event) => setCommonName(event.target.value)}
                placeholder="localhost, *.aes-studio.dev…"
                className="w-full rounded-md border px-3 py-2 text-sm"
                style={inputStyle}
              />
            </Field>
            <Field label="Organización (opcional)">
              <input
                type="text"
                value={organization}
                onChange={(event) => setOrganization(event.target.value)}
                className="w-full rounded-md border px-3 py-2 text-sm"
                style={inputStyle}
              />
            </Field>
            <Field label="País (opcional, código de 2 letras)">
              <input
                type="text"
                value={country}
                onChange={(event) => setCountry(event.target.value)}
                maxLength={2}
                placeholder="ES"
                className="w-full rounded-md border px-3 py-2 text-sm"
                style={inputStyle}
              />
            </Field>
            <Field label="Subject Alternative Names (separados por coma)">
              <input
                type="text"
                value={sans}
                onChange={(event) => setSans(event.target.value)}
                placeholder="localhost, 192.168.1.10, admin@aes-studio.dev"
                className="w-full rounded-md border px-3 py-2 text-sm"
                style={inputStyle}
              />
            </Field>
            <Field label="Validez (días)">
              <input
                type="number"
                value={validityDays}
                onChange={(event) => setValidityDays(Number(event.target.value) || 365)}
                min={1}
                max={3650}
                className="w-full rounded-md border px-3 py-2 text-sm"
                style={inputStyle}
              />
            </Field>
          </div>

          <div className="mt-4 flex items-center gap-3">
            <button
              type="button"
              onClick={() => void generateX509()}
              disabled={!commonName.trim() || generating}
              className="flex items-center gap-2 rounded-md px-3 py-2 text-sm font-medium transition-colors disabled:opacity-40"
              style={{ background: 'var(--pcs-accent)', color: '#fff' }}
            >
              <RefreshCw size={14} aria-hidden className={generating ? 'animate-spin' : undefined} />
              {generating ? 'Generando…' : 'Generar certificado'}
            </button>
            <span className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
              Los archivos se guardan en Desktop\PCSuite-Certificados.
            </span>
          </div>

          {lastCert && (
            <div className="mt-4 space-y-3">
              <dl className="grid gap-2 text-sm sm:grid-cols-2">
                <div>
                  <dt className="font-medium">Subject</dt>
                  <dd style={{ color: 'var(--pcs-text-muted)' }}>{lastCert.subject}</dd>
                </div>
                <div>
                  <dt className="font-medium">Válido</dt>
                  <dd style={{ color: 'var(--pcs-text-muted)' }}>
                    {lastCert.validFrom} → {lastCert.validTo}
                  </dd>
                </div>
                <div>
                  <dt className="font-medium">Fingerprint SHA-256</dt>
                  <dd className="font-mono text-xs break-all" style={{ color: 'var(--pcs-text-muted)' }}>
                    {lastCert.fingerprint}
                  </dd>
                </div>
                <div>
                  <dt className="font-medium">Serial</dt>
                  <dd className="font-mono text-xs break-all" style={{ color: 'var(--pcs-text-muted)' }}>
                    {lastCert.serialNumber}
                  </dd>
                </div>
              </dl>

              <div>
                <div className="mb-1 flex items-center justify-between gap-2">
                  <span className="text-sm font-medium">Clave privada</span>
                  <button
                    type="button"
                    onClick={() => void openInExplorer(lastCert.privateKeyPath)}
                    className="flex items-center gap-1 text-xs"
                    style={{ color: 'var(--pcs-accent)' }}
                  >
                    <FolderOpen size={12} aria-hidden />
                    Mostrar en el Explorador
                  </button>
                </div>
                <code
                  className="block rounded-md border px-3 py-2 font-mono text-xs"
                  style={inputStyle}
                >
                  {lastCert.privateKeyPath}
                </code>
              </div>

              <details>
                <summary className="cursor-pointer text-sm font-medium">Ver certificado PEM</summary>
                <textarea
                  readOnly
                  value={lastCert.certificatePem}
                  rows={12}
                  className="mt-2 w-full rounded-md border p-3 font-mono text-xs"
                  style={inputStyle}
                  onFocus={(event) => event.target.select()}
                />
              </details>
            </div>
          )}
        </section>
      )}

      {/* ============ CSR (PKCS#10) ============ */}
      {tab === 'csr' && (
        <section
          className="rounded-lg border p-4"
          style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
        >
          <p className="mb-4 text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
            Genera la clave + el Certificate Signing Request para que una CA real
            (Let's Encrypt, DigiCert…) lo firme. Los SANs viajan en el atributo
            estándar <code>extensionRequest</code> — las CA de hoy SOLO honran los
            SANs, nunca el CN.
          </p>

          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Algoritmo">
              <select
                value={csrAlgorithm}
                onChange={(event) => setCsrAlgorithm(event.target.value)}
                className="w-full rounded-md border px-3 py-2 text-sm"
                style={inputStyle}
              >
                {X509_ALGORITHMS.map((option) => (
                  <option key={option.value} value={option.value}>
                    {option.label}
                  </option>
                ))}
              </select>
            </Field>
            <Field label="Common Name *">
              <input
                type="text"
                value={csrCommonName}
                onChange={(event) => setCsrCommonName(event.target.value)}
                placeholder="api.aes-studio.dev"
                className="w-full rounded-md border px-3 py-2 text-sm"
                style={inputStyle}
              />
            </Field>
            <Field label="Organización (opcional)">
              <input
                type="text"
                value={csrOrganization}
                onChange={(event) => setCsrOrganization(event.target.value)}
                className="w-full rounded-md border px-3 py-2 text-sm"
                style={inputStyle}
              />
            </Field>
            <Field label="País (opcional, código de 2 letras)">
              <input
                type="text"
                value={csrCountry}
                onChange={(event) => setCsrCountry(event.target.value)}
                maxLength={2}
                placeholder="ES"
                className="w-full rounded-md border px-3 py-2 text-sm"
                style={inputStyle}
              />
            </Field>
            <Field label="Subject Alternative Names (separados por coma)">
              <input
                type="text"
                value={csrSans}
                onChange={(event) => setCsrSans(event.target.value)}
                placeholder="api.aes-studio.dev, www.aes-studio.dev"
                className="w-full rounded-md border px-3 py-2 text-sm"
                style={inputStyle}
              />
            </Field>
          </div>

          <div className="mt-4 flex items-center gap-3">
            <button
              type="button"
              onClick={() => void generateCsr()}
              disabled={!csrCommonName.trim() || csrBusy}
              className="flex items-center gap-2 rounded-md px-3 py-2 text-sm font-medium transition-colors disabled:opacity-40"
              style={{ background: 'var(--pcs-accent)', color: '#fff' }}
            >
              <RefreshCw size={14} aria-hidden className={csrBusy ? 'animate-spin' : undefined} />
              {csrBusy ? 'Generando…' : 'Generar CSR'}
            </button>
            <span className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
              CSR + clave privada se guardan en Desktop\PCSuite-Certificados.
            </span>
          </div>

          {lastCsr && (
            <div className="mt-4 space-y-3">
              <div>
                <div className="mb-1 flex items-center justify-between gap-2">
                  <span className="text-sm font-medium">Clave privada</span>
                  <button
                    type="button"
                    onClick={() => void openInExplorer(lastCsr.privateKeyPath)}
                    className="flex items-center gap-1 text-xs"
                    style={{ color: 'var(--pcs-accent)' }}
                  >
                    <FolderOpen size={12} aria-hidden />
                    Mostrar en el Explorador
                  </button>
                </div>
                <code
                  className="block rounded-md border px-3 py-2 font-mono text-xs"
                  style={inputStyle}
                >
                  {lastCsr.privateKeyPath}
                </code>
              </div>

              <details>
                <summary className="cursor-pointer text-sm font-medium">Ver CSR (PEM)</summary>
                <textarea
                  readOnly
                  value={lastCsr.csrPem}
                  rows={12}
                  className="mt-2 w-full rounded-md border p-3 font-mono text-xs"
                  style={inputStyle}
                  onFocus={(event) => event.target.select()}
                />
              </details>
              <p className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
                Copia el bloque PEM y envíalo a tu CA. La clave privada NUNCA
                sale de este equipo.
              </p>
            </div>
          )}
        </section>
      )}

      {/* ============ Inspeccionar ============ */}
      {tab === 'inspect' && (
        <section
          className="rounded-lg border p-4"
          style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
        >
          <div className="flex items-center gap-3">
            <button
              type="button"
              onClick={() => void pickAndInspect()}
              disabled={inspecting}
              className="flex items-center gap-2 rounded-md px-3 py-2 text-sm font-medium transition-colors disabled:opacity-40"
              style={{ background: 'var(--pcs-accent)', color: '#fff' }}
            >
              <FileSearch size={14} aria-hidden />
              {inspecting ? 'Inspeccionando…' : 'Elegir archivo e inspeccionar'}
            </button>
            {inspectPath && (
              <code className="truncate font-mono text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
                {inspectPath}
              </code>
            )}
          </div>
          <p className="mt-2 text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
            Reconoce: X.509 (PEM/DER), CSR, claves privadas (PKCS#8/RSA/EC), claves
            PGP públicas/privadas y claves públicas SSH.
          </p>

          {inspectResult && (
            <div className="mt-4 space-y-3">
              <div className="flex items-center gap-2">
                <span
                  className="rounded-full border px-3 py-1 text-xs font-medium"
                  style={{ borderColor: 'var(--pcs-accent)', color: 'var(--pcs-accent)' }}
                >
                  {inspectResult.kindDescription}
                </span>
                {inspectResult.fingerprint && (
                  <code className="font-mono text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
                    SHA-256: {inspectResult.fingerprint}
                  </code>
                )}
              </div>
              {Object.keys(inspectResult.details).length > 0 && (
                <dl className="grid gap-2 text-sm sm:grid-cols-2">
                  {Object.entries(inspectResult.details).map(([key, value]) => (
                    <div key={key} className="min-w-0">
                      <dt className="font-medium">{key}</dt>
                      <dd className="break-all" style={{ color: 'var(--pcs-text-muted)' }}>
                        {value}
                      </dd>
                    </div>
                  ))}
                </dl>
              )}
            </div>
          )}
        </section>
      )}

      {/* ============ Convertir ============ */}
      {tab === 'convert' && (
        <section
          className="rounded-lg border p-4"
          style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
        >
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Dirección">
              <select
                value={convertDirection}
                onChange={(event) => setConvertDirection(event.target.value)}
                className="w-full rounded-md border px-3 py-2 text-sm"
                style={inputStyle}
              >
                {CONVERT_DIRECTIONS.map((direction) => (
                  <option key={direction.value} value={direction.value}>
                    {direction.label}
                  </option>
                ))}
              </select>
            </Field>
            <Field label="Archivo de entrada">
              <button
                type="button"
                onClick={() => void pickAndConvert()}
                className="flex w-full items-center gap-2 rounded-md border px-3 py-2 text-sm"
                style={inputStyle}
              >
                <FolderOpen size={14} aria-hidden />
                <span className="truncate">
                  {convertPath || 'Elegir archivo…'}
                </span>
              </button>
            </Field>
            {convertNeedsInPassword && (
              <Field label="Password del PFX de entrada">
                <input
                  type="password"
                  value={convertInputPassword}
                  onChange={(event) => setConvertInputPassword(event.target.value)}
                  autoComplete="off"
                  className="w-full rounded-md border px-3 py-2 text-sm"
                  style={inputStyle}
                />
              </Field>
            )}
            {convertNeedsOutPassword && (
              <Field label="Password del PFX de salida">
                <input
                  type="password"
                  value={convertOutputPassword}
                  onChange={(event) => setConvertOutputPassword(event.target.value)}
                  autoComplete="off"
                  className="w-full rounded-md border px-3 py-2 text-sm"
                  style={inputStyle}
                />
              </Field>
            )}
          </div>

          <div className="mt-4 flex items-center gap-3">
            <button
              type="button"
              onClick={() => void runConvert()}
              disabled={!convertPath || converting}
              className="flex items-center gap-2 rounded-md px-3 py-2 text-sm font-medium transition-colors disabled:opacity-40"
              style={{ background: 'var(--pcs-accent)', color: '#fff' }}
            >
              <RefreshCw size={14} aria-hidden className={converting ? 'animate-spin' : undefined} />
              {converting ? 'Convirtiendo…' : 'Convertir'}
            </button>
            <span className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
              El resultado se guarda en Desktop\PCSuite-Certificados.
              {convertDirection === 'pem2pfx' && ' El PEM debe contener clave + certificado (bundle fullchain).'}
            </span>
          </div>

          {convertResult && (
            <div className="mt-4">
              <div className="mb-1 flex items-center justify-between gap-2">
                <span className="text-sm font-medium">Archivo convertido ({convertResult.outputFormat})</span>
                <button
                  type="button"
                  onClick={() => void openInExplorer(convertResult.outputPath)}
                  className="flex items-center gap-1 text-xs"
                  style={{ color: 'var(--pcs-accent)' }}
                >
                  <FolderOpen size={12} aria-hidden />
                  Mostrar en el Explorador
                </button>
              </div>
              <code
                className="block rounded-md border px-3 py-2 font-mono text-xs"
                style={inputStyle}
              >
                {convertResult.outputPath}
              </code>
            </div>
          )}
        </section>
      )}

      {/* ============ Firmar con PGP ============ */}
      {tab === 'sign' && (
        <section
          className="rounded-lg border p-4"
          style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
        >
          {/* ---- Generación de clave (autosuficiente: pgp.generateKey) ---- */}
          <div className="mb-4">
            <button
              type="button"
              onClick={() => { setGenOpen((open) => !open); setPublicArmor(null); }}
              className="flex items-center gap-1.5 text-sm font-medium"
              style={{ color: 'var(--pcs-accent)' }}
            >
              {genOpen ? '− Cerrar' : '+ Generar clave de firma…'}
            </button>

            {(genOpen || keys.length === 0) && (
              <div
                className="mt-3 rounded-md border p-4"
                style={{ background: 'var(--pcs-bg)', borderColor: 'var(--pcs-border)' }}
              >
                <p className="mb-3 text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
                  {keys.length === 0
                    ? 'Aún no hay claves con material privado. Crea tu clave de firma — queda en el keyring de la app (%APPDATA%\\PCSuite\\pgp) y la privada NUNCA sale de este equipo.'
                    : 'La nueva clave se añade al keyring de la app. Algoritmo ed25519 (recomendada).'}
                </p>
                <div className="grid gap-4 sm:grid-cols-3">
                  <Field label="Nombre *">
                    <input
                      type="text"
                      value={genName}
                      onChange={(event) => setGenName(event.target.value)}
                      placeholder="Ana García"
                      className="w-full rounded-md border px-3 py-2 text-sm"
                      style={inputStyle}
                    />
                  </Field>
                  <Field label="Email *">
                    <input
                      type="email"
                      value={genEmail}
                      onChange={(event) => setGenEmail(event.target.value)}
                      placeholder="ana@ejemplo.com"
                      className="w-full rounded-md border px-3 py-2 text-sm"
                      style={inputStyle}
                    />
                  </Field>
                  <Field label="Passphrase (opcional)">
                    <input
                      type="password"
                      value={genPassphrase}
                      onChange={(event) => setGenPassphrase(event.target.value)}
                      autoComplete="new-password"
                      className="w-full rounded-md border px-3 py-2 text-sm"
                      style={inputStyle}
                    />
                  </Field>
                </div>
                <div className="mt-3 flex items-center gap-3">
                  <button
                    type="button"
                    onClick={() => void generateSigningKey()}
                    disabled={!genName.trim() || !genEmail.includes('@') || genBusy}
                    className="flex items-center gap-2 rounded-md px-3 py-2 text-sm font-medium transition-colors disabled:opacity-40"
                    style={{ background: 'var(--pcs-accent)', color: '#fff' }}
                  >
                    <RefreshCw size={14} aria-hidden className={genBusy ? 'animate-spin' : undefined} />
                    {genBusy ? 'Generando…' : 'Crear clave'}
                  </button>
                  <span className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
                    Sin elevación; ed25519 se genera en segundos.
                  </span>
                </div>
              </div>
            )}
          </div>

          {/* ---- Firma con clave existente ---- */}
          {keys.length === 0 ? (
            <p className="text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
              Cuando tengas al menos una clave, aquí podrás firmar archivos y
              exportar tu clave pública.
            </p>
          ) : (
            <>
              <div className="grid gap-4 sm:grid-cols-2">
                <Field label="Clave firmante">
                  <select
                    value={signFingerprint}
                    onChange={(event) => { setSignFingerprint(event.target.value); setPublicArmor(null); }}
                    className="w-full rounded-md border px-3 py-2 text-sm"
                    style={inputStyle}
                  >
                    {keys.map((key) => (
                      <option key={key.fingerprint} value={key.fingerprint}>
                        {key.userId || key.name} ({key.algorithm})
                      </option>
                    ))}
                  </select>
                </Field>
                <Field label="Passphrase (solo si la clave la tiene)">
                  <input
                    type="password"
                    value={signPassphrase}
                    onChange={(event) => setSignPassphrase(event.target.value)}
                    autoComplete="off"
                    className="w-full rounded-md border px-3 py-2 text-sm"
                    style={inputStyle}
                  />
                </Field>
                <Field label="Archivo a firmar">
                  <button
                    type="button"
                    onClick={() => void pickAndSign()}
                    className="flex w-full items-center gap-2 rounded-md border px-3 py-2 text-sm"
                    style={inputStyle}
                  >
                    <FolderOpen size={14} aria-hidden />
                    <span className="truncate">{signPath || 'Elegir archivo…'}</span>
                  </button>
                </Field>
              </div>

              <div className="mt-4 flex flex-wrap items-center gap-3">
                <button
                  type="button"
                  onClick={() => void runSign()}
                  disabled={!signFingerprint || !signPath || signing}
                  className="flex items-center gap-2 rounded-md px-3 py-2 text-sm font-medium transition-colors disabled:opacity-40"
                  style={{ background: 'var(--pcs-accent)', color: '#fff' }}
                >
                  <PenLine size={14} aria-hidden />
                  {signing ? 'Firmando…' : 'Firmar archivo'}
                </button>
                <button
                  type="button"
                  onClick={() => void exportPublicKey()}
                  disabled={!signFingerprint || exporting}
                  className="flex items-center gap-2 rounded-md border px-3 py-2 text-sm font-medium transition-colors disabled:opacity-40"
                  style={inputStyle}
                >
                  <RefreshCw size={14} aria-hidden className={exporting ? 'animate-spin' : undefined} />
                  {exporting ? 'Exportando…' : 'Exportar clave pública'}
                </button>
                <span className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
                  Firma detached armoured (.sig) junto al original.
                </span>
              </div>

              {publicArmor && (
                <div className="mt-4">
                  <p className="mb-1 text-sm font-medium">
                    Tu clave pública — compártela; quien la importe podrá verificar tus firmas
                  </p>
                  <textarea
                    readOnly
                    value={publicArmor}
                    rows={10}
                    className="w-full rounded-md border p-3 font-mono text-xs"
                    style={inputStyle}
                    onFocus={(event) => event.target.select()}
                  />
                  <p className="mt-1 text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
                    Es un bloque de solo lectura: pulsa dentro, Ctrl+A y Ctrl+C para copiarlo.
                  </p>
                </div>
              )}
            </>
          )}

          {signResult && (
            <div className="mt-4">
              <div className="mb-1 flex items-center justify-between gap-2">
                <span className="text-sm font-medium">Firma generada</span>
                <button
                  type="button"
                  onClick={() => void openInExplorer(signResult.signaturePath)}
                  className="flex items-center gap-1 text-xs"
                  style={{ color: 'var(--pcs-accent)' }}
                >
                  <FolderOpen size={12} aria-hidden />
                  Mostrar en el Explorador
                </button>
              </div>
              <code
                className="block rounded-md border px-3 py-2 font-mono text-xs"
                style={inputStyle}
              >
                {signResult.signaturePath}
              </code>
            </div>
          )}
        </section>
      )}
    </div>
  );
}
