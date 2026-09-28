import { useCallback, useEffect, useState } from 'react';
import { RefreshCw, Sparkles, Trash2 } from 'lucide-react';
import { bridge, BridgeError } from '../services/pcsuiteBridge';
import { OperationIds } from '../types/bridge';
import type {
  CleanerCategoryScanResult,
  CleanerCleanResult,
  CleanerScanResult,
} from '../types/bridge';

/**
 * Página "Limpiador" (Task 011): escaneo de las 3 raíces temporales fijas
 * (Temp del usuario, C:\Windows\Temp y la caché de Windows Update), revisión
 * de tamaños con selección, confirmación explícita y resultado de la limpieza.
 *
 * Seguridad (§12 / §53): el frontend solo envía ids de categoría de la lista
 * fija ({"categories":[ids]}) — jamás rutas ni argumentos; la validación y las
 * raíces viven en .NET. cleaner.scan/clean no piden elevación: lo que la ACL
 * no permite se omite y se reporta (skippedFiles + detail), nunca rompe la
 * operación. Sin stack traces: los fallos vuelven como mensaje estructurado
 * del puente (p. ej. CLEANER_FAILED).
 */

// Timeouts del lado del cliente, por encima de los del nativo (sin timeout
// propio el servicio: 45 s al escaneo y 5 min a la limpieza, por red/ACL).
const READ_TIMEOUT_MS = 45_000;
const CLEAN_TIMEOUT_MS = 300_000;

interface Notice {
  kind: 'success' | 'error';
  text: string;
}

function errorMessage(error: unknown, fallback: string): string {
  return error instanceof BridgeError ? error.message : fallback;
}

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  const units = ['KB', 'MB', 'GB', 'TB'];
  let value = bytes;
  let unit = -1;
  do {
    value /= 1024;
    unit += 1;
  } while (value >= 1024 && unit < units.length - 1);
  return `${value.toFixed(value >= 100 ? 0 : 1)} ${units[unit]}`;
}

