import { useEffect, useState } from 'react';
import { Copy, Play, RotateCcw, AlertTriangle, CheckCircle2, Download, FileText, FolderOpen, Terminal } from 'lucide-react';
import { bridge, BridgeError } from '../services/pcsuiteBridge';
import { OperationIds } from '../types/bridge';
import type {
  AiStatusResult,
  AiMutationResult,
  AiConvertResult,
  AiConvertPayload,
  OllamaStatusResult,
  OllamaModelResult,
  OllamaModelCollectionResult,
} from '../types/bridge';
import { useTranslation } from '../hooks/useTranslation';

/**
 * Página "IA" (Task 038): convierte archivos a Markdown con markItDown y
 * gestiona modelos de Ollama.
 *
 * Flujo markItDown:
 * 1. ai.detect: ¿hay Python + markItDown?
 * 2. ai.install: instala markItDown vía pip (si falta).
 * 3. system.pickFiles: elige un archivo.
 * 4. ai.convert: ejecuta `python -m markitdown <ruta>` y devuelve Markdown.
 * 5. Copiar al portapapeles.
 *
 * Flujo Ollama:
 * 1. ollama.detect: ¿está Ollama instalado?
 * 2. ollama.listModels: lista los modelos disponibles.
 *
 * La conversión es una operación directa (respuesta síncrona con timeout
 * generoso): markItDown puede tardar minutos en archivos grandes.
 */
