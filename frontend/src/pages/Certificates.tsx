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
  PgpVerifyFileResult,
  SystemPickFilesResult,
  X509GenerateResult,
} from '../types/bridge';
import { useTranslation } from '../hooks/useTranslation';

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

const X509_ALGORITHMS: { value: string; labelKey: string }[] = [
  { value: 'rsa2048', labelKey: 'pages.certificates.algoRsa2048' },
  { value: 'rsa4096', labelKey: 'pages.certificates.algoRsa4096' },
  { value: 'ecdsa-p256', labelKey: 'pages.certificates.algoEcdsaP256' },
  { value: 'ecdsa-p384', labelKey: 'pages.certificates.algoEcdsaP384' },
  { value: 'ed25519', labelKey: 'pages.certificates.algoEd25519' },
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
  const { t } = useTranslation();
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
      setNotice({ kind: 'success', text: t('pages.certificates.certGenerated') });
    } catch (error) {
      setNotice({ kind: 'error', text: errorMessage(error, t('pages.certificates.genCertError')) });
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
        text: t('pages.certificates.csrGenerated'),
      });
    } catch (error) {
      setNotice({ kind: 'error', text: errorMessage(error, t('pages.certificates.csrError')) });
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
      setNotice({ kind: 'error', text: errorMessage(error, t('pages.certificates.inspectError')) });
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
  const CONVERT_DIRECTIONS: { value: string; labelKey: string; from: string; to: string; needsOutPassword: boolean; needsInPassword: boolean }[] = [
    { value: 'pem2der', labelKey: 'pages.certificates.convertPem2der', from: 'pem', to: 'der', needsOutPassword: false, needsInPassword: false },
    { value: 'der2pem', labelKey: 'pages.certificates.convertDer2pem', from: 'der', to: 'pem', needsOutPassword: false, needsInPassword: false },
    { value: 'pem2pfx', labelKey: 'pages.certificates.convertPem2pfx', from: 'pem', to: 'pfx', needsOutPassword: true, needsInPassword: false },
    { value: 'pfx2pem', labelKey: 'pages.certificates.convertPfx2pem', from: 'pfx', to: 'pem', needsOutPassword: false, needsInPassword: true },
    { value: 'openssh2pem', labelKey: 'pages.certificates.convertOpenSsh2pem', from: 'openssh', to: 'pem', needsOutPassword: false, needsInPassword: false },
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
      setNotice({ kind: 'error', text: errorMessage(error, t('pages.certificates.openSelectorError')) });
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
      setNotice({ kind: 'success', text: t('pages.certificates.conversionCompleted') });
    } catch (error) {
      setNotice({ kind: 'error', text: errorMessage(error, t('pages.certificates.conversionFailed')) });
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
  /** Task 028: algoritmo y tamaño de la clave PGP — el backend ya soportaba
   *  rsa (2048/3072/4096) y ed25519; la UI solo fijaba ed25519. */
  const [genAlgorithm, setGenAlgorithm] = useState<'ed25519' | 'rsa'>('ed25519');
  const [genBits, setGenBits] = useState(4096);

  // Export de la clave pública (pgp.exportKey, includePrivate: false SIEMPRE).
  const [exporting, setExporting] = useState(false);
  const [publicArmor, setPublicArmor] = useState<string | null>(null);

  // Import de clave pública de terceros (pgp.importKey): el paso del VERIFICADOR —
  // sin la clave del firmante en el keyring, la verificación no puede hacerse.
  const [importOpen, setImportOpen] = useState(false);
  const [importArmor, setImportArmor] = useState('');
  const [importing, setImporting] = useState(false);

  // Verificación de firma detached: archivo original + .sig → cert.pgp.verifyFile.
  const [verifyOpen, setVerifyOpen] = useState(false);
  const [verifyPath, setVerifyPath] = useState('');
  const [verifySigPath, setVerifySigPath] = useState('');
  const [verifying, setVerifying] = useState(false);
  const [verifyOutcome, setVerifyOutcome] = useState<PgpVerifyFileResult | null>(null);

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
          algorithm: genAlgorithm,
          // El tamaño solo aplica a RSA; Ed25519 es una curva fija (el
          // backend ignora los bits en ese caso).
          bits: genAlgorithm === 'rsa' ? genBits : undefined,
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
        text: t('pages.certificates.keyCreated', { algorithm: created.algorithm, fingerprint: created.fingerprint }),
      });
    } catch (error) {
      setNotice({ kind: 'error', text: errorMessage(error, t('pages.certificates.keyGenError')) });
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
        text: t('pages.certificates.publicKeyExported'),
      });
    } catch (error) {
      setNotice({ kind: 'error', text: errorMessage(error, t('pages.certificates.exportKeyError')) });
    } finally {
      setExporting(false);
    }
  };

  const importPublicKey = async () => {
    const armor = importArmor.trim();
    if (!armor.includes('-----BEGIN PGP') || importing) return;
    setImporting(true);
    setNotice(null);
    try {
      const imported = await bridge.call<PgpKeyItem>(
        OperationIds.pgpImportKey,
        { armoredKey: armor },
        READ_TIMEOUT,
      );
      setImportArmor('');
      setImportOpen(false);
      setNotice({
        kind: 'success',
        text: t('pages.certificates.publicKeyImported', { userId: imported.userId, fingerprint: imported.fingerprint }),
      });
    } catch (error) {
      setNotice({ kind: 'error', text: errorMessage(error, t('pages.certificates.importKeyError')) });
    } finally {
      setImporting(false);
    }
  };

  /** Selector nativo para UN archivo; devuelve null si se canceló. */
  const pickOneFile = useCallback(async (): Promise<string | null> => {
    const picked = await bridge.call<SystemPickFilesResult>(
      OperationIds.systemPickFiles, undefined, PICK_TIMEOUT);
    if (picked.cancelled || picked.paths.length === 0) return null;
    return picked.paths[0];
  }, []);

  const pickVerifyOriginal = async () => {
    setNotice(null);
    try {
      const path = await pickOneFile();
      if (path) { setVerifyPath(path); setVerifyOutcome(null); }
    } catch (error) {
      setNotice({ kind: 'error', text: errorMessage(error, t('pages.certificates.openSelectorError')) });
    }
  };

  const pickVerifySignature = async () => {
    setNotice(null);
    try {
      const path = await pickOneFile();
      if (path) { setVerifySigPath(path); setVerifyOutcome(null); }
    } catch (error) {
      setNotice({ kind: 'error', text: errorMessage(error, t('pages.certificates.openSelectorError')) });
    }
  };

  const verifySignature = async () => {
    if (!verifyPath || !verifySigPath || verifying) return;
    setVerifying(true);
    setNotice(null);
    try {
      const outcome = await bridge.call<PgpVerifyFileResult>(
        OperationIds.certPgpVerifyFile,
        { inputPath: verifyPath, signaturePath: verifySigPath },
        READ_TIMEOUT,
      );
      setVerifyOutcome(outcome);
    } catch (error) {
      setNotice({ kind: 'error', text: errorMessage(error, t('pages.certificates.verifyError')) });
    } finally {
      setVerifying(false);
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
      setNotice({ kind: 'error', text: errorMessage(error, t('pages.certificates.openSelectorError')) });
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
      setNotice({ kind: 'success', text: t('pages.certificates.fileSigned') });
    } catch (error) {
      setNotice({ kind: 'error', text: errorMessage(error, t('pages.certificates.signError')) });
    } finally {
      setSigning(false);
    }
  };

  const tabs: { id: Tab; labelKey: string }[] = [
    { id: 'x509', labelKey: 'pages.certificates.tabX509' },
    { id: 'csr', labelKey: 'pages.certificates.tabCsr' },
    { id: 'inspect', labelKey: 'pages.certificates.tabInspect' },
    { id: 'convert', labelKey: 'pages.certificates.tabConvert' },
    { id: 'sign', labelKey: 'pages.certificates.tabSign' },
  ];

  return (
    <div className="space-y-6">
      <header className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold">{t('sidebar.certificates')}</h1>
          <p className="mt-1 text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
            {t('pages.certificates.subtitle')}
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
            {t(item.labelKey)}
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
            <Field label={t('pages.certificates.fieldAlgorithm')}>
              <select
                value={algorithm}
                onChange={(event) => setAlgorithm(event.target.value)}
                className="w-full rounded-md border px-3 py-2 text-sm"
                style={inputStyle}
              >
                {X509_ALGORITHMS.map((option) => (
                  <option key={option.value} value={option.value}>
                    {t(option.labelKey)}
                  </option>
                ))}
              </select>
            </Field>
            <Field label={t('pages.certificates.fieldCommonName')}>
              <input
                type="text"
                value={commonName}
                onChange={(event) => setCommonName(event.target.value)}
                placeholder="localhost, *.aes-studio.dev…"
                className="w-full rounded-md border px-3 py-2 text-sm"
                style={inputStyle}
              />
            </Field>
            <Field label={t('pages.certificates.fieldOrganization')}>
              <input
                type="text"
                value={organization}
                onChange={(event) => setOrganization(event.target.value)}
                className="w-full rounded-md border px-3 py-2 text-sm"
                style={inputStyle}
              />
            </Field>
            <Field label={t('pages.certificates.fieldCountry')}>
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
            <Field label={t('pages.certificates.fieldSans')}>
              <input
                type="text"
                value={sans}
                onChange={(event) => setSans(event.target.value)}
                placeholder="localhost, 192.168.1.10, admin@aes-studio.dev"
                className="w-full rounded-md border px-3 py-2 text-sm"
                style={inputStyle}
              />
            </Field>
            <Field label={t('pages.certificates.fieldValidity')}>
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
              {generating ? t('pages.certificates.generating') : t('pages.certificates.generateCert')}
            </button>
            <span className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
              {t('pages.certificates.filesSaved')}
            </span>
          </div>

          {lastCert && (
            <div className="mt-4 space-y-3">
              <dl className="grid gap-2 text-sm sm:grid-cols-2">
                <div>
                  <dt className="font-medium">{t('pages.certificates.detailSubject')}</dt>
                  <dd style={{ color: 'var(--pcs-text-muted)' }}>{lastCert.subject}</dd>
                </div>
                <div>
                  <dt className="font-medium">{t('pages.certificates.detailValid')}</dt>
                  <dd style={{ color: 'var(--pcs-text-muted)' }}>
                    {lastCert.validFrom} → {lastCert.validTo}
                  </dd>
                </div>
                <div>
                  <dt className="font-medium">{t('pages.certificates.detailFingerprint')}</dt>
                  <dd className="font-mono text-xs break-all" style={{ color: 'var(--pcs-text-muted)' }}>
                    {lastCert.fingerprint}
                  </dd>
                </div>
                <div>
                  <dt className="font-medium">{t('pages.certificates.detailSerial')}</dt>
                  <dd className="font-mono text-xs break-all" style={{ color: 'var(--pcs-text-muted)' }}>
                    {lastCert.serialNumber}
                  </dd>
                </div>
              </dl>

              <div>
                <div className="mb-1 flex items-center justify-between gap-2">
                  <span className="text-sm font-medium">{t('pages.certificates.fieldPrivateKey')}</span>
                  <button
                    type="button"
                    onClick={() => void openInExplorer(lastCert.privateKeyPath)}
                    className="flex items-center gap-1 text-xs"
                    style={{ color: 'var(--pcs-accent)' }}
                  >
                    <FolderOpen size={12} aria-hidden />
                    {t('pages.certificates.showInExplorer')}
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
                <summary className="cursor-pointer text-sm font-medium">{t('pages.certificates.showCertPem')}</summary>
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
            <Field label={t('pages.certificates.fieldAlgorithm')}>
              <select
                value={csrAlgorithm}
                onChange={(event) => setCsrAlgorithm(event.target.value)}
                className="w-full rounded-md border px-3 py-2 text-sm"
                style={inputStyle}
              >
                {X509_ALGORITHMS.map((option) => (
                  <option key={option.value} value={option.value}>
                    {t(option.labelKey)}
                  </option>
                ))}
              </select>
            </Field>
            <Field label={t('pages.certificates.fieldCommonName')}>
              <input
                type="text"
                value={csrCommonName}
                onChange={(event) => setCsrCommonName(event.target.value)}
                placeholder="api.aes-studio.dev"
                className="w-full rounded-md border px-3 py-2 text-sm"
                style={inputStyle}
              />
            </Field>
            <Field label={t('pages.certificates.fieldOrganization')}>
              <input
                type="text"
                value={csrOrganization}
                onChange={(event) => setCsrOrganization(event.target.value)}
                className="w-full rounded-md border px-3 py-2 text-sm"
                style={inputStyle}
              />
            </Field>
            <Field label={t('pages.certificates.fieldCountry')}>
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
            <Field label={t('pages.certificates.fieldSans')}>
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
              {csrBusy ? t('pages.certificates.generating') : t('pages.certificates.generateCsr')}
            </button>
            <span className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
              {t('pages.certificates.csrFilesSaved')}
            </span>
          </div>

          {lastCsr && (
            <div className="mt-4 space-y-3">
              <div>
                <div className="mb-1 flex items-center justify-between gap-2">
                  <span className="text-sm font-medium">{t('pages.certificates.fieldPrivateKey')}</span>
                  <button
                    type="button"
                    onClick={() => void openInExplorer(lastCsr.privateKeyPath)}
                    className="flex items-center gap-1 text-xs"
                    style={{ color: 'var(--pcs-accent)' }}
                  >
                    <FolderOpen size={12} aria-hidden />
                    {t('pages.certificates.showInExplorer')}
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
                <summary className="cursor-pointer text-sm font-medium">{t('pages.certificates.showCsrPem')}</summary>
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
                {t('pages.certificates.pemCopyNote')}
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
              {inspecting ? t('pages.certificates.inspecting') : t('pages.certificates.btnInspect')}
            </button>
            {inspectPath && (
              <code className="truncate font-mono text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
                {inspectPath}
              </code>
            )}
          </div>
          <p className="mt-2 text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
            {t('pages.certificates.inspectRecognizes')}
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
            <Field label={t('pages.certificates.fieldDirection')}>
              <select
                value={convertDirection}
                onChange={(event) => setConvertDirection(event.target.value)}
                className="w-full rounded-md border px-3 py-2 text-sm"
                style={inputStyle}
              >
                {CONVERT_DIRECTIONS.map((direction) => (
                  <option key={direction.value} value={direction.value}>
                    {t(direction.labelKey)}
                  </option>
                ))}
              </select>
            </Field>
            <Field label={t('pages.certificates.fieldInputFile')}>
              <button
                type="button"
                onClick={() => void pickAndConvert()}
                className="flex w-full items-center gap-2 rounded-md border px-3 py-2 text-sm"
                style={inputStyle}
              >
                <FolderOpen size={14} aria-hidden />
                <span className="truncate">
                  {convertPath || t('pages.certificates.btnPickFile')}
                </span>
              </button>
            </Field>
            {convertNeedsInPassword && (
              <Field label={t('pages.certificates.fieldInputPassword')}>
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
              <Field label={t('pages.certificates.fieldOutputPassword')}>
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
              {converting ? t('pages.certificates.converting') : t('pages.certificates.convert')}
            </button>
            <span className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
              {t('pages.certificates.convertFilesSaved')}
              {convertDirection === 'pem2pfx' && t('pages.certificates.pemBundleNote')}
            </span>
          </div>

          {convertResult && (
            <div className="mt-4">
              <div className="mb-1 flex items-center justify-between gap-2">
                <span className="text-sm font-medium">{t('pages.certificates.convertedFile', { format: convertResult.outputFormat })}</span>
                <button
                  type="button"
                  onClick={() => void openInExplorer(convertResult.outputPath)}
                  className="flex items-center gap-1 text-xs"
                  style={{ color: 'var(--pcs-accent)' }}
                >
                  <FolderOpen size={12} aria-hidden />
                  {t('pages.certificates.showInExplorer')}
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
              {genOpen ? t('pages.certificates.closeKeyGen') : t('pages.certificates.generateSigningKey')}
            </button>

            {(genOpen || keys.length === 0) && (
              <div
                className="mt-3 rounded-md border p-4"
                style={{ background: 'var(--pcs-bg)', borderColor: 'var(--pcs-border)' }}
              >
                <p className="mb-3 text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
                  {keys.length === 0
                    ? 'Aún no hay claves con material privado. Crea tu clave de firma — queda en el keyring de la app (%APPDATA%\\PCSuite\\pgp) y la privada NUNCA sale de este equipo.'
                    : 'La nueva clave se añade al keyring de la app. Ed25519 es la recomendada; RSA queda para compatibilidad con sistemas que no aceptan curvas modernas.'}
                </p>
                <div className="grid gap-4 sm:grid-cols-3">
                  <Field label={t('pages.certificates.fieldName')}>
                    <input
                      type="text"
                      value={genName}
                      onChange={(event) => setGenName(event.target.value)}
                      placeholder="Ana García"
                      className="w-full rounded-md border px-3 py-2 text-sm"
                      style={inputStyle}
                    />
                  </Field>
                  <Field label={t('pages.certificates.fieldEmail')}>
                    <input
                      type="email"
                      value={genEmail}
                      onChange={(event) => setGenEmail(event.target.value)}
                      placeholder="ana@ejemplo.com"
                      className="w-full rounded-md border px-3 py-2 text-sm"
                      style={inputStyle}
                    />
                  </Field>
                  <Field label={t('pages.certificates.fieldPassphrase')}>
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
                {/* Task 028: algoritmo + tamaño. Ed25519 es la recomendada;
                    RSA (2048/3072/4096) por compatibilidad clásica — el
                    backend ya generaba ambas desde el Task 009. */}
                <div className="mt-3 grid gap-4 sm:grid-cols-2">
                  <Field label={t('pages.certificates.fieldAlgorithm')}>
                    <select
                      value={genAlgorithm}
                      onChange={(event) => setGenAlgorithm(event.target.value as 'ed25519' | 'rsa')}
                      className="w-full rounded-md border px-3 py-2 text-sm"
                      style={inputStyle}
                    >
                      <option value="ed25519">{t('pages.certificates.algoEd25519Recommended')}</option>
                      <option value="rsa">{t('pages.certificates.algoRsa')}</option>
                    </select>
                  </Field>
                  <Field label={t('pages.certificates.fieldKeySize')}>
                    <select
                      value={String(genBits)}
                      onChange={(event) => setGenBits(Number(event.target.value))}
                      disabled={genAlgorithm !== 'rsa'}
                      className="w-full rounded-md border px-3 py-2 text-sm disabled:opacity-50"
                      style={inputStyle}
                    >
                      <option value="2048">2048 bits</option>
                      <option value="3072">3072 bits</option>
                      <option value="4096">4096 bits</option>
                    </select>
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
                    {genBusy ? t('pages.certificates.generating') : t('pages.certificates.createKey')}
                  </button>
                  <span className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
                    Sin elevación. Ed25519 se genera en segundos; RSA 4096 puede
                    tardar unos segundos más.
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
                <Field label={t('pages.certificates.fieldSigningKey')}>
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
                <Field label={t('pages.certificates.fieldPassphrase')}>
                  <input
                    type="password"
                    value={signPassphrase}
                    onChange={(event) => setSignPassphrase(event.target.value)}
                    autoComplete="off"
                    className="w-full rounded-md border px-3 py-2 text-sm"
                    style={inputStyle}
                  />
                </Field>
                <Field label={t('pages.certificates.fieldFileToSign')}>
                  <button
                    type="button"
                    onClick={() => void pickAndSign()}
                    className="flex w-full items-center gap-2 rounded-md border px-3 py-2 text-sm"
                    style={inputStyle}
                  >
                    <FolderOpen size={14} aria-hidden />
                    <span className="truncate">{signPath || t('pages.certificates.btnPickFile')}</span>
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
                  {signing ? t('pages.certificates.signing') : t('pages.certificates.signFile')}
                </button>
                <button
                  type="button"
                  onClick={() => void exportPublicKey()}
                  disabled={!signFingerprint || exporting}
                  className="flex items-center gap-2 rounded-md border px-3 py-2 text-sm font-medium transition-colors disabled:opacity-40"
                  style={inputStyle}
                >
                  <RefreshCw size={14} aria-hidden className={exporting ? 'animate-spin' : undefined} />
                  {exporting ? t('pages.certificates.exporting') : t('pages.certificates.exportPublicKey')}
                </button>
                <span className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
                  {t('pages.certificates.signDetachedNote')}
                </span>
              </div>

              {publicArmor && (
                <div className="mt-4">
                  <p className="mb-1 text-sm font-medium">
                    {t('pages.certificates.publicKeyNote')}
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
                    {t('pages.certificates.readonlyNote')}
                  </p>
                </div>
              )}
            </>
          )}

          {signResult && (
            <div className="mt-4">
              <div className="mb-1 flex items-center justify-between gap-2">
                <span className="text-sm font-medium">{t('pages.certificates.signatureGenerated')}</span>
                <button
                  type="button"
                  onClick={() => void openInExplorer(signResult.signaturePath)}
                  className="flex items-center gap-1 text-xs"
                  style={{ color: 'var(--pcs-accent)' }}
                >
                  <FolderOpen size={12} aria-hidden />
                  {t('pages.certificates.showInExplorer')}
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

          {/* ---- Importar clave pública (el paso del VERIFICADOR) ---- */}
          <div className="mt-6 border-t pt-4" style={{ borderColor: 'var(--pcs-border)' }}>
            <button
              type="button"
              onClick={() => { setImportOpen((open) => !open); setImportArmor(''); }}
              className="flex items-center gap-1.5 text-sm font-medium"
              style={{ color: 'var(--pcs-accent)' }}
            >
              {importOpen ? t('pages.certificates.closeKeyGen') : t('pages.certificates.importPublicKey')}
            </button>
            <p className="mt-1 text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
              {t('pages.certificates.importPubKeyNote')}
            </p>

            {importOpen && (
              <div className="mt-3">
                <textarea
                  value={importArmor}
                  onChange={(event) => setImportArmor(event.target.value)}
                  rows={8}
                  placeholder={'-----BEGIN PGP PUBLIC KEY BLOCK-----\n…pega aquí el bloque completo…\n-----END PGP PUBLIC KEY BLOCK-----'}
                  className="w-full rounded-md border p-3 font-mono text-xs"
                  style={inputStyle}
                  spellCheck={false}
                />
                <div className="mt-2 flex items-center gap-3">
                  <button
                    type="button"
                    onClick={() => void importPublicKey()}
                    disabled={!importArmor.includes('-----BEGIN PGP') || importing}
                    className="flex items-center gap-2 rounded-md px-3 py-2 text-sm font-medium transition-colors disabled:opacity-40"
                    style={{ background: 'var(--pcs-accent)', color: '#fff' }}
                  >
                    <RefreshCw size={14} aria-hidden className={importing ? 'animate-spin' : undefined} />
                    {importing ? t('pages.certificates.importing') : t('pages.certificates.importKey')}
                  </button>
                  <span className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
                    {t('pages.certificates.importPublicOnly')}
                  </span>
                </div>
              </div>
            )}
          </div>

          {/* ---- Verificar firma de un archivo ---- */}
          <div className="mt-6 border-t pt-4" style={{ borderColor: 'var(--pcs-border)' }}>
            <button
              type="button"
              onClick={() => { setVerifyOpen((open) => !open); setVerifyOutcome(null); }}
              className="flex items-center gap-1.5 text-sm font-medium"
              style={{ color: 'var(--pcs-accent)' }}
            >
              {verifyOpen ? t('pages.certificates.closeKeyGen') : t('pages.certificates.verifyFile')}
            </button>
            <p className="mt-1 text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
              {t('pages.certificates.verifyNote')}
            </p>

            {verifyOpen && (
              <div className="mt-3">
                <div className="grid gap-4 sm:grid-cols-2">
                  <Field label={t('pages.certificates.fieldOriginalFile')}>
                    <button
                      type="button"
                      onClick={() => void pickVerifyOriginal()}
                      className="flex w-full items-center gap-2 rounded-md border px-3 py-2 text-sm"
                      style={inputStyle}
                    >
                      <FolderOpen size={14} aria-hidden />
                      <span className="truncate">{verifyPath || 'Elegir archivo…'}</span>
                    </button>
                  </Field>
                  <Field label={t('pages.certificates.fieldSignatureFile')}>
                    <button
                      type="button"
                      onClick={() => void pickVerifySignature()}
                      className="flex w-full items-center gap-2 rounded-md border px-3 py-2 text-sm"
                      style={inputStyle}
                    >
                      <FolderOpen size={14} aria-hidden />
                      <span className="truncate">{verifySigPath || t('pages.certificates.btnPickFile')}</span>
                    </button>
                  </Field>
                </div>

                <div className="mt-3 flex items-center gap-3">
                  <button
                    type="button"
                    onClick={() => void verifySignature()}
                    disabled={!verifyPath || !verifySigPath || verifying}
                    className="flex items-center gap-2 rounded-md px-3 py-2 text-sm font-medium transition-colors disabled:opacity-40"
                    style={{ background: 'var(--pcs-accent)', color: '#fff' }}
                  >
                    <RefreshCw size={14} aria-hidden className={verifying ? 'animate-spin' : undefined} />
                    {verifying ? t('pages.certificates.verifying') : t('pages.certificates.verify')}
                  </button>
                </div>

                {verifyOutcome && (
                  <div
                    className="mt-3 rounded-md border px-4 py-3 text-sm"
                    role={verifyOutcome.valid ? 'status' : 'alert'}
                    style={
                      verifyOutcome.valid
                        ? {
                            borderColor: 'var(--pcs-success)',
                            color: 'var(--pcs-success)',
                            background: 'color-mix(in srgb, var(--pcs-success) 8%, transparent)',
                          }
                        : {
                            borderColor: 'var(--pcs-danger)',
                            color: 'var(--pcs-danger)',
                            background: 'color-mix(in srgb, var(--pcs-danger) 8%, transparent)',
                          }
                    }
                  >
                    {verifyOutcome.valid ? (
                      <p>
                        ✓ {t('pages.certificates.signatureValid', { signer: verifyOutcome.signer?.userId || 'el firmante' })}
                        <strong>{verifyOutcome.signer?.userId || 'el firmante'}</strong>.
                        {verifyOutcome.signer && (
                          <span className="ml-1 font-mono text-xs" style={{ opacity: 0.8 }}>
                            ({verifyOutcome.signer.fingerprint})
                          </span>
                        )}
                      </p>
                    ) : (
                      <p>
                        ✗ Firma inválida. {verifyOutcome.detail}
                      </p>
                    )}
                  </div>
                )}
              </div>
            )}
          </div>
        </section>
      )}
    </div>
  );
}
