/**
 * Contratos del protocolo puente (espejo de PCSuite.Contracts).
 * Mantener sincronizados con src/PCSuite.Contracts.
 */

/* =====================================================================
   OperationIds — Identificadores de operaciones permitidas para el puente.
   El frontend SOLO puede invocar operaciones registradas aquí.
   Ejecución arbitraria de comandos NUNCA está expuesta.
   ===================================================================== */

export interface BridgeRequest {
  id: string;
  operation: string;
  payload?: unknown;
}

export interface BridgeError {
  code: string;
  message: string;
}

export interface BridgeResponse {
  id: string;
  success: boolean;
  result?: unknown;
  error?: BridgeError;
}

export interface BridgeEvent {
  event: string;
  payload?: unknown;
}

/* =====================================================================
   System info
   ===================================================================== */

/** Payload del evento app.ready. */
export interface AppReadyEvent {
  appVersion: string;
  startedAtUtc: string;
}

/* =====================================================================
   OperationIds — Constantes string para los identificadores de operación.
   ===================================================================== */

export const OperationIds = {
  systemInfo: 'system.info',
  systemPickFiles: 'system.pickFiles',
  /** Diálogo nativo de UNA carpeta (Task 026): escaneo personalizado de ClamAV. */
  systemPickFolder: 'system.pickFolder',
  /** Portapapeles nativo del host — navigator.clipboard falla en WebView2 sin permiso. */
  systemWriteClipboard: 'system.writeClipboard',
  toolsList: 'tools.list',
  toolsSearch: 'tools.search',
  toolExecute: 'tool.execute',
  developerList: 'developer.list',
  developerCheckUpdate: 'developer.checkUpdate',
  developerUpdate: 'developer.update',
  developerLaunch: 'developer.launch',
  wingetDetect: 'winget.detect',
  wingetSearch: 'winget.search',
  wingetList: 'winget.list',
  wingetUpdates: 'winget.updates',
  wingetInstall: 'winget.install',
  wingetUpgrade: 'winget.upgrade',
  wingetUpgradeAll: 'winget.upgradeAll',
  wingetUninstall: 'winget.uninstall',
  wslStatus: 'wsl.status',
  wslList: 'wsl.list',
  wslUpdate: 'wsl.update',
  wslShutdown: 'wsl.shutdown',
  wslStart: 'wsl.start',
  clamavStatus: 'clamav.status',
  clamavInstall: 'clamav.install',
  clamavUpdateDefinitions: 'clamav.updateDefinitions',
  clamavQuickScan: 'clamav.quickScan',
  clamavQuickTargets: 'clamav.quickTargets',
  clamavFullScan: 'clamav.fullScan',
  clamavCustomScan: 'clamav.customScan',
  clamavHistory: 'clamav.history',
  clamavActiveScan: 'clamav.activeScan',
  cleanerScan: 'cleaner.scan',
  cleanerClean: 'cleaner.clean',
  securityGetStatus: 'security.getStatus',
  pluginList: 'plugin.list',
  pluginCheckUpdate: 'plugin.checkUpdate',
  pluginLaunch: 'plugin.launch',
  pluginStatus: 'plugin.status',
  explorerOpenPath: 'explorer.openPath',
  explorerOpenUrl: 'explorer.openUrl',
  jobGet: 'job.get',
  jobCancel: 'job.cancel',
  blazorToggle: 'blazor.toggle',
  localShareStatus: 'localshare.status',
  localShareStart: 'localshare.start',
  localShareStop: 'localshare.stop',
  localShareDevices: 'localshare.devices',
  localSharePair: 'localshare.pair',
  localShareSend: 'localshare.send',
  localShareSendMessage: 'localshare.sendMessage',
  localShareAccept: 'localshare.accept',
  localShareReject: 'localshare.reject',
  localShareTransfers: 'localshare.transfers',
  localShareCancelTransfer: 'localshare.cancelTransfer',
  localShareConfig: 'localshare.config',
  localShareClearHistory: 'localshare.clearHistory',
  localShareRevoke: 'localshare.revoke',
  localShareFirewallStatus: 'localshare.firewallStatus',
  localShareFirewallRule: 'localshare.firewallRule',
  localSharePairAccept: 'localshare.pairAccept',
  localSharePairReject: 'localshare.pairReject',
  localSharePairCancel: 'localshare.pairCancel',
  localSharePauseTransfer: 'localshare.pauseTransfer',
  localShareResumeTransfer: 'localshare.resumeTransfer',
  updateCheck: 'update.check',
  updateDownload: 'update.download',
  updateInstall: 'update.install',
  uninstallList: 'uninstall.list',
  uninstallDiscover: 'uninstall.discover',
  uninstallRunOfficial: 'uninstall.runOfficial',
  uninstallCleanup: 'uninstall.cleanup',
  certX509Generate: 'cert.x509.generate',
  certInspect: 'cert.inspect',
  certConvert: 'cert.convert',
  certPgpSignFile: 'cert.pgp.signFile',
  certCsrGenerate: 'cert.csr.generate',
  certPgpVerifyFile: 'cert.pgp.verifyFile',
  pgpListKeys: 'pgp.listKeys',
  pgpGenerateKey: 'pgp.generateKey',
  pgpExportKey: 'pgp.exportKey',
  pgpImportKey: 'pgp.importKey',
} as const;

