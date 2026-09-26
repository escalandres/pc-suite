import { useSyncExternalStore } from 'react';

/**
 * Preferencias de UX en localStorage (decisión de diseño Task 010:
 * `ponytail:` SQLite §30 sería over-engineering para claves-valor de UI).
 * Cada clave se guarda como JSON, se cachea por sesión y cada escritura
 * notifica a los hooks usePreference suscritos. El tema vive aparte en
 * theme.ts (clave pcsuite.theme, string plano).
 */

export const PREF_NOTIFICATIONS = 'notifications';
export const PREF_CLIPBOARD_DETECTION = 'clipboardDetection';
export const PREF_FAVORITES = 'favorites';
export const PREF_FIRST_RUN = 'firstRunSeen';

const PREFIX = 'pcsuite.';
const cache = new Map<string, unknown>();
const listeners = new Set<() => void>();

function load<T>(fullKey: string, fallback: T): T {
  try {
    const raw = localStorage.getItem(fullKey);
    if (raw !== null) return JSON.parse(raw) as T;
  } catch {
    // clave ausente o JSON corrupto → valor por defecto
  }
  return fallback;
}

export function getPreference<T>(key: string, fallback: T): T {
  const fullKey = PREFIX + key;
  if (!cache.has(fullKey)) cache.set(fullKey, load(fullKey, fallback));
  return cache.get(fullKey) as T;
}

export function setPreference<T>(key: string, value: T): void {
  const fullKey = PREFIX + key;
  cache.set(fullKey, value);
  try {
    localStorage.setItem(fullKey, JSON.stringify(value));
  } catch {
    // sin persistencia el cambio sigue vigente en esta sesión
  }
  listeners.forEach((notify) => notify());
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

/** Preferencia persistida que re-renderiza el componente al cambiarse. */
export function usePreference<T>(key: string, fallback: T): T {
  const read = () => getPreference(key, fallback);
  return useSyncExternalStore(subscribe, read, read);
}
