import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

// En producción el backend sirve frontend/dist mediante el host virtual
// https://app.pcsuite.local. En desarrollo (PCSUITE_DEV_URL) se usa el
// dev server de Vite con HMR. base './' mantiene rutas relativas para
// que el WebView cargue https://app.pcsuite.local/index.html sin fallos.
export default defineConfig({
  base: './',
  plugins: [react(), tailwindcss()],
  build: {
    outDir: 'dist',
    emptyOutDir: true,
  },
  server: {
    port: 5173,
    strictPort: true,
  },
});