export const EventIds = {
  appReady: 'app.ready',
  /** Task 026.3: rutas reales del drop de archivos capturado por el HOST nativo. */
  systemFilesDropped: 'system.filesDropped',
  clamavFreshclamProgress: 'clamav.freshclam.progress',
  clamavScanProgress: 'clamav.scan.progress',
  jobEvent: 'job.event',
  localShareDeviceChanged: 'localshare.device-changed',
  localShareIncomingRequest: 'localshare.incoming-request',
  localShareTransferProgress: 'localshare.transfer-progress',
  localShareTransferFinished: 'localshare.transfer-finished',
  localSharePairRequested: 'localshare.pair-request',
  localSharePairFinished: 'localshare.pair-finished',
} as const;

/** Resultado de system.pickFiles: rutas absolutas del diálogo nativo de Windows. */
export interface SystemPickFilesResult {
  paths: string[];
  cancelled: boolean;
}

/** Resultado de system.pickFolder (Task 026): UNA carpeta absoluta del diálogo nativo. */
export interface SystemPickFolderResult {
  cancelled: boolean;
  path?: string | null;
}

/** Evento system.filesDropped (Task 026.3): rutas absolutas del drop de
 *  archivos del SO, capturadas por el host nativo (la página no puede verlas). */
export interface SystemFilesDroppedPayload {
  paths: string[];
}

/* =====================================================================
   Tool metadata
   ===================================================================== */

/** Parámetro de una tool: la UI genera el formulario desde esta lista. */
export interface ToolParameter {
  /** Clave del payload JSON que rellena el campo. */
  name: string;
  label: string;
  type: 'text' | 'textArea' | 'number' | 'boolean' | 'select';
  required: boolean;
  default?: unknown;
  placeholder?: string | null;
  helpText?: string | null;
  /** Valores admitidos (solo select). */
  options: string[];
  /** Límites (solo number). */
  min?: number | null;
  max?: number | null;
}

/** Metadato de una tool tal como lo devuelve el nativo. */
export interface ToolInfo {
  id: string;
  moduleId: string;
  name: string;
  description: string;
  keywords: string[];
  /** Ejemplo de payload para precargar la UI genérica. */
  exampleInput?: unknown;
  /** Formulario de configuración de la tool (vacío → editor JSON genérico). */
  parameters: ToolParameter[];
}

/* =====================================================================
   Tool collection results
   ===================================================================== */

/** Resultado de tools.list / tools.search. */
export interface ToolCollectionResult {
  tools: ToolInfo[];
}

/** Resultado de tool.execute. */
export interface ToolExecuteResult {
  toolId: string;
  output?: unknown;
}

/* =====================================================================
   Developer tools
   ===================================================================== */

/** Una herramienta de desarrollador tal como la devuelve developer.list. */
export interface DeveloperToolInfo {
  /** Id estable (node, npm, pnpm, bun, claude-code). */
  id: string;
  name: string;
  isInstalled: boolean;
  /** Ausente si es desconocida (WhenWritingNull del puente). */
  version?: string | null;
  /** Ausente si no está instalada. */
  path?: string | null;
}

