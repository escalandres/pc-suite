import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import App from './App';
import { initTheme } from './theme';
import './index.css';

// Tema persistido aplicado antes del primer render (el default :root ya
// es oscuro, así que no hay flash mientras carga el módulo).
initTheme();

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
