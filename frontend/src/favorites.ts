import { getPreference, PREF_FAVORITES, setPreference, usePreference } from './prefs';
import type { PageId } from './layouts/AppLayout';

/**
 * Favoritos (plan §19, Task 010 parte 10.3): lista persistida en el
 * store de preferencias. Cada entrada fija una página de la sidebar o
 * una tool de la página Herramientas (que además se selecciona sola al
 * navegar desde el favorito).
 */

export interface Favorite {
  key: string; // 'page:<id>' | 'tool:<id>'
  label?: string; // texto guardado (fallback si no hay labelKey)
  labelKey?: string; // clave de i18n; se prefiere sobre `label`
  page: PageId;
  toolId?: string;
}

export function pageKey(id: PageId): string {
  return `page:${id}`;
}

export function toolKey(id: string): string {
  return `tool:${id}`;
}

export function toggleFavorite(favorite: Favorite): void {
  const items = getPreference<Favorite[]>(PREF_FAVORITES, []);
  const exists = items.some((item) => item.key === favorite.key);
  setPreference(
    PREF_FAVORITES,
    exists ? items.filter((item) => item.key !== favorite.key) : [...items, favorite],
  );
}

/** Lista de favoritos reactiva (re-renderiza el componente al cambiar). */
export function useFavorites(): Favorite[] {
  return usePreference<Favorite[]>(PREF_FAVORITES, []);
}