/** Resultado de developer.list. */
export interface DeveloperCollectionResult {
  tools: DeveloperToolInfo[];
}

/** Resultado de developer.checkUpdate. */
export interface UpdateCheckResult {
  toolId: string;
  currentVersion?: string | null;
  /** Ausente si la comprobación degradó (sin red / sin fuente). */
  latestVersion?: string | null;
  updateAvailable: boolean;
  /** false: sin fuente de updates conocida o comprobación degradada. */
  supported: boolean;
}

/* =====================================================================
   WinGet
   ===================================================================== */

/** Un paquete en winget.search / winget.list / winget.updates. */
export interface WinGetPackageResult {
  id: string;
  name: string;
  version: string;
  /** Solo winget.updates. */
  available?: string | null;
  /** Solo winget.search. */
  match?: string | null;
}

/** Resultado de winget.search / winget.list / winget.updates. */
export interface WinGetPackageCollectionResult {
  packages: WinGetPackageResult[];
}

/** Resultado de las mutaciones winget (install/upgrade/upgradeAll/uninstall).
 * Un fallo del CLI NO es un error del puente: success:true con succeeded:false
 * y el detalle controlado en `detail`.
 */
export interface WinGetMutationResult {
  /** Ausente en upgradeAll. */
  packageId?: string | null;
  succeeded: boolean;
  exitCode: number;
  elevated: boolean;
  /** Última línea saneada de winget; ausente si no hay salida. */
  detail?: string | null;
}

/** Resultado de winget.detect. */
export interface WinGetDetectResult {
  /** Id del paquete. */
  packageId: string;
  /** Nombre del paquete. */
  name: string;
  /** Versión actual. */
  version: string;
  /** ¿Hay actualización disponible? */
  updateAvailable: boolean;
  /** Versión disponible. */
  latestVersion?: string | null;
  /** Fuente de la detección. */
  source?: string | null;
  /** ¿Está winget instalado en el sistema. */
  installed: boolean;
  /** Ruta de instalación del App Installer / winget. */
  path?: string | null;
  /** ¿El proceso tiene privilegios de administrador. */
  isAdministrator: boolean;
}

/* =====================================================================
   WSL
   ===================================================================== */

/** Resultado de wsl.status. */
export interface WslStatusResult {
  installed: boolean;
  path?: string | null;
  /** running | stopped | unknown. */
  state: string;
  version?: string | null;
  defaultDistro?: string | null;
}

/** Una distribución en wsl.list. */
export interface WslDistroResult {
  name: string;
  /** Estado en crudo (Running / Stopped). */
  state: string;
  /** Plataforma WSL (1 o 2). */
  version: number;
}

/** Resultado de wsl.list. */
export interface WslListResult {
  distributions: WslDistroResult[];
}

/** Resultado de las mutaciones wsl (update/shutdown/start). Igual que winget:
 * un fallo del CLI vuelve como resultado estructurado, nunca como error del
 * puente.
 */
export interface WslMutationResult {
  succeeded: boolean;
  exitCode: number;
  elevated: boolean;
  detail?: string | null;
}

/* =====================================================================
   ClamAV
   ===================================================================== */

/** Resultado de clamav.status. */
export interface ClamAvStatusResult {
  installed: boolean;
  /** Ruta resolvida de clamscan.exe; ausente si no está instalado. */
  path?: string | null;
  /** Versión del motor (clamscan --version); ausente si es desconocida. */
  version?: string | null;
  /** Build de definiciones conocidas; ausente si es desconocido. */
  definitions?: string | null;
  /** false: falta freshclam.exe junto a clamscan (sin actualización de BD). */
  canUpdateDefinitions: boolean;
}

/** Resultado de clamav.install / clamav.updateDefinitions. Igual que winget y
 * wsl: un fallo del CLI vuelve como resultado estructurado (succeeded:false +
 * detail controlado), nunca como error del puente.
 */
export interface ClamAvMutationResult {
  succeeded: boolean;
  exitCode: number;
  /** true: corrió en el proceso elevado (UAC aceptado). */
  elevated: boolean;
  detail?: string | null;
}

