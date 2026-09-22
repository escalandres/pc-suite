import { useEffect, useState } from 'react';
import { AppLayout } from './layouts/AppLayout';
import type { PageId } from './layouts/AppLayout';
import { Home } from './pages/Home';
import { Tools } from './pages/Tools';
import { bridge } from './services/pcsuiteBridge';
import type { AppReadyEvent } from './types/bridge';
import { EventIds } from './types/bridge';

export default function App() {
  const [page, setPage] = useState<PageId>('home');
  const [readyNotice, setReadyNotice] = useState<string | null>(null);

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

  return (
    <AppLayout page={page} onNavigate={setPage} readyNotice={readyNotice}>
      {page === 'tools' ? <Tools /> : <Home />}
    </AppLayout>
  );
}
