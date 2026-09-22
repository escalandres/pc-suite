import { useEffect, useState } from 'react';
import { AppLayout } from './layouts/AppLayout';
import { Home } from './pages/Home';
import { bridge, BridgeError } from './services/pcsuiteBridge';
import type { AppReadyEvent } from './types/bridge';
import { EventIds } from './types/bridge';

export default function App() {
  const [readyNotice, setReadyNotice] = useState<string | null>(null);

  // Al cargar, informa disponibilidad del host y escucha app.ready.
  useEffect(() => {
    if (!bridge.isNativeHostAvailable) {
      setReadyNotice('Modo navegador: el host nativo no está disponible.');
      return;
    }

    const unsubscribe = bridge.on<AppReadyEvent>(EventIds.appReady, (payload) => {
      setReadyNotice(`PCSuite ${payload?.appVersion ?? ''} listo.`);
    });

    return unsubscribe;
  }, []);

  return (
    <AppLayout readyNotice={readyNotice}>
      <Home />
    </AppLayout>
  );
}

export { BridgeError };