/** Una amenaza detectada por clamscan. */
export interface ClamAvThreatResult {
  /** Ruta del archivo marcado como infectado. */
  file: string;
  /** Firma de virus (p. ej. Eicar-Test-Signature). */
  threat: string;
}

/** Respuesta de iniciar un escaneo clamav.*: el job queda corriendo. */
export interface ClamAvScanStartResult {
  jobId: string;
}

/** Respuesta de clamav.activeScan: el escaneo en curso (Task 025.1) — para
 *  re-adjuntar la UI tras recargar/navegar. Vacío si no hay escaneo activo. */
export interface ClamAvActiveScanResult {
  jobId?: string;
  kind?: 'quick' | 'full' | 'custom' | null;
  startedAt?: string | null;
  progress?: number | null;
}

/** Evento job.event: ciclo de vida de un job (resultado en statusChanged). */
export interface JobEventPayload {
  jobId: string;
  type: 'statusChanged' | 'output' | 'progress';
  /** running | completed | failed | cancelled (en statusChanged). */
  status?: string | null;
  /** 0..1 (en progress). */
  progress?: number | null;
  error?: string | null;
  /** Resultado estructurado al completarse (según el tipo de job). */
  result?: ClamAvScanResult | UpdateDownloadJobResult | null;
}

/** Snapshot de job.get. */
export interface JobSnapshot {
  id: string;
  type: string;
  status: 'running' | 'completed' | 'failed' | 'cancelled';
  startedAt?: string | null;
  finishedAt?: string | null;
  exitCode?: number | null;
  progress?: number | null;
  error?: string | null;
  result?: ClamAvScanResult | null;
}

/** Resultado de job.cancel. */
export interface JobCancelResult {
  cancelled: boolean;
}

/* =====================================================================
   Task 017 — Actualizaciones
   ===================================================================== */

/** Resultado de update.check (app). */
export interface AppUpdateCheckResult {
  /** ¿Hay versión mayor que la instalada? Un fallo de red = false (sin drama). */
  available: boolean;
  currentVersion: string;
  latestVersion: string;
  /** Tamaño del instalador a descargar (bytes). */
  sizeBytes: number;
  releaseNotesUrl?: string | null;
  /** Mensaje humano si el check falló; null si todo fue bien. */
  error?: string | null;
  /**
   * Aviso TOFU (Task 17.6): la clave de firma del manifiesto CAMBIÓ desde la
   * primera verificación. La UI DEBE mostrarlo prominentemente; null = normal.
   */
  keyRotationWarning?: string | null;
}

/** Resultado de update.download: el job queda corriendo (job.event). */
export interface UpdateDownloadResult {
  jobId: string;
}

/** Payload del evento job.event para update.download (statusChanged → completed). */
export interface UpdateDownloadJobResult {
  /** Ruta del archivo verificado — vuelve en el payload de update.install. */
  path: string;
}

/* =====================================================================
   Task 019 — Certificados
   ===================================================================== */

/** Resultado de cert.x509.generate. */
export interface X509GenerateResult {
  /** Certificado en PEM (la UI lo muestra; la clave NUNCA viaja en claro). */
  certificatePem: string;
  /** Ruta del archivo con la clave privada — el usuario la abre desde el explorador. */
  privateKeyPath: string;
  pfxPath?: string | null;
  fingerprint: string;
  serialNumber: string;
  subject: string;
  validFrom: string;
  validTo: string;
  algorithm: string;
}

/** Resultado de cert.inspect: reconoce X.509, CSR, claves, PGP y SSH. */
export interface CertificateInspectResult {
  kind: string;
  kindDescription: string;
  fingerprint?: string | null;
  details: Record<string, string>;
}

/** Resultado de cert.convert (PEM↔DER por ahora). */
export interface CertificateConvertResult {
  outputPath: string;
  outputFormat: string;
}

/** Resultado de cert.pgp.signFile: firma detached armoured junto al archivo. */
export interface PgpSignFileResult {
  signaturePath: string;
  fingerprint: string;
}

