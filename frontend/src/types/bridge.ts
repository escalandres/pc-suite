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