export function Cleaner() {
  const [scan, setScan] = useState<CleanerScanResult | null>(null);
  const [scanning, setScanning] = useState(true);
  const [scanError, setScanError] = useState<string | null>(null);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [busy, setBusy] = useState<'clean' | null>(null);
  const [notice, setNotice] = useState<Notice | null>(null);
  const [lastClean, setLastClean] = useState<CleanerCleanResult | null>(null);

  const runScan = useCallback(async () => {
    setScanning(true);
    setScanError(null);
    try {
      const result = await bridge.call<CleanerScanResult>(
        OperationIds.cleanerScan,
        undefined,
        READ_TIMEOUT_MS,
      );
      const categories = result.categories ?? [];
      setScan({ categories });
      // En el primer escaneo marca lo accesible; después respeta la selección.
      setSelected((current) =>
        current.size > 0
          ? current
          : new Set(
              categories.filter((category) => category.accessible).map((category) => category.id),
            ),
      );
    } catch (error) {
      setScanError(errorMessage(error, 'No se pudieron escanear las carpetas temporales.'));
    } finally {
      setScanning(false);
    }
  }, []);

  useEffect(() => {
    void runScan();
  }, [runScan]);

  const toggle = (id: string) => {
    setSelected((current) => {
      const next = new Set(current);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  const categories = scan?.categories ?? [];
  const accessible = categories.filter((category) => category.accessible);
  const allSelected = accessible.length > 0 && accessible.every((c) => selected.has(c.id));
  const totalSelected = accessible
    .filter((category) => selected.has(category.id))
    .reduce((sum, category) => sum + category.sizeBytes, 0);

  const runClean = async () => {
    const ids = [...selected];
    if (ids.length === 0 || busy) return;
    if (
      !window.confirm(
        `¿Borrar los archivos temporales de ${ids.length} ubicación(es) (${formatBytes(totalSelected)})? Los archivos en uso o sin permiso se omitirán.`,
      )
    ) {
      return;
    }
    setBusy('clean');
    setNotice(null);
    try {
      // Solo ids de la lista fija; .NET valida contra el mapa cerrado.
      const result = await bridge.call<CleanerCleanResult>(
        OperationIds.cleanerClean,
        { categories: ids },
        CLEAN_TIMEOUT_MS,
      );
      setLastClean(result);
      const freed = result.categories.reduce((sum, category) => sum + category.freedBytes, 0);
      const skipped = result.categories.reduce((sum, category) => sum + category.skippedFiles, 0);
      setNotice({
        kind: 'success',
        text:
          skipped > 0
            ? `Limpieza terminada: ${formatBytes(freed)} liberados, ${skipped} fichero(s) omitidos.`
            : `Limpieza terminada: ${formatBytes(freed)} liberados.`,
      });
      await runScan();
    } catch (error) {
      setNotice({ kind: 'error', text: errorMessage(error, 'La limpieza falló.') });
    } finally {
      setBusy(null);
    }
  };

  const labelOf = (id: string) => categories.find((category) => category.id === id)?.label ?? id;
  const busyClean = busy === 'clean';

  return (
    <div className="space-y-6">
      <header className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold">Limpiador</h1>
          <p className="mt-1 text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
            Archivos temporales del usuario, de Windows y de la caché de Windows Update — solo
            categorías fijas seleccionables, nunca rutas.
          </p>
        </div>
        <button
          type="button"
          onClick={() => void runScan()}
          disabled={scanning || busyClean}
          className="flex items-center gap-2 rounded-md px-3 py-2 text-sm font-medium transition-colors disabled:opacity-50"
          style={{ background: 'var(--pcs-accent)', color: '#fff' }}
        >
          <RefreshCw size={14} aria-hidden className={scanning ? 'animate-spin' : undefined} />
          Escanear
        </button>
      </header>

      {scanning && (
        <p className="text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
          Escaneando carpetas temporales…
        </p>
      )}

      {scanError && (
        <div
          className="rounded-md border px-4 py-3 text-sm"
          style={{ borderColor: 'var(--pcs-danger)', color: 'var(--pcs-danger)' }}
          role="alert"
        >
          {scanError}
        </div>
      )}

      {notice && (
        <div
          className="rounded-md border px-4 py-3 text-sm"
          style={
            notice.kind === 'success'
              ? { borderColor: 'var(--pcs-success)', color: 'var(--pcs-success)' }
              : { borderColor: 'var(--pcs-danger)', color: 'var(--pcs-danger)' }
          }
          role={notice.kind === 'error' ? 'alert' : 'status'}
        >
          {notice.text}
        </div>
      )}

      {/* Categorías escaneadas */}
      {!scanning && scan && (
        <section
          className="rounded-lg border p-4"
          style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
          aria-label="Categorías de archivos temporales"
        >
          <div className="flex items-center justify-between gap-3">
            <label className="flex cursor-pointer items-center gap-2 text-sm font-medium">
              <input
                type="checkbox"
                checked={allSelected}
                onChange={() =>
                  setSelected(allSelected ? new Set() : new Set(accessible.map((c) => c.id)))
                }
                style={{ accentColor: 'var(--pcs-accent)' }}
              />
              Seleccionar todas
            </label>
            <span className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
              {formatBytes(totalSelected)} seleccionados
            </span>
          </div>

          <ul className="mt-3 space-y-2">
            {categories.map((category) => (
              <CategoryRow
                key={category.id}
                category={category}
                checked={selected.has(category.id)}
                disabled={busyClean}
                onToggle={() => toggle(category.id)}
              />
            ))}
          </ul>

          <div className="mt-4 flex items-center gap-3">
            <button
              type="button"
              onClick={() => void runClean()}
              disabled={busyClean || selected.size === 0}
              className="flex shrink-0 items-center gap-2 whitespace-nowrap rounded-md px-3 py-2 text-sm font-medium transition-colors disabled:opacity-40"
              style={{
                background: 'color-mix(in srgb, var(--pcs-danger) 14%, transparent)',
                color: 'var(--pcs-danger)',
              }}
            >
              <Trash2 size={14} aria-hidden />
              {busyClean ? 'Limpiando…' : 'Limpiar seleccionadas'}
            </button>
            <span className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
              Sin elevación: lo que no se pueda borrar se omite y se reporta.
            </span>
          </div>
        </section>
      )}

      {/* Resultado de la última limpieza */}
      {!scanning && lastClean && (
        <section
          className="rounded-lg border p-4"
          style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
          aria-label="Resultado de la última limpieza"
        >
          <h2 className="flex items-center gap-2 text-sm font-semibold">
            <Sparkles size={14} aria-hidden />
            Última limpieza
          </h2>
          <ul className="mt-2 divide-y" style={{ borderColor: 'var(--pcs-border)' }}>
            {lastClean.categories.map((category) => (
              <li key={category.id} className="flex flex-wrap items-center gap-x-3 gap-y-1 py-2 text-sm">
                <span className="font-medium">{labelOf(category.id)}</span>
                <span className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
                  {category.deletedFiles} borrado(s) · {formatBytes(category.freedBytes)} liberados
                </span>
                {category.skippedFiles > 0 && (
                  <span
                    className="rounded-full px-2 py-0.5 text-xs font-medium"
                    style={{
                      background: 'color-mix(in srgb, var(--pcs-danger) 14%, transparent)',
                      color: 'var(--pcs-danger)',
                    }}
                  >
                    {category.skippedFiles} omitido(s)
                  </span>
                )}
                {category.detail && (
                  <p className="w-full text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
                    {category.detail}
                  </p>
                )}
              </li>
            ))}
          </ul>
        </section>
      )}
    </div>
  );
}

function CategoryRow(props: {
  category: CleanerCategoryScanResult;
  checked: boolean;
  disabled: boolean;
  onToggle: () => void;
}) {
  const { category } = props;
  return (
    <li
      className="flex items-center gap-3 rounded-md border px-3 py-2"
      style={{
        borderColor: 'var(--pcs-border)',
        background: 'var(--pcs-surface-2)',
        opacity: category.accessible ? 1 : 0.6,
      }}
    >
      <input
        type="checkbox"
        checked={props.checked}
        disabled={props.disabled || !category.accessible}
        onChange={props.onToggle}
        aria-label={`Seleccionar ${category.label}`}
        style={{ accentColor: 'var(--pcs-accent)' }}
      />
      <div className="min-w-0 flex-1">
        <p className="truncate text-sm font-medium">{category.label}</p>
        <p
          className="truncate font-mono text-xs"
          style={{ color: 'var(--pcs-text-muted)' }}
          title={category.path}
        >
          {category.path}
        </p>
      </div>
      {!category.accessible ? (
        <span
          className="rounded-full px-2 py-0.5 text-xs font-medium"
          style={{
            background: 'color-mix(in srgb, var(--pcs-danger) 14%, transparent)',
            color: 'var(--pcs-danger)',
          }}
        >
          Sin permisos
        </span>
      ) : (
        <span className="whitespace-nowrap text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
          {category.fileCount} fichero(s) · {formatBytes(category.sizeBytes)}
        </span>
      )}
    </li>
  );
}