/** Resultado de cert.csr.generate: CSR PKCS#10 + clave privada en archivo. */
export interface CsrGenerateResult {
  /** CSR en PEM (-----BEGIN CERTIFICATE REQUEST-----) listo para la CA. */
  csrPem: string;
  /** Ruta del archivo con la clave privada generada. */
  privateKeyPath: string;
  /** Ruta del archivo .pem con el CSR guardado en disco. */
  csrPath?: string | null;
}

/** Entrada de pgp.listKeys: claves del keyring propio (%APPDATA%\PCSuite\pgp). */
export interface PgpKeyItem {
  fingerprint: string;
  keyId: string;
  userId: string;
  name: string;
  email: string;
  algorithm: string;
  bits: number;
  createdAt: string;
  expiresAt?: string | null;
  hasPrivate: boolean;
  canEncrypt: boolean;
}

/** Resultado de pgp.listKeys. */
export interface PgpListKeysResult {
  keys: PgpKeyItem[];
}

/**
 * Resultado de pgp.exportKey. La PRIVADA solo sale si se pidió
 * explícitamente (includePrivate) — la UI de Certificados solo exporta
 * públicas.
 */
export interface PgpExportKeyResult {
  armoredKey: string;
  isPrivate: boolean;
}

/**
 * Resultado de cert.pgp.verifyFile: una firma inválida es un RESULTADO
 * legítimo (valid=false con detail), no un error del puente.
 */
export interface PgpVerifyFileResult {
  valid: boolean;
  detail?: string | null;
  /** Identidad del firmante cuando su clave pública está en el keyring. */
  signer?: {
    fingerprint: string;
    userId: string;
    name: string;
    email: string;
    algorithm: string;
  } | null;
}

/* =====================================================================
   Task 020 — Desinstalador forzado
   ===================================================================== */

/** App instalada según el registro de desinstalación de Windows (la fuente
 * de appwiz; incluye apps que winget no ve). */
export interface InstalledApp {
  registryKeyPath: string;
  displayName: string;
  publisher: string;
  displayVersion: string;
  installLocation: string;
  uninstallString: string;
  msiGuid?: string | null;
  estimatedSizeKb: number;
  isSystemComponent: boolean;
}

/** Resultado de uninstall.list. */
export interface InstalledAppListResult {
  apps: InstalledApp[];
}

/** Ítem del plan de eliminación. */
export interface RemovalPlanItem {
  /** process | service | scheduledtask | folder | registrykey */
  kind: string;
  /** derived (quirúrgico, pre-marcado) | candidate (heurístico, sin marcar) */
  tier: string;
  identifier: string;
  detail: string;
}

/** Plan de eliminación revisable (uninstall.discover). */
export interface RemovalPlan {
  app: InstalledApp;
  hasOfficialUninstaller: boolean;
  items: RemovalPlanItem[];
}

/** Resultado de uninstall.runOfficial: el asistente quedó lanzado. */
export interface UninstallOfficialResult {
  launched: boolean;
  launcher: string;
}

/** Desenlace de un ítem de la limpieza. */
export interface RemovalItemResult {
  kind: string;
  identifier: string;
  succeeded: boolean;
  detail?: string | null;
}

/** Resultado de uninstall.cleanup (vía elevado). */
export interface UninstallCleanupResult {
  results: RemovalItemResult[];
  requiresRestart: boolean;
}

/** Resultado de clamav.quickTargets: carpetas del escaneo rápido (resueltas
 * por el nativo — Descargas cambia de nombre según el idioma de Windows). */
export interface ClamAvQuickTargetsResult {
  targets: string[];
}

/* =====================================================================
   Local Share types (Task 016)
   ===================================================================== */

export interface LocalShareStatusResult {
  status: 'stopped' | 'starting' | 'running' | 'error';
  port: number;
  displayName?: string | null;
  discoveryEnabled: boolean;
  autoStartService: boolean;
  downloadDirectory?: string | null;
  error?: string | null;
}

export interface SharedDeviceResult {
  deviceId: string;
  displayName: string;
  trust: 'unknown' | 'paired' | 'revoked';
  address?: string | null;
  port: number;
  lastSeenAt?: string | null;
  /** ¿Se anunció en los últimos 45 s? Heurística de la UI. */
  online: boolean;
}

export interface SharedDeviceCollectionResult {
  devices: SharedDeviceResult[];
}