export function AiPage() {
  const { t } = useTranslation();
  // --- markItDown ---
  const [status, setStatus] = useState<AiStatusResult | null>(null);
  const [installing, setInstalling] = useState(false);
  const [converting, setConverting] = useState(false);
  const [selectedPath, setSelectedPath] = useState<string | null>(null);
  const [result, setResult] = useState<AiConvertResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  // --- Ollama ---
  const [ollamaStatus, setOllamaStatus] = useState<OllamaStatusResult | null>(null);
  const [ollamaModels, setOllamaModels] = useState<OllamaModelResult[]>([]);
  const [listingModels, setListingModels] = useState(false);

  // Detectar estado al montar (markItDown + Ollama en paralelo).
  useEffect(() => {
    let cancelled = false;
    const detectAll = async () => {
      const [aiResult, ollamaResult] = await Promise.allSettled([
        bridge.call<AiStatusResult>(OperationIds.aiDetect, undefined, 15_000),
        bridge.call<OllamaStatusResult>(OperationIds.ollamaDetect, undefined, 15_000),
      ]);
      if (!cancelled) {
        if (aiResult.status === 'fulfilled') setStatus(aiResult.value);
        if (ollamaResult.status === 'fulfilled') setOllamaStatus(ollamaResult.value);
      }
    };
    detectAll();
    return () => { cancelled = true; };
  }, []);

  const installMarkItDown = async () => {
    setError(null);
    setNotice(null);
    setInstalling(true);
    try {
      const outcome = await bridge.call<AiMutationResult>(OperationIds.aiInstall, undefined, 10 * 60_000);
      if (outcome.succeeded) {
        setNotice('markItDown instalado correctamente.');
        const s = await bridge.call<AiStatusResult>(OperationIds.aiDetect, undefined, 15_000);
        setStatus(s);
      } else {
        setError(outcome.detail ?? 'La instalación falló.');
      }
    } catch (e) {
      setError(e instanceof BridgeError ? e.message : 'No se pudo instalar markItDown.');
    } finally {
      setInstalling(false);
    }
  };

  const listOllamaModels = async () => {
    setNotice(null);
    setListingModels(true);
    try {
      const collection = await bridge.call<OllamaModelCollectionResult>(
        OperationIds.ollamaListModels,
        undefined,
        15_000,
      );
      setOllamaModels(collection.models ?? []);
      if (collection.models.length === 0) {
        setNotice('Ollama no tiene modelos instalados. Usa "ollama pull <modelo>" desde la terminal.');
      }
    } catch (e) {
      setError(e instanceof BridgeError ? e.message : 'No se pudo listar los modelos de Ollama.');
    } finally {
      setListingModels(false);
    }
  };

  const pickFile = async () => {
    setError(null);
    setNotice(null);
    setResult(null);
    try {
      const resultPick = await bridge.call<{ cancelled: boolean; paths: string[] }>(
        OperationIds.systemPickFiles,
        { multiselect: false },
        10 * 60_000,
      );
      if (!resultPick.cancelled && resultPick.paths.length > 0) {
        setSelectedPath(resultPick.paths[0]);
      }
    } catch {
      // best-effort
    }
  };

  const convert = async () => {
    if (!selectedPath || converting) return;
    setError(null);
    setNotice(null);
    setResult(null);
    setConverting(true);
    try {
      const payload: AiConvertPayload = { path: selectedPath };
      const outcome = await bridge.call<AiConvertResult>(OperationIds.aiConvert, payload, 10 * 60_000);
      setResult(outcome);
      if (!outcome.succeeded) {
        setError(outcome.detail ?? 'La conversión falló.');
      } else {
        setNotice('Archivo Markdown generado junto al original.');
      }
    } catch (e) {
      setError(e instanceof BridgeError ? e.message : 'No se pudo convertir el archivo.');
    } finally {
      setConverting(false);
    }
  };

  const openContainingFolder = async () => {
    if (!result?.outputPath) return;
    const folder = result.outputPath.substring(0, result.outputPath.lastIndexOf('\\') + 1) ||
                   result.outputPath.substring(0, result.outputPath.lastIndexOf('/') + 1);
    try {
      await bridge.call(OperationIds.explorerOpenPath, { path: folder });
      setNotice('Carpeta abierta en el Explorador.');
    } catch {
      setNotice(null);
    }
  };

  const copyOutputPath = async () => {
    if (!result?.outputPath) return;
    try {
      await bridge.call(OperationIds.systemWriteClipboard, { text: result.outputPath });
      setNotice('Ruta copiada al portapapeles.');
    } catch {
      setNotice(null);
    }
  };

  const clearResult = () => {
    setResult(null);
    setError(null);
    setNotice(null);
    setSelectedPath(null);
  };

  const aiInstalled = status?.installed === true;
  const canConvert = aiInstalled && !!selectedPath && !converting;
  const ollamaInstalled = ollamaStatus?.installed === true;

  return (
    <div className="space-y-6">
      <header>
        <h1 className="text-2xl font-semibold">{t('sidebar.ai')}</h1>
        <p className="mt-1 text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
          {t('pages.ai.subtitle')}
        </p>
      </header>

      {/* Estado de detección (markItDown) */}
      {status === null && ollamaStatus === null && (
        <p className="text-sm" style={{ color: 'var(--pcs-text-muted)' }}>Detectando entorno…</p>
      )}

      {/* ==================== markItDown ==================== */}
      <section className="rounded-lg border p-4" style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}>
        <div className="flex items-center gap-2">
          <Terminal size={16} aria-hidden style={{ color: 'var(--pcs-accent)' }} />
          <h3 className="text-sm font-medium">markItDown — archivos a Markdown</h3>
        </div>

        {!aiInstalled && status !== null && (
          <div className="mt-3 flex flex-col gap-2">
            <p className="text-sm" style={{ color: 'var(--pcs-text)' }}>
              markItDown no está instalado. Se requiere Python + markItDown para
              convertir archivos a Markdown.
            </p>
            {status.detail && (
              <p className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>{status.detail}</p>
            )}
            <button
              type="button"
              onClick={() => void installMarkItDown()}
              disabled={installing}
              className="flex items-center justify-center gap-2 rounded-md px-3 py-2 text-sm font-medium transition-colors disabled:opacity-40 disabled:cursor-not-allowed"
              style={{ background: 'var(--pcs-accent)', color: '#fff' }}
            >
              {installing ? <RotateCcw size={14} className="animate-spin" aria-hidden /> : <Download size={14} aria-hidden />}
              {installing ? 'Instalando…' : 'Instalar markItDown'}
            </button>
          </div>
        )}

        {aiInstalled && status !== null && (
          <div className="mt-3 flex flex-col gap-2">
            <p className="text-sm" style={{ color: 'var(--pcs-text)' }}>
              Python {status.pythonVersion ?? ''} + markItDown {status.markItDownVersion ?? ''} detectados.
            </p>
            <div className="mt-2 flex gap-2">
              <button
                type="button"
                onClick={() => void pickFile()}
                disabled={converting}
                className="flex items-center gap-2 rounded-md px-3 py-2 text-sm font-medium transition-colors disabled:opacity-40 disabled:cursor-not-allowed"
                style={{ background: 'var(--pcs-accent)', color: '#fff' }}
              >
                <FileText size={14} aria-hidden />
                Seleccionar archivo…
              </button>
              {selectedPath && (
                <button
                  type="button"
                  onClick={() => void clearResult()}
                  className="cursor-pointer rounded-md px-3 py-2 text-xs font-medium"
                  style={{ background: 'var(--pcs-surface-2)', color: 'var(--pcs-text-muted)' }}
                >
                  Cambiar archivo
                </button>
              )}
            </div>
            {selectedPath && (
              <p className="mt-2 text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
                Archivo: <code className="truncate block" title={selectedPath}>{selectedPath}</code>
              </p>
            )}
          </div>
        )}
      </section>

      {/* Acción de conversión (markItDown) */}
      {aiInstalled && status !== null && (
        <section className="rounded-lg border p-4" style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}>
          <div className="flex items-center justify-between gap-3">
            <div>
              <h3 className="text-sm font-medium">Convertir a Markdown</h3>
              <p className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
                Ejecuta markItDown sobre el archivo seleccionado. Puede tardar en PDFs grandes o audio.
              </p>
            </div>
            <button
              type="button"
              onClick={() => void convert()}
              disabled={!canConvert}
              className="flex items-center gap-2 rounded-md px-3 py-2 text-sm font-medium transition-colors disabled:opacity-40 disabled:cursor-not-allowed"
              style={{ background: 'var(--pcs-accent)', color: '#fff' }}
            >
              {converting ? <RotateCcw size={14} className="animate-spin" aria-hidden /> : <Play size={14} aria-hidden />}
              {converting ? 'Convirtiendo…' : 'Convertir'}
            </button>
          </div>
        </section>
      )}

      {/* Resultado markItDown */}
      {result && (
        <section className="space-y-3">
          <div className="flex items-center gap-2 rounded-lg border p-3" style={{ background: 'var(--pcs-surface-2)', borderColor: 'var(--pcs-border)' }}>
            {result.succeeded
              ? <CheckCircle2 size={16} aria-hidden style={{ color: 'var(--pcs-success)' }} />
              : <AlertTriangle size={16} aria-hidden style={{ color: 'var(--pcs-warning, #d97706)' }} />}
            <h3 className="text-sm font-medium">
              {result.succeeded ? 'Archivo Markdown generado' : 'La conversión falló'}
            </h3>
          </div>

          {result.succeeded && result.outputPath && (
            <>
              <div className="rounded-lg border p-4 font-mono text-xs overflow-auto max-h-96 break-all" style={{
                background: 'var(--pcs-bg, #0f172a)',
                borderColor: 'var(--pcs-border)',
                color: 'var(--pcs-text)',
              }}>
                {result.outputPath}
              </div>
              <div className="flex gap-2 mt-3">
                <button
                  type="button"
                  onClick={() => void openContainingFolder()}
                  className="cursor-pointer rounded-md px-3 py-2 text-sm font-medium transition-colors hover:bg-(--pcs-surface-2)"
                  style={{ background: 'var(--pcs-accent)', color: '#fff' }}
                >
                  <FolderOpen size={14} aria-hidden /> Abrir carpeta
                </button>
                <button
                  type="button"
                  onClick={() => void copyOutputPath()}
                  className="cursor-pointer rounded-md px-3 py-2 text-sm font-medium transition-colors hover:bg-(--pcs-surface-2)"
                  style={{ background: 'var(--pcs-surface-2)', color: 'var(--pcs-text-muted)' }}
                >
                  <Copy size={14} aria-hidden /> Copiar ruta
                </button>
                <button
                  type="button"
                  onClick={() => void setResult(null)}
                  className="cursor-pointer rounded-md px-3 py-2 text-sm font-medium transition-colors hover:bg-(--pcs-surface-2)"
                  style={{ background: 'var(--pcs-surface-2)', color: 'var(--pcs-text-muted)' }}
                >
                  Descartar
                </button>
              </div>
            </>
          )}

          {!result.succeeded && result.detail && (
            <p className="text-sm" style={{ color: 'var(--pcs-text)' }}>{result.detail}</p>
          )}
        </section>
      )}

      {/* ==================== Ollama ==================== */}
      <section className="rounded-lg border p-4" style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}>
        <div className="flex items-center gap-2">
          <Terminal size={16} aria-hidden style={{ color: 'var(--pcs-accent)' }} />
          <h3 className="text-sm font-medium">Ollama — modelos locales</h3>
        </div>

        {ollamaStatus === null && (
          <p className="mt-2 text-xs" style={{ color: 'var(--pcs-text-muted)' }}>Detectando Ollama…</p>
        )}

        {!ollamaInstalled && ollamaStatus !== null && (
          <div className="mt-3 flex flex-col gap-2">
            <p className="text-sm" style={{ color: 'var(--pcs-text)' }}>
              Ollama no está instalado. Para usar modelos locales, instálalo desde
              <a href="https://ollama.com" target="_blank" rel="noopener noreferrer" className="text-(--pcs-accent) hover:underline">ollama.com</a>.
            </p>
            {ollamaStatus.detail && (
              <p className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>{ollamaStatus.detail}</p>
            )}
          </div>
        )}

        {ollamaInstalled && ollamaStatus !== null && (
          <div className="mt-3 flex flex-col gap-2">
            <p className="text-sm" style={{ color: 'var(--pcs-text)' }}>
              Ollama {ollamaStatus.version ?? ''} detectado.
            </p>
            <div className="flex gap-2">
              <button
                type="button"
                onClick={() => void listOllamaModels()}
                disabled={listingModels}
                className="flex items-center gap-2 rounded-md px-3 py-2 text-sm font-medium transition-colors disabled:opacity-40 disabled:cursor-not-allowed"
                style={{ background: 'var(--pcs-accent)', color: '#fff' }}
              >
                {listingModels ? <RotateCcw size={14} className="animate-spin" aria-hidden /> : <Terminal size={14} aria-hidden />}
                {listingModels ? 'Cargando…' : 'Listar modelos'}
              </button>
            </div>
            {ollamaModels.length > 0 && (
              <div className="mt-2">
                <p className="text-xs font-semibold mb-1" style={{ color: 'var(--pcs-text-muted)' }}>Modelos instalados:</p>
                <ul className="space-y-1">
                  {ollamaModels.map((model) => (
                    <li key={model.name} className="flex items-center justify-between px-2 py-1.5 rounded bg-(--pcs-surface-2) text-xs border" style={{ borderColor: 'var(--pcs-border)' }}>
                      <span className="font-mono truncate" title={model.name}>{model.name}</span>
                      <span className="text-(--pcs-text-muted)">{model.size ?? ''}</span>
                    </li>
                  ))}
                </ul>
              </div>
            )}
            {ollamaModels.length === 0 && !listingModels && (
              <p className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
                No hay modelos instalados. Usa{' '}
                <code className="font-mono">{'ollama pull <modelo>'}</code>
                desde la terminal.
              </p>
            )}
          </div>
        )}
      </section>

      {error && (
        <section className="flex items-start gap-2 rounded-lg border p-4 text-sm" style={{ borderColor: 'color-mix(in srgb, var(--pcs-danger) 55%, transparent)', background: 'var(--pcs-surface)' }}>
          <AlertTriangle size={16} aria-hidden className="mt-0.5 shrink-0" style={{ color: 'var(--pcs-danger)' }} />
          <p style={{ color: 'var(--pcs-text)' }}>{error}</p>
        </section>
      )}

      {notice && (
        <p className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>{notice}</p>
      )}
    </div>
  );
}
