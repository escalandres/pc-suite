/**
 * Contratos del protocolo puente (espejo de PCSuite.Contracts).
 * Mantener sincronizados con src/PCSuite.Contracts/.
 */

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

/** Resultado de la operación system.info. */
export interface SystemInfoResult {
  appVersion: string;
  runtimeVersion: string;
  osDescription: string;
  osVersion: string;
  machineName: string;
  processorCount: number;
  is64BitOperatingSystem: boolean;
  isAdministrator: boolean;
}

/** Payload del evento app.ready. */
export interface AppReadyEvent {
  appVersion: string;
  startedAtUtc: string;
}

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
} as const;

export const EventIds = {
  appReady: 'app.ready',
} as const;

/** Metadato de una tool tal como lo devuelve el nativo. */
export interface ToolInfo {
  id: string;
  moduleId: string;
  name: string;
  description: string;
  keywords: string[];
  /** Ejemplo de payload para precargar la UI genérica. */
  exampleInput?: unknown;
}

/** Resultado de tools.list / tools.search. */
export interface ToolCollectionResult {
  tools: ToolInfo[];
}

/** Resultado de tool.execute. */
export interface ToolExecuteResult {
  toolId: string;
  output?: unknown;
}

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

/** Resultado de winget.detect. */
export interface WinGetDetectResult {
  installed: boolean;
  /** Versión sin la 'v'; ausente si es desconocida. */
  version?: string | null;
  path?: string | null;
  /** true: el proceso ya corre elevado (sin pedir UAC). */
  isAdministrator: boolean;
}

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

/**
 * Resultado de las mutaciones winget (install/upgrade/upgradeAll/uninstall).
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

/**
 * Resultado de las mutaciones wsl (update/shutdown/start). Igual que winget:
 * un fallo del CLI vuelve como resultado estructurado, nunca como error del
 * puente.
 */
export interface WslMutationResult {
  succeeded: boolean;
  exitCode: number;
  elevated: boolean;
  detail?: string | null;
}