export interface LocalSharePairResult {
  /** Identificador del intento (correlaciona con el evento pair-finished). */
  requestId: string;
  /** Código de comparación: DEBE coincidir con el del otro equipo. */
  code: string;
  peerDisplayName: string;
}

export interface LocalShareSendResult {
  transferId: string;
}

export interface ShareTransferItemResult {
  itemId: string;
  relativePath: string;
  expectedSize: number;
  expectedHash?: string | null;
  status: string;
  transferredBytes: number;
  /** Task 016.8: el ítem es un mensaje de texto, no un archivo. */
  isMessage?: boolean;
  /** Contenido del mensaje (receptor, ítem completado). */
  messageText?: string | null;
}

export interface ShareTransferResult {
  transferId: string;
  direction: 'sending' | 'receiving';
  peerDisplayName: string;
  status: string;
  createdAt: string;
  startedAt?: string | null;
  completedAt?: string | null;
  totalBytes: number;
  transferredBytes: number;
  itemCount: number;
  /** Task 16.8: la transferencia ES un mensaje — la lista la pinta como "Mensaje". */
  isMessage?: boolean;
  destinationSummary?: string | null;
  errorCode?: string | null;
  errorMessage?: string | null;
  items: ShareTransferItemResult[];
}

export interface ShareTransferCollectionResult {
  transfers: ShareTransferResult[];
}

export interface LocalShareConfigResult {
  displayName: string;
  downloadDirectory: string;
  port: number;
  discoveryEnabled: boolean;
  autoAcceptFromPaired: boolean;
  autoStartService: boolean;
  maxSimultaneousTransfers: number;
  maxBandwidthBytesPerSecond: number;
  maxTransferSizeBytes: number;
}

export interface LocalShareTransferProgressPayload {
  transferId: string;
  transferredBytes: number;
  totalBytes: number;
  bytesPerSecond: number;
  itemsCompleted: number;
  itemsTotal: number;
  currentFile?: string | null;
}

export interface LocalShareIncomingRequestPayload {
  transferId: string;
  peerDisplayName: string;
  itemCount: number;
  totalBytes: number;
  items: ShareTransferItemResult[];
}

export interface LocalShareTransferFinishedPayload {
  transferId: string;
  status: string;
  /** Bytes transferidos en el desenlace (carrera de transferencias diminutas). */
  transferredBytes: number;
  errorMessage?: string | null;
  totalBytes: number;
}

export interface LocalShareDeviceChangedPayload {
  deviceId: string;
  displayName: string;
  trust: string;
  address?: string | null;
  port: number;
}

/** Evento localshare.pair-request (lado receptor): otro equipo quiere emparejarse. */
export interface LocalSharePairRequestedPayload {
  requestId: string;
  peerDeviceId: string;
  peerDisplayName: string;
  /** Código derivado de las dos huellas TLS: comparar con la otra pantalla. */
  code: string;
}

/** Evento localshare.pair-finished (lado iniciador). */
export interface LocalSharePairFinishedPayload {
  requestId: string;
  /** accepted | rejected | failed */
  outcome: string;
  deviceId?: string | null;
  displayName?: string | null;
  errorMessage?: string | null;
}

/** Resultado de localshare.firewallStatus (solo lectura). */
export interface LocalShareFirewallStatusResult {
  ruleExists: boolean | null;
  portMatches: boolean;
  discoveryPortMatches: boolean;
  port: number;
}

/** Resultado de localshare.firewallRule (tras UAC puntual). */
export interface LocalShareFirewallRuleResult {
  created: boolean;
  detail: string;
}

/** Resultado de clamav.quickScan / clamav.fullScan / clamav.customScan. */
export interface ClamAvScanResult {
  succeeded: boolean;
  /** 0: limpio, 1: amenazas, 2: parcial. */
  exitCode: number;
  scannedFiles: number;
  scannedDirectories: number;
  /** Total exacto aunque la lista threats venga recortada. */
  infectedFiles: number;
  engineVersion?: string | null;
  knownViruses?: number | null;
  /** Detalle controlado (parcial/fallo); ausente si no hay. */
  detail?: string | null;
  threats: ClamAvThreatResult[];
  /** Duración del escaneo en segundos. */
  duration?: number | null;
  /** Ruta al archivo de log generado. */
  logFile?: string | null;
}

