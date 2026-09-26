import { useSyncExternalStore } from 'react';

/**
 * Registro global de atajos de teclado (Task 010 parte 10.5).
 * Un único listener en window: los componentes registran/sacan sus
 * atajos y el overlay de ayuda (?) lista exactamente lo registrado.
 */

export interface Shortcut {
  id: string;
  /** Representación para la ayuda, p. ej. "Ctrl + K". */
  keys: string;
  description: string;
  /** true si el evento corresponde a este atajo. */
  match: (event: KeyboardEvent) => boolean;
  run: (event: KeyboardEvent) => void;
}

const registry = new Map<string, Shortcut>();
const listeners = new Set<() => void>();
let snapshot: Shortcut[] = [];
let bound = false;

function emit(): void {
  snapshot = [...registry.values()];
  listeners.forEach((listener) => listener());
}

function onKeyDown(event: KeyboardEvent): void {
  for (const shortcut of registry.values()) {
    if (shortcut.match(event)) {
      shortcut.run(event);
      return;
    }
  }
}

/** Registra un atajo (id repetido lo reemplaza); devuelve su anulación. */
export function registerShortcut(shortcut: Shortcut): () => void {
  registry.set(shortcut.id, shortcut);
  if (!bound) {
    bound = true;
    // ponytail: el listener no se desvincula al quedar el registro
    // vacío — un listener mudo en window es más barato que el ciclo.
    window.addEventListener('keydown', onKeyDown);
  }
  emit();
  return () => {
    registry.delete(shortcut.id);
    emit();
  };
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

function getSnapshot(): Shortcut[] {
  return snapshot;
}

/** Lista reactiva de atajos registrados (overlay de ayuda). */
export function useShortcutList(): Shortcut[] {
  return useSyncExternalStore(subscribe, getSnapshot);
}

/** true si el foco está en texto editable (para no disparar «?» al escribir). */
export function isEditableTarget(target: EventTarget | null): boolean {
  const element = target as HTMLElement | null;
  return (
    element?.tagName === 'INPUT' ||
    element?.tagName === 'TEXTAREA' ||
    element?.isContentEditable === true
  );
}

// --- Ayuda de atajos: estado global para abrirla desde cualquier sitio ---

let helpOpen = false;
const helpListeners = new Set<() => void>();

function emitHelp(): void {
  helpListeners.forEach((listener) => listener());
}

export function isHelpOpen(): boolean {
  return helpOpen;
}

export function openHelp(): void {
  helpOpen = true;
  emitHelp();
}

export function closeHelp(): void {
  helpOpen = false;
  emitHelp();
}

export function toggleHelp(): void {
  helpOpen = !helpOpen;
  emitHelp();
}

function subscribeHelp(listener: () => void): () => void {
  helpListeners.add(listener);
  return () => {
    helpListeners.delete(listener);
  };
}

export function useHelpOpen(): boolean {
  return useSyncExternalStore(subscribeHelp, isHelpOpen);
}
