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
  clamavFullScan: 'clamav.fullScan',
  clamavCustomScan: 'clamav.customScan',
  clamavHistory: 'clamav.history',
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
  localShareAccept: 'localshare.accept',
  localShareReject: 'localshare.reject',
  localShareTransfers: 'localshare.transfers',
  localShareCancelTransfer: 'localshare.cancelTransfer',
  localShareConfig: 'localshare.config',
  localShareClearHistory: 'localshare.clearHistory',
  localShareRevoke: 'localshare.revoke',
  localShareFirewallStatus: 'localshare.firewallStatus',
  localShareFirewallRule: 'localshare.firewallRule',
} as const;

export const EventIds = {
  appReady: 'app.ready',
  clamavFreshclamProgress: 'clamav.freshclam.progress',
  clamavScanProgress: 'clamav.scan.progress',
  jobEvent: 'job.event',
  localShareDeviceChanged: 'localshare.device-changed',
  localShareIncomingRequest: 'localshare.incoming-request',
  localShareTransferProgress: 'localshare.transfer-progress',
  localShareTransferFinished: 'localshare.transfer-finished',
} as const;

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

/** Evento job.event: ciclo de vida de un job (resultado en statusChanged). */
export interface JobEventPayload {
  jobId: string;
  type: 'statusChanged' | 'output' | 'progress';
  /** running | completed | failed | cancelled (en statusChanged). */
  status?: string | null;
  /** 0..1 (en progress). */
  progress?: number | null;
  error?: string | null;
  /** Resultado estructurado al completarse (p. ej. el escaneo). */
  result?: ClamAvScanResult | null;
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
   Local Share types (Task 016)
   ===================================================================== */

export interface LocalShareStatusResult {
  status: 'stopped' | 'starting' | 'running' | 'error';
  port: number;
  displayName?: string | null;
  discoveryEnabled: boolean;
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
}

export interface SharedDeviceCollectionResult {
  devices: SharedDeviceResult[];
}

export interface LocalSharePairResult {
  deviceId: string;
  displayName: string;
  pin: string;
  pendingConfirmation: boolean;
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

/** Resultado de localshare.firewallStatus (solo lectura). */
export interface LocalShareFirewallStatusResult {
  ruleExists: boolean | null;
  portMatches: boolean;
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