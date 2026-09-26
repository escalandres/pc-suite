import { useEffect, useState } from 'react';
import { AppLayout } from './layouts/AppLayout';
import type { PageId } from './layouts/AppLayout';
import { Home } from './pages/Home';
import { Tools } from './pages/Tools';
import { DevTools } from './pages/DevTools';
import { Winget } from './pages/Winget';
import { Wsl } from './pages/Wsl';
import { Clamav } from './pages/Clamav';
import { Settings } from './pages/Settings';
import { bridge } from './services/pcsuiteBridge';
import { CommandPalette } from './components/CommandPalette';
import { ShortcutsHelp } from './components/ShortcutsHelp';
import {
  isEditableTarget,
  registerShortcut,
  toggleHelp,
} from './shortcuts';
import type { AppReadyEvent } from './types/bridge';
import { EventIds } from './types/bridge';

export default function App() {
  const [page, setPage] = useState<PageId>('home');
  // Identidad nueva en cada navegación para re-seleccionar la misma tool.
  const [focus, setFocus] = useState<{ toolId: string } | null>(null);
  const [readyNotice, setReadyNotice] = useState<string | null>(null);

  const navigate = (target: PageId, toolId?: string) => {
    setPage(target);
    setFocus(toolId ? { toolId } : null);
  };

  // Al cargar, informa disponibilidad del host y escucha app.ready.
  useEffect(() => {
    if (!bridge.isNativeHostAvailable) {
      setReadyNotice('UI sin host nativo: abre la app de escritorio de PCSuite.');
      return;
    }

    const unsubscribe = bridge.on<AppReadyEvent>(EventIds.appReady, (payload) => {
      setReadyNotice(`PCSuite ${payload?.appVersion ?? ''} listo.`);
    });

    return unsubscribe;
  }, []);

  // Atajos globales (10.5): todo pasa por el registro; la ayuda (?)
  // lista exactamente lo que haya dado de alta.
  const [paletteOpen, setPaletteOpen] = useState(false);
  useEffect(() => {
    const unregister = [
      registerShortcut({
        id: 'palette',
        keys: 'Ctrl + K',
        description: 'Abrir o cerrar la búsqueda global',
        match: (event) =>
          (event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k',
        run: (event) => {
          event.preventDefault();
          setPaletteOpen((isOpen) => !isOpen);
        },
      }),
      registerShortcut({
        id: 'help',
        keys: '?',
        description: 'Mostrar u ocultar la ayuda de atajos',
        match: (event) =>
          event.key === '?' &&
          !event.ctrlKey &&
          !event.metaKey &&
          !event.altKey &&
          !isEditableTarget(event.target),
        run: () => toggleHelp(),
      }),
      registerShortcut({
        id: 'settings',
        keys: 'Ctrl + ,',
        description: 'Ir a Ajustes',
        match: (event) =>
          (event.ctrlKey || event.metaKey) && event.key === ',',
        run: (event) => {
          event.preventDefault();
          navigate('settings');
        },
      }),
    ];
    return () => unregister.forEach((unregisterOne) => unregisterOne());
  }, []);

  return (
    <>
      <AppLayout page={page} onNavigate={navigate} readyNotice={readyNotice}>
        {renderPage(page, focus)}
      </AppLayout>
      <CommandPalette
        open={paletteOpen}
        onClose={() => setPaletteOpen(false)}
        onNavigate={navigate}
      />
      <ShortcutsHelp />
    </>
  );
}

function renderPage(page: PageId, focus: { toolId: string } | null) {
  switch (page) {
    case 'tools':
      return <Tools focus={focus} />;
    case 'devtools':
      return <DevTools />;
    case 'winget':
      return <Winget />;
    case 'wsl':
      return <Wsl />;
    case 'clamav':
      return <Clamav />;
    case 'settings':
      return <Settings />;
    default:
      return <Home />;
  }
}
