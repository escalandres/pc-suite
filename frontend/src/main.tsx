import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import App from './App';
import { initTheme } from './theme';
import { getPreference, PREF_DENSITY } from './prefs';
import './index.css';

// Tema persistido aplicado antes del primer render (el default :root ya
// es oscuro, así que no hay flash mientras carga el módulo).
initTheme();

// Densidad persistida aplicada antes del primer render — mismo patrón
// que el tema: evita un frame en modo "comfy" al arrancar en compacto.
document.documentElement.dataset.density = getPreference<'compact' | 'comfy'>(PREF_DENSITY, 'comfy');

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
