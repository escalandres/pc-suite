# Plan i18n PCSuite

## Fases

### Fase 1: Infraestructura core
- [x] `frontend/src/lib/i18n.ts` — cargador JSON, idioma actual, `t(key)`, `setLang()`, `getLang()`
- [x] `frontend/src/hooks/useTranslation.ts` — hook React reactivo
- [x] `frontend/src/locales/es.json` — traducciones español (base)
- [x] `frontend/src/locales/en.json` — traducciones inglés (base)

### Fase 2: Sidebar y navegación
- [x] `frontend/src/layouts/AppLayout.tsx` — labels del sidebar + selector de idioma (ES/EN)

### Fase 3: Páginas principales (migrar a `t()`)
- [x] `frontend/src/pages/Home.tsx`
- [x] `frontend/src/pages/Ai.tsx`
- [x] `frontend/src/pages/Windows.tsx`
- [x] `frontend/src/pages/FileTools.tsx`
- [x] `frontend/src/pages/Cipher.tsx`
- [x] `frontend/src/pages/Certificates.tsx`
- [x] `frontend/src/pages/Security.tsx`
- [x] `frontend/src/pages/Cleaner.tsx`
- [x] `frontend/src/pages/Settings.tsx`
- [x] `frontend/src/pages/Plugins.tsx`
- [x] `frontend/src/pages/LocalShare.tsx` (parcial: h1, subtítulo, status labels; restantes: notices, botones, transfer rows)
- [x] `frontend/src/pages/Activity.tsx`
- [x] `frontend/src/pages/Tools.tsx`
- [x] `frontend/src/pages/Winget.tsx` (parcial: h1, subtítulo, modes, empty hints; restantes: notices, modal, tarjetas de paquetes)
- [x] `frontend/src/pages/Wsl.tsx`
- [x] `frontend/src/pages/Clamav.tsx`

### Fase 4: Componentes compartidos
- [ ] `frontend/src/components/*.tsx` (UpdateCenter, Toasts, Breadcrumbs, etc.)

### Fase 5: Verificación
- [x] `npm --prefix frontend run build` pasa sin errores
