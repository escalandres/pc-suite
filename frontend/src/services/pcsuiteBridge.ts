import type { BridgeEvent, BridgeRequest, BridgeResponse } from '../types/bridge';

/**
 * Servicio puente: envía operaciones al lado nativo y recibe eventos.
 *
 * Lado nativo (WebViewBridge.cs) expone window.chrome.webview:
 *   - postMessage(request)  → envía {id, operation, payload}
 *   - addEventListener('message') → recibe respuestas {id,...} y eventos {event,...}
 */

declare global {
  interface Window {
    chrome?: {
      webview?: {
        postMessage(message: unknown): void;
        addEventListener(type: 'message', listener: (event: MessageEvent) => void): void;
      };
    };
  }
}

const DEFAULT_TIMEOUT_MS = 15_000;

interface Pending {
  resolve: (value: unknown) => void;
  reject: (reason: Error) => void;
  timer: ReturnType<typeof setTimeout>;
}

export class BridgeError extends Error {
  readonly code: string;

  constructor(code: string, message: string) {
    super(message);
    this.name = 'BridgeError';
    this.code = code;
  }
}

type EventHandler<T> = (payload: T) => void;

/** Fase del ciclo de vida de una call() (alimenta Activity y toasts, §36–37). */
export interface BridgeCallEvent {
  phase: 'start' | 'end';
  id: string;
  operation: string;
  /** Solo en 'end' si falló el puente (mensaje estructurado, jamás stack trace). */
  error?: { code: string; message: string };
  /** Solo en 'end': resultado estructurado (p. ej. succeeded:false del CLI). */
  result?: unknown;
}

type CallObserver = (event: BridgeCallEvent) => void;

export class PcsuiteBridge {
  private readonly pending = new Map<string, Pending>();
  private readonly handlers = new Map<string, Set<EventHandler<unknown>>>();
  private readonly callObservers = new Set<CallObserver>();
  private seq = 0;

  constructor() {
    const webview = window.chrome?.webview;
    if (webview) {
      webview.addEventListener('message', (event) => this.onMessage(event.data));
    }
  }

  /** true si estamos dentro de un WebView2 con el bridge disponible. */
  get isNativeHostAvailable(): boolean {
    return window.chrome?.webview !== undefined;
  }

  /**
   * Invoca una operación nativa y espera su respuesta.
   * Rechaza con BridgeError si la operación falla o no está permitida.
   */
  call<T>(operation: string, payload?: unknown, timeoutMs = DEFAULT_TIMEOUT_MS): Promise<T> {
    const id = `${Date.now()}-${++this.seq}`;
    const webview = window.chrome?.webview;
    if (!webview) {
      const error = new BridgeError('BRIDGE_UNAVAILABLE', 'El host nativo no está disponible.');
      this.emitCall({
        phase: 'end',
        id,
        operation,
        error: { code: error.code, message: error.message },
      });
      return Promise.reject(error);
    }

    const request: BridgeRequest = { id, operation };
    if (payload !== undefined) {
      request.payload = payload;
    }

    return new Promise<T>((resolve, reject) => {
      const timer = setTimeout(() => {
        this.pending.delete(id);
        reject(new BridgeError('TIMEOUT', `La operación "${operation}" no respondió a tiempo.`));
      }, timeoutMs);

      this.pending.set(id, {
        resolve: resolve as (value: unknown) => void,
        reject,
        timer,
      });

      try {
        webview.postMessage(request);
      } catch (error) {
        clearTimeout(timer);
        this.pending.delete(id);
        reject(
          new BridgeError(
            'POST_FAILED',
            error instanceof Error ? error.message : 'No se pudo enviar el mensaje.',
          ),
        );
        return;
      }
      this.emitCall({ phase: 'start', id, operation });
    }).then(
      (value) => {
        this.emitCall({ phase: 'end', id, operation, result: value });
        return value;
      },
      (error: unknown) => {
        const info =
          error instanceof BridgeError
            ? { code: error.code, message: error.message }
            : {
                code: 'UNKNOWN',
                message: error instanceof Error ? error.message : 'Error desconocido.',
              };
        this.emitCall({ phase: 'end', id, operation, error: info });
        throw error;
      },
    );
  }

  /** Observa el inicio/fin de cada call(); devuelve la función de baja. */
  onCall(observer: CallObserver): () => void {
    this.callObservers.add(observer);
    return () => {
      this.callObservers.delete(observer);
    };
  }

  private emitCall(event: BridgeCallEvent): void {
    for (const observer of this.callObservers) {
      try {
        observer(event);
      } catch {
        // un observador roto no debe romper la operación observada
      }
    }
  }

  /** Suscribe un manejador a un evento nativo. Devuelve la función de baja. */
  on<T>(event: string, handler: EventHandler<T>): () => void {
    let set = this.handlers.get(event);
    if (!set) {
      set = new Set();
      this.handlers.set(event, set);
    }
    const wrapped = handler as EventHandler<unknown>;
    set.add(wrapped);
    return () => {
      set?.delete(wrapped);
    };
  }

  private onMessage(data: unknown): void {
    if (!data || typeof data !== 'object') {
      return;
    }

    const message = data as Partial<BridgeResponse> & Partial<BridgeEvent>;

    // Respuesta: tiene "id".
    if (typeof message.id === 'string') {
      const pending = this.pending.get(message.id);
      if (!pending) {
        return;
      }
      clearTimeout(pending.timer);
      this.pending.delete(message.id);

      if (message.success === true) {
        pending.resolve(message.result);
      } else {
        const error = message.error;
        pending.reject(
          new BridgeError(
            error?.code ?? 'UNKNOWN',
            error?.message ?? 'Error desconocido.',
          ),
        );
      }
      return;
    }

    // Evento: tiene "event".
    if (typeof message.event === 'string') {
      const set = this.handlers.get(message.event);
      if (set) {
        for (const handler of set) {
          handler(message.payload);
        }
      }
    }
  }
}

export const bridge = new PcsuiteBridge();
