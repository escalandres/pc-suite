import { useSyncExternalStore } from 'react';

/**
 * Sistema de temas (plan §34): dark (primario), light y system.
 *
 * La preferencia se guarda en localStorage y al cambiarla se escribe
 * data-theme en <html>; los tokens de index.css hacen el resto.
 * "system" sigue los cambios de prefers-color-scheme en caliente.
 * Se llama a initTheme() desde main.tsx antes del primer render.
 */

export type ThemePreference = 'dark' | 'light' | 'system';
export type ResolvedTheme = 'dark' | 'light';

const STORAGE_KEY = 'pcsuite.theme';
const VALID: readonly string[] = ['dark', 'light', 'system'];

function readStored(): ThemePreference {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (raw !== null && VALID.includes(raw)) return raw as ThemePreference;
  } catch {
    // storage no disponible → preferencia por defecto
  }
  return 'dark'; // el oscuro es la experiencia primaria (§34)
}

function systemTheme(): ResolvedTheme {
  return window.matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark';
}

function resolve(pref: ThemePreference): ResolvedTheme {
  return pref === 'system' ? systemTheme() : pref;
}

interface ThemeSnapshot {
  preference: ThemePreference;
  resolved: ResolvedTheme;
}

let preference: ThemePreference = readStored();
let snapshot: ThemeSnapshot = { preference, resolved: resolve(preference) };
let initialized = false;
const listeners = new Set<() => void>();

function apply(): void {
  snapshot = { preference, resolved: resolve(preference) };
  document.documentElement.dataset.theme = snapshot.resolved;
  listeners.forEach((notify) => notify());
}

function onSystemChange(): void {
  if (preference === 'system') apply();
}

/** Aplica la preferencia persistida y escucha el sistema. Una sola vez. */
export function initTheme(): void {
  if (initialized) return;
  initialized = true;
  document.documentElement.dataset.theme = snapshot.resolved;
  window.matchMedia('(prefers-color-scheme: light)').addEventListener('change', onSystemChange);
}

export function setThemePreference(next: ThemePreference): void {
  if (!VALID.includes(next)) return;
  preference = next;
  try {
    localStorage.setItem(STORAGE_KEY, next);
  } catch {
    // sin persistencia, el cambio sigue vigente en esta sesión
  }
  apply();
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

function getSnapshot(): ThemeSnapshot {
  return snapshot;
}

/** Preferencia elegida (puede ser "system") + tema realmente aplicado. */
export function useTheme(): ThemeSnapshot & { setPreference: (next: ThemePreference) => void } {
  const snap = useSyncExternalStore(subscribe, getSnapshot, getSnapshot);
  return { ...snap, setPreference: setThemePreference };
}