/** Una entrada de clamav.history (la más reciente primero). */
export interface ClamAvHistoryEntryResult {
  /** Marca de tiempo UTC en ISO 8601. */
  timestamp: string;
  /** quick | full | custom. */
  kind: string;
  /** Ruta escaneada en customScan; ausente en quick/full. */
  path?: string | null;
  succeeded: boolean;
  scannedFiles: number;
  infectedFiles: number;
  detail?: string | null;
  threats: ClamAvThreatResult[];
}

/** Resultado de clamav.history. */
export interface ClamAvHistoryResult {
  entries: ClamAvHistoryEntryResult[];
}

/** Progreso de actualización de definiciones ClamAV (freshclam). */
export interface ClamAvFreshclamProgressPayload {
  /** Archivo actual que se está descargando/procesando (p. ej. "main.cvd", "daily.cvd"). */
  currentFile?: string | null;
  /** Porcentaje de descarga del archivo actual (0-100). null si no aplica. */
  percent?: number | null;
  /** Bytes descargados del archivo actual. */
  downloadedBytes?: number | null;
  /** Total de bytes del archivo actual (si se conoce). */
  totalBytes?: number | null;
  /** Velocidad de descarga en bytes/segundo. */
  bytesPerSecond?: number | null;
  /** Mensaje de estado legible (p. ej. "Descargando main.cvd...", "Verificando firmas..."). */
  statusText?: string | null;
  /** Indica si la operación ha finalizado (éxito o fallo). */
  isComplete: boolean;
  /** Si isComplete=true, indica si la actualización tuvo éxito. */
  succeeded?: boolean | null;
  /** Mensaje de detalle si falló o terminó. */
  detail?: string | null;
}

/** Progreso de escaneo ClamAV (clamscan). */
export interface ClamAvScanProgressPayload {
  /** Archivo actual siendo escaneado. */
  currentFile?: string | null;
  /** Directorio actual siendo escaneado. */
  currentDirectory?: string | null;
  /** Archivos escaneados hasta el momento. */
  scannedFiles: number;
  /** Directorios escaneados hasta el momento. */
  scannedDirectories: number;
  /** Amenazas encontradas hasta el momento. */
  infectedFiles: number;
  /** Mensaje de estado legible (p. ej. "Escaneando C:\Users\X\Downloads\..."). */
  statusText?: string | null;
  /** Indica si la operación ha finalizado (éxito o fallo). */
  isComplete: boolean;
  /** Si isComplete=true, indica si el escaneo tuvo éxito. */
  succeeded?: boolean | null;
  /** Mensaje de detalle si falló o terminó. */
  detail?: string | null;
  /** Tipo de escaneo (quick, full, custom). */
  scanKind?: string | null;
  /** Porcentaje de progreso estimado (0-100). Ausente si no se puede calcular. */
  percent?: number | null;
}

/* =====================================================================
   Cleaner
   ===================================================================== */

/** Categoría escaneada por cleaner.scan (raíz fija resuelta en .NET). */
export interface CleanerCategoryScanResult {
  /** Id fijo (userTemp / windowsTemp / updateCache). */
  id: string;
  /** Etiqueta visible. */
  label: string;
  /** Raíz fija resuelta por el backend (solo transparencia). */
  path: string;
  fileCount: number;
  sizeBytes: number;
  /** false: la raíz no se pudo leer (ACL) — la UI lo muestra como "sin permisos". */
  accessible: boolean;
}

/** Resultado de cleaner.scan. */
export interface CleanerScanResult {
  categories: CleanerCategoryScanResult[];
}

/** Resultado de limpieza de una categoría (cleaner.clean). */
export interface CleanerCategoryCleanResult {
  /** Id fijo de la categoría. */
  id: string;
  deletedFiles: number;
  freedBytes: number;
  /** Ficheros omitidos (en uso o sin permiso). */
  skippedFiles: number;
  /** Detalle controlado (p. ej. "requiere administrador"); ausente si no hay. */
  detail?: string | null;
}

/** Resultado de cleaner.clean. */
export interface CleanerCleanResult {
  categories: CleanerCategoryCleanResult[];
}

