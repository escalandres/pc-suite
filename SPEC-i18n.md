# Spec: i18n — Internacionalización Inglés/Español (PCSuite)

## Objective

Implementar un sistema de internacionalización (i18n) ligero en el frontend de PCSuite para permitir al usuario cambiar el idioma de la interfaz entre **español (es)** e **inglés (en)**. El texto de todos los componentes del sidebar, páginas, diálogos y notificaciones debe estar disponible en ambos idiomas.

El sistema debe ser simple, sin dependencias externas pesadas: un conjunto de archivos JSON con claves de traducción y un hook `useTranslation` que las resuelva.

## Assumptions I'm making

1. El frontend es React 19 + TypeScript + Vite, embebido en WebView2.
2. No se usa ninguna librería i18n externa (react-i18next, etc.) para evitar aumentar el bundle.
3. Los archivos de traducción viven en `frontend/src/locales/es.json` y `frontend/src/locales/en.json`.
4. El idioma se almacena en `localStorage` con clave `pcsuite.lang` y por defecto se detecta del navegador (`navigator.language`).
5. El idioma se muestra en el sidebar con un selector (dropdown o botones).
6. Todos los componentes existentes deben migrar a usar `t('clave')` en lugar de texto hardcodeado.
7. Los cambios de protocolo/bridge no son necesarios (todo es del lado del frontend).

## Tech Stack

- Frontend: React 19, TypeScript, Tailwind CSS 4, Vite
- i18n: sistema propio con archivos JSON + hook `useTranslation`
- Archivos: `frontend/src/locales/es.json`, `frontend/src/locales/en.json`, `frontend/src/lib/i18n.ts`, `frontend/src/hooks/useTranslation.ts`

## Commands

```
# Build frontend
npm --prefix frontend run build

# Lint/typecheck
npm --prefix frontend run build  (tsc -b && vite build)
```

## Project Structure (files to touch)

```
frontend/src/lib/i18n.ts              — cargador de JSON, idioma actual, función t()
frontend/src/hooks/useTranslation.ts  — hook React que re-renderiza al cambiar idioma
frontend/src/locales/es.json          — traducciones en español
frontend/src/locales/en.json          — traducciones en inglés
frontend/src/layouts/AppLayout.tsx    — sidebar labels + selector de idioma
frontend/src/App.tsx                  — breadcrumbs, modales, textos sueltos
frontend/src/pages/*.tsx              — todas las páginas: texto de UI
frontend/src/components/*.tsx         — componentes compartidos
```

## Code Style

- Claves de traducción en kebab-case o camelCase anidado: `sidebar.inicio`, `pages.ai.title`, `buttons.convertir`, etc.
- `t(key)` retorna string; si falta la clave, retorna la propia clave (fallback silencioso).
- Interpolación simple con placeholders `{{nombre}}` opcional.
- Los componentes usan `const t = useTranslation()` y escriben `t('clave')` en lugar de strings literales.

## Testing Strategy

- No hay tests de i18n específicos exigidos en v1.
- El build de TypeScript debe pasar sin errores.
- El frontend debe compilar con `npm --prefix frontend run build`.

## Boundaries

- Always: usar `t()` para todo texto visible al usuario; no hardcodear strings nuevas sin traducir.
- Ask first: añadir dependencia de i18n externa; cambiar el sistema de persistencia del idioma.
- Never: texto en inglés hardcodeado en componentes después de la migración; dejar claves faltantes que rompan el build.

## Success Criteria

- [ ] `frontend/src/lib/i18n.ts` carga `es.json` y `en.json` y expone `t(key)` + `setLang(lang)` + `getLang()`
- [ ] `frontend/src/hooks/useTranslation.ts` re-renderiza al cambiar idioma
- [ ] `frontend/src/locales/es.json` y `frontend/src/locales/en.json` existen con claves para sidebar, páginas IA, Windows, Archivos, Cifrador, Seguridad, etc.
- [ ] Sidebar muestra selector de idioma (ES/EN)
- [ ] Todas las páginas principales usan `t()` para su texto visible
- [ ] `npm --prefix frontend run build` pasa sin errores

## Open Questions

1. ¿Qué tan exhaustiva debe ser la traducción v1? (todas las páginas o solo las principales)
2. ¿Se añade el selector de idioma en el sidebar o en Ajustes?