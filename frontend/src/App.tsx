import { useEffect, useState, useCallback } from 'react';
import { AppLayout, NAV_ITEMS } from './layouts/AppLayout';
import type { PageId } from './layouts/AppLayout';
import { Home } from './pages/Home';
import { Tools } from './pages/Tools';
import { DevTools } from './pages/DevTools';
import { Winget } from './pages/Winget';
import { Wsl } from './pages/Wsl';
import { Clamav } from './pages/Clamav';
import { Certificates } from './pages/Certificates';
import { FileTools } from './pages/FileTools';
import { Cipher } from './pages/Cipher';
import { Cleaner } from './pages/Cleaner';
import { Security } from './pages/Security';
import { Settings } from './pages/Settings';
import { Plugins } from './pages/Plugins';
import { LocalShare } from './pages/LocalShare';
import { ActivityPage } from './pages/Activity';
import { Toasts } from './components/Toasts';
import { bridge } from './services/pcsuiteBridge';
import { CommandPalette } from './components/CommandPalette';
import { ShortcutsHelp } from './components/ShortcutsHelp';
import { Welcome } from './components/Welcome';
import {
  isEditableTarget,
  registerShortcut,
  toggleHelp,
} from './shortcuts';
import type { AppReadyEvent, SystemInfoResult } from './types/bridge';
import { EventIds, OperationIds } from './types/bridge';
import { usePreference, PREF_DENSITY } from './prefs';

/** Páginas válidas para deep-link: EL MISMO registro del sidebar.
 *  (Fix 2026-10-26: la lista manual olvidó files/cipher — al navegar se
 *  escribía #/files, el hashchange no lo reconocía y rebotaba a Inicio.
 *  Derivado de NAV_ITEMS, cualquier página nueva es deep-linkable por
 *  construcción.) */
const VALID_PAGES: PageId[] = NAV_ITEMS.filter((item) => item.enabled).map((item) => item.id);

/** Parsea el hash de la URL: #/page o #/tools/tool-id */
function parseHash(): { page: PageId; toolId?: string } {
  const hash = window.location.hash.slice(1); // quita '#'
  if (!hash || hash === '/') return { page: 'home' };
  const parts = hash.split('/').filter(Boolean);
  const page = parts[0] as PageId;
  const toolId = parts[1];
  if (!VALID_PAGES.includes(page)) return { page: 'home' };
  return { page, toolId };
}

/** Construye hash para navegación */
function buildHash(page: PageId, toolId?: string): string {
  return toolId ? `#/${page}/${toolId}` : `#/${page}`;
}

/** Cache simple de nombres de tools */
const toolNameCache = new Map<string, string>();

/** Obtiene nombre de tool (usa cache o devuelve toolId como fallback) */
function getToolName(page: PageId, toolId: string): string {
  if (page !== 'tools') return toolId;
  return toolNameCache.get(toolId) ?? toolId;
}

/** Precarga nombres de tools en cache */
async function precacheToolNames(): Promise<void> {
  try {
    const result = await bridge.call<{ tools: { id: string; name: string }[] }>('tools.list');
    result.tools?.forEach((tool) => {
      toolNameCache.set(tool.id, tool.name);
    });
  } catch {
    // Silencioso: fallback a toolId
  }
}

export default function App() {
  // 14.1.5 — Deep linking: estado inicial desde URL hash
  const initial = parseHash();
  const [page, setPage] = useState<PageId>(initial.page);
  const [focus, setFocus] = useState<{ toolId: string } | null>(
    initial.toolId ? { toolId: initial.toolId } : null
  );
  const [readyNotice, setReadyNotice] = useState<string | null>(null);

  // Sincroniza URL hash al navegar
  const navigate = useCallback((target: PageId, toolId?: string) => {
    setPage(target);
    setFocus(toolId ? { toolId } : null);
    window.location.hash = buildHash(target, toolId);
  }, []);

  // Escucha cambios de hash (botón atrás/adelante del navegador)
  useEffect(() => {
    const onHashChange = () => {
      const { page: newPage, toolId } = parseHash();
      setPage(newPage);
      setFocus(toolId ? { toolId } : null);
    };
    window.addEventListener('hashchange', onHashChange);
    return () => window.removeEventListener('hashchange', onHashChange);
  }, []);

  // 14.3.6 — Densidad (compact/comfy) aplicada al <html>. usePreference
  // suscribe App al store de preferencias: al cambiarla en Ajustes el
  // efecto se re-ejecuta y la densidad aplica al instante, sin recargar.
  const density = usePreference<'compact' | 'comfy'>(PREF_DENSITY, 'comfy');
  useEffect(() => {
    document.documentElement.dataset.density = density;
  }, [density]);

  // Al cargar, informa disponibilidad del host y escucha app.ready.
  useEffect(() => {
    if (!bridge.isNativeHostAvailable) {
      setReadyNotice('UI sin host nativo: abre la app de escritorio de PCSuite.');
      return;
    }

    void precacheToolNames();

    const unsubscribe = bridge.on<AppReadyEvent>(EventIds.appReady, (payload) => {
      setReadyNotice(`PCSuite ${payload?.appVersion ?? ''} listo.`);
    });

    // Carrera de arranque: el host emite app.ready en NavigationCompleted,
    // que puede llegar ANTES de que React monte y suscriba el handler de
    // arriba → el evento one-shot se pierde y el pie de la sidebar quedaba
    // "Cargando…" para siempre (por eso solo pasaba A VECES). Recuperación
    // determinista: pedir la versión por system.info — el router del puente
    // se conecta antes de navegar, así que esta llamada no se puede perder.
    // Respeta un aviso ya puesto por app.ready (setReadyNotice funcional).
    bridge
      .call<SystemInfoResult>(OperationIds.systemInfo)
      .then((info) => {
        setReadyNotice((current) => current ?? `PCSuite ${info.appVersion} listo.`);
      })
      .catch(() => {
        // Sin recuperación posible: si el call falló, app.ready tampoco
        // llegará — el pie mantiene "Cargando…", que es cierto.
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
      <AppLayout
        page={page}
        onNavigate={navigate}
        readyNotice={readyNotice}
        currentToolId={focus?.toolId}
        currentToolName={focus?.toolId ? getToolName(page, focus.toolId) : undefined}
      >
        {renderPage(page, focus)}
      </AppLayout>
      <CommandPalette
        open={paletteOpen}
        onClose={() => setPaletteOpen(false)}
        onNavigate={navigate}
      />
      <ShortcutsHelp />
      <Toasts />
      <Welcome />
    </>
  );
}

function renderPage(
  page: PageId,
  focus: { toolId: string } | null,
) {
  switch (page) {
    case 'tools':
      return <Tools focus={focus} />;
    case 'localshare':
      return <LocalShare />;
    case 'devtools':
      return <DevTools />;
    case 'winget':
      return <Winget />;
    case 'wsl':
      return <Wsl />;
    case 'clamav':
      return <Clamav />;
    case 'certificates':
      return <Certificates />;
    case 'files':
      return <FileTools />;
    case 'cipher':
      return <Cipher />;
    case 'security':
      return <Security />;
    case 'cleaner':
      return <Cleaner />;
    case 'plugins':
      return <Plugins />;
    case 'settings':
      return <Settings />;
    case 'activity':
      return <ActivityPage />;
    default:
      return <Home />;
  }
}
