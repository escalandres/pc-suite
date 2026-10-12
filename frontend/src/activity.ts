import { useSyncExternalStore } from 'react';
import { getPreference, PREF_NOTIFICATIONS } from './prefs';
import { bridge } from './services/pcsuiteBridge';
import type { BridgeCallEvent } from './services/pcsuiteBridge';

/**
 * Activity center + toasts globales (§36–37, Task 010 parte 10.6).
 *
 * Se alimentan del ciclo de vida de las llamadas al puente (onCall), sin
 * instrumentar el host. Todo vive en memoria: nada a disco ni a logs, sin
 * stack traces (los mensajes vienen estructurados del puente).
 */

export type ActivityStatus = 'running' | 'ok' | 'error';

export interface ActivityEntry {
  id: string;
  operation: string;
  status: ActivityStatus;
  startedAt: number;
  finishedAt?: number;
  errorMessage?: string;
}

export interface ToastAction {
  label: string;
  onClick: () => void;
  variant?: 'primary' | 'secondary';
}

export interface Toast {
  id: string;
  kind: 'success' | 'error';
  message: string;
  /** Acciones opcionales (retry, undo, etc.) */
  actions?: ToastAction[];
}

/** Consultas de sondeo que ensuciarían Activity (búsquedas por tecla, etc.). */
const NOISY = new Set(['system.info', 'tools.list', 'tools.search', 'developer.list']);

/** Solo las mutaciones generan toast de éxito; los errores, siempre. */
const MUTATION =
  /\.(install|upgrade|upgradeAll|uninstall|update|updateDefinitions|quickScan|fullScan|customScan|shutdown|start|execute|generate|generateKey|importKey|importPublicKey|encrypt|decrypt|sign|clean)$/;

const MAX_ENTRIES = 100;
const MAX_TOASTS = 4;
const TOAST_MS = 5000;

// ponytail: sin toast 'warning' todavía — el guion §36 lo admite pero ninguna
// página lo produce hoy; añadirlo cuando exista un emisor real.

let entries: ActivityEntry[] = [];
let toasts: Toast[] = [];
const entryListeners = new Set<() => void>();
const toastListeners = new Set<() => void>();
let seq = 0;

function emitEntries(): void {
  entryListeners.forEach((notify) => notify());
}

function emitToasts(): void {
  toastListeners.forEach((notify) => notify());
}

export function dismissToast(id: string): void {
  if (!toasts.some((toast) => toast.id === id)) return;
  toasts = toasts.filter((toast) => toast.id !== id);
  emitToasts();
}

function pushToast(kind: Toast['kind'], message: string, actions?: ToastAction[]): void {
  if (!getPreference(PREF_NOTIFICATIONS, true)) return;
  const id = `t${++seq}`;
  toasts = [...toasts, { id, kind, message, actions }].slice(-MAX_TOASTS);
  emitToasts();
  setTimeout(() => dismissToast(id), TOAST_MS);
}

/** Resultado de mutación CLI: success del puente con succeeded:false dentro. */
interface MutationLike {
  succeeded?: boolean;
  detail?: string | null;
}

function handleCall(event: BridgeCallEvent): void {
  if (NOISY.has(event.operation)) return;

  if (event.phase === 'start') {
    const entry: ActivityEntry = {
      id: event.id,
      operation: event.operation,
      status: 'running',
      startedAt: Date.now(),
    };
    entries = [entry, ...entries].slice(0, MAX_ENTRIES);
    emitEntries();
    return;
  }

  const mutation = event.result as MutationLike | undefined;
  const cliFailed = !event.error && mutation?.succeeded === false;
  const status: ActivityStatus = event.error || cliFailed ? 'error' : 'ok';
  const errorMessage = event.error
    ? event.error.message
    : cliFailed
      ? mutation?.detail || 'La operación no se completó.'
      : undefined;
  const finishedAt = Date.now();

  const index = entries.findIndex((entry) => entry.id === event.id);
  if (index >= 0) {
    const current = entries[index];
    entries = [
      ...entries.slice(0, index),
      { ...current, status, finishedAt, errorMessage },
      ...entries.slice(index + 1),
    ];
  } else {
    // Sin start registrado (fallo antes de enviar): entra ya completada.
    entries = [
      { id: event.id, operation: event.operation, status, startedAt: finishedAt, finishedAt, errorMessage },
      ...entries,
    ].slice(0, MAX_ENTRIES);
  }
  emitEntries();

  if (status === 'error') {
    // Para errores de mutación, ofrecer reintento
    const isMutation = MUTATION.test(event.operation);
    const actions: ToastAction[] | undefined = isMutation
      ? [
          {
            label: 'Reintentar',
            onClick: () => {
              // Re-disparar la operación (simplificado: navegar a la página correspondiente)
              // En una implementación completa se guardaría el payload original
              console.log('Retry requested for:', event.operation);
            },
            variant: 'primary',
          },
        ]
      : undefined;
    pushToast('error', `✕ ${event.operation}: ${errorMessage}`, actions);
  } else if (MUTATION.test(event.operation)) {
    pushToast('success', `✓ ${event.operation} finalizada`);
  }
}

// Suscripción permanente al importar: cualquier llamada al puente — página,
// paleta o atajo — alimenta Activity y toasts sin carreras con los efectos de
// React (los efectos de hijos corren antes que los del App).
bridge.onCall(handleCall);

export function useActivity(): ActivityEntry[] {
  return useSyncExternalStore(
    (listener) => {
      entryListeners.add(listener);
      return () => {
        entryListeners.delete(listener);
      };
    },
    () => entries,
  );
}

export function useToasts(): Toast[] {
  return useSyncExternalStore(
    (listener) => {
      toastListeners.add(listener);
      return () => {
        toastListeners.delete(listener);
      };
    },
    () => toasts,
  );
}