/* =====================================================================
   Security Center (§14: solo lectura)
   ===================================================================== */

/** Tarjeta de Windows Defender en security.getStatus. */
export interface DefenderStatusResult {
  /** good | poor | notMonitored | snoozed | unknown. */
  status: string;
  /** Ausente si WMI no respondió — no significa "desactivado". */
  antivirusEnabled?: boolean | null;
  realTimeProtection?: boolean | null;
  /** Ausente si Defender no expuso la fecha. */
  signatureLastUpdated?: string | null;
}

/** Tarjeta de firewall con un semáforo por perfil. */
export interface FirewallStatusResult {
  /** good | poor | notMonitored | snoozed | unknown. */
  status: string;
  /** false: perfil desactivado; ausente: el registro no se pudo leer. */
  domainEnabled?: boolean | null;
  privateEnabled?: boolean | null;
  publicEnabled?: boolean | null;
}

/** Antivirus registrados en el Centro de seguridad (WMI SecurityCenter2). */
export interface AntivirusStatusResult {
  /** good | poor | notMonitored | snoozed | unknown. */
  status: string;
  /** Vacío si WMI no respondió. */
  products: string[];
}

/** Puerto en escucha (solo lectura, IPGlobalProperties + PID). */
export interface PortResult {
  /** TCP | UDP. */
  protocol: string;
  localAddress: string;
  localPort: number;
  /** Ausente si la ACL lo niega. */
  pid?: number | null;
  process?: string | null;
}

/** Una línea de diagnóstico de security.getStatus. */
export interface SecurityCheckResult {
  /** uac | rdp | realtime | signatures. */
  id: string;
  label: string;
  /** ok | warn | unknown. */
  status: string;
  /** Ausente si no hay detalle legible. */
  detail?: string | null;
}

/** Resultado de security.getStatus (§14: Security Center de solo lectura). */
export interface SecurityStatusResult {
  defender: DefenderStatusResult;
  firewall: FirewallStatusResult;
  antivirus: AntivirusStatusResult;
  ports: PortResult[];
  diagnostics: SecurityCheckResult[];
  /** Marca de tiempo ISO 8601 de la lectura. */
  scannedAt: string;
}

/* =====================================================================
   System info (Task 001)
   ===================================================================== */

export interface SystemInfoResult {
  machineName: string;
  osDescription: string;
  osVersion: string;
  processorCount: number;
  isAdministrator: boolean;
  appVersion: string;
  runtimeVersion: string;
  is64BitOperatingSystem: boolean;
}

/* =====================================================================
   Plugins (Task 013 / §17)
   ===================================================================== */

/** Tarjeta de plugin en plugin.list. */
export interface PluginMetadata {
  /** Id único del plugin (clave). */
  id: string;
  /** Nombre visible del plugin. */
  name: string;
  /** Versión actual del plugin. */
  version: string;
  /** Estado del plugin. */
  status: string;
  /** Indica si hay actualización disponible. */
  updateAvailable: boolean;
  /** Versión disponible (si hay update). */
  availableVersion?: string | null;
}

/** Resultado de la operación plugin.list: colección de plugins instalados. */
export interface PluginCollectionResult {
  /** Lista de plugins instalados. */
  plugins: PluginMetadata[];
  /** Instante UTC de la lista (cuando fue escaneado). */
  scannedAt: string;
}

/** Resultado de checkUpdate o launch de plugin. */
export interface PluginOperationResult {
  /** Indica si la operación succeeded. */
  succeeded: boolean;
  /** Mensaje descriptivo para la UI (toast, etc.). */
  detail?: string | null;
  /** Versión actual después de la operación. */
  currentVersion?: string | null;
  /** Versión más reciente disponible. */
  latestVersion?: string | null;
}

/* =====================================================================
   Activity
   ===================================================================== */

/** Metadato de una activity entry. */
export interface ActivityEntry {
  /** Marca de tiempo ISO 8601. */
  timestamp: string;
  /** Tipo de actividad (p. ej. tool.execute, plugin.launch). */
  type: string;
  /** Payload asociado (opcional). */
  payload?: unknown;
}

/** Resultado de activity/history. */
export interface ActivityHistoryResult {
  entries: ActivityEntry[];
}