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
} as const;

export const EventIds = {
  appReady: 'app.ready',
} as const;
