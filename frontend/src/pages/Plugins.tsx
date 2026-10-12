import { useCallback, useEffect, useState } from 'react';
import { bridge } from '../services/pcsuiteBridge';
import { OperationIds } from '../types/bridge';
import type { PluginCollectionResult, PluginOperationResult, PluginMetadata } from '../types/bridge';
import { Sparkles, RefreshCw, CheckCircle2 } from 'lucide-react';

/**
 * Página "Plugins": catálogo y gestión de complementos extensibles.
 *
 * Muestra un catálogo de plugins instalados obtenidos mediante el puente
 * `plugin.list`, con acciones de checkUpdate y launch. Incluye estado vacío,
 * manejo de errores y directivas de extensión.
 */

const PLUGIN_TIMEOUT_MS = 15_000;

export function Plugins() {
  const [loading, setLoading] = useState(true);
  const [plugins, setPlugins] = useState<PluginMetadata[]>([]);
  const [checkingUpdate, setCheckingUpdate] = useState<string | null>(null); // pluginId en proceso

  const load = useCallback(async () => {
    setLoading(true);
    setPlugins([]);
    try {
      // 1. Cargar lista de plugins desde el backend
      const result = await bridge.call<PluginCollectionResult>(
        OperationIds.pluginList,
        undefined,
        PLUGIN_TIMEOUT_MS,
      );
      setPlugins(result?.plugins ?? []);
    } catch (caught) {
      // Best-effort: un fallo de lectura del historial no bloquea la página.
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  // Helper: verificar si ya hay una comprobación en progreso para un plugin
  const isCheckingUpdate = (pid: string) => checkingUpdate === pid;

  // Helper: obtener color de badge según estado
  const getBadgeColor = (status: string) => {
    switch (status) {
      case 'active': return 'var(--pcs-success)';
      case 'inactive': return 'var(--pcs-text-muted)';
      case 'error': return 'var(--pcs-danger)';
      default: return 'var(--pcs-text-muted)';
    }
  };

  // Helper: obtener texto del estado
  const getStatusText = (status: string) => {
    switch (status) {
      case 'active': return 'Activo';
      case 'inactive': return 'Inactivo';
      case 'error': return 'Error';
      default: return 'Desconocido';
    }
  };

  // Ejecutar checkUpdate para un plugin específico
  const handleCheckUpdate = async (pluginId: string) => {
    // Si ya hay una comprobación en progreso, ignorar
    if (isCheckingUpdate(pluginId)) return;

    setCheckingUpdate(pluginId);
    try {
      const result = await bridge.call<PluginOperationResult>(
        OperationIds.pluginCheckUpdate,
        { pluginId },
        PLUGIN_TIMEOUT_MS,
      );

      if (result?.succeeded) {
        // Actualizar UI localmente con los nuevos datos
        setPlugins((prev) =>
          prev.map((p) =>
            p.id === pluginId
              ? { ...p, updateAvailable: result.latestVersion !== undefined, availableVersion: result.latestVersion ?? p.availableVersion }
              : p
          )
        );
      }
    } catch (error) {
      // Manejo de error sin stack trace en UI
    } finally {
      setCheckingUpdate(null);
    }
  };

  // Ejecutar launch para un plugin específico
  const handleLaunch = async (pluginId: string) => {
    if (isCheckingUpdate(pluginId)) return;

    setCheckingUpdate(pluginId);
    try {
      await bridge.call<PluginOperationResult>(
        OperationIds.pluginLaunch,
        { pluginId },
        PLUGIN_TIMEOUT_MS,
      );
    } catch (error) {
      // Manejo de error
    } finally {
      setCheckingUpdate(null);
    }
  };

  if (loading) {
    return (
      <p className="text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
        Cargando información de plugins…
      </p>
    );
  }

  return (
    <div className="space-y-6">
      <header className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold">Plugins</h1>
          <p className="mt-1 text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
            Sistema de complementos extensibles para PCSuite. Los plugins
            permiten ampliar las funcionalidades de la aplicación con nuevos
            herramientas y capacidades.
          </p>
        </div>
        <Sparkles size={16} aria-hidden style={{ color: 'var(--pcs-accent)' }} />
      </header>

      {/* Sección Catálogo */}
      <section
        className="rounded-lg border p-6"
        style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
      >
        <h2 className="text-lg font-medium mb-4">Catálogo de plugins</h2>
        
        {plugins.length === 0 ? (
          <div className="p-4 text-center text-sm" style={{ color: 'var(--pcs-text-muted)' }}>
            No hay plugins instalados.
            <br />
            <span className="font-medium" style={{ cursor: 'pointer', color: 'var(--pcs-accent)' }} onClick={() => load()}>
              Actualizar lista
            </span>
          </div>
        ) : (
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {plugins.map((plugin) => {
              const status = plugin.status;
              const badgeColor = getBadgeColor(status);
              const statusText = getStatusText(status);
              
              return (
                <div
                  key={plugin.id}
                  className="group rounded-lg border p-4 transition-colors pcs-stagger-in"
                  style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
                >
                  <div className="flex items-start gap-3">
                    {/* Icono según estado */}
                    <div className="w-8 h-8 rounded-md flex items-center justify-center flex-shrink-0 group-hover:bg-gray-200 transition-colors"
                         style={{ background: `color-mix(in srgb, ${badgeColor} 15%, transparent)` }}>
                      <Sparkles size={16} aria-hidden style={{ color: badgeColor }} />
                    </div>
                    
                    <div className="flex-1 min-w-0">
                      <p className="font-medium truncate">{plugin.name}</p>
                      <p className="text-xs opacity-75">{plugin.id}</p>
                      <p className="text-xs font-medium">
                        <span style={{ color: 'var(--pcs-text-muted)' }}>Versión actual: </span>
                        <span style={{ color: status === 'active' ? 'var(--pcs-success)' : 'var(--pcs-warning)' }}>
                          {plugin.version}
                        </span>
                        {plugin.updateAvailable && plugin.availableVersion ? (
                          <span style={{ marginLeft: '4px', color: 'var(--pcs-success)' }}> ({plugin.availableVersion} disponible)</span>
                        ) : ''}
                      </p>
                    </div>
                  </div>
                  
                  <div className="mt-2 flex items-center gap-2">
                    {/* Estado badge */}
                    <span className="px-2 py-1 text-xs rounded-full font-medium"
                          style={{ background: `color-mix(in srgb, ${badgeColor} 15%, transparent)`, color: badgeColor }}>
                      {statusText}
                    </span>
                    
                    {/* Acciones: CheckUpdate y Launch */}
                    <div className="flex gap-1">
                      {/* Botón CheckUpdate */}
                      <button
                        type="button"
                        onClick={() => handleCheckUpdate(plugin.id)}
                        disabled={isCheckingUpdate(plugin.id)}
                        className="flex items-center gap-1 px-2 py-1 rounded-md text-xs font-medium transition-colors disabled:opacity-40 pcs-press-feedback"
                        style={{
                          background: isCheckingUpdate(plugin.id) ? 'var(--pcs-surface-2)' : 'var(--pcs-accent)',
                          color: isCheckingUpdate(plugin.id) ? 'var(--pcs-text-muted)' : '#fff',
                          cursor: isCheckingUpdate(plugin.id) ? 'not-allowed' : 'pointer',
                        }}
                        title={isCheckingUpdate(plugin.id) ? 'Comprobando…' : 'Comprobar actualización'}
                      >
                        {isCheckingUpdate(plugin.id) ? (
                          <RefreshCw size={12} aria-hidden className="animate-spin" />
                        ) : (
                          <CheckCircle2 size={12} aria-hidden />
                        )}
                        <span className="hidden sm:inline">Actualizar</span>
                      </button>
                      
                      {/* Botón Launch */}
                      <button
                        type="button"
                        onClick={() => handleLaunch(plugin.id)}
                        disabled={isCheckingUpdate(plugin.id)}
                        className="flex items-center gap-1 px-2 py-1 rounded-md text-xs font-medium transition-colors disabled:opacity-40 pcs-press-feedback"
                        style={{
                          background: 'var(--pcs-surface-2)',
                          color: 'var(--pcs-text)',
                          cursor: 'pointer',
                        }}
                        title="Lanzar plugin"
                      >
                        <Sparkles size={12} aria-hidden />{' '}
                        <span className="hidden sm:inline">Lanzar</span>
                      </button>
                    </div>
                  </div>
                </div>
              );
            })}
          </div>
        )}

        {/* Cuando hay plugins pero algunos fallaron el load */}
        {plugins.length > 0 && (
          <p className="mt-2 text-xs opacity-60">
            {plugins.length} plugin(s) cargado(s)
          </p>
        )}

      </section>

      {/* Sección Directivas */}
      <section
        className="rounded-lg border p-6"
        style={{ background: 'var(--pcs-surface)', borderColor: 'var(--pcs-border)' }}
      >
        <h2 className="text-lg font-medium mb-4">Directivas de extensión</h2>
        <ul className="space-y-3 text-sm">
          <li>
            <p className="font-medium">Añadir nuevo plugin:</p>
            <p className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
              Los plugins se distribuyen como assemblies .NET y se descubren
              automáticamente en el directorio <code className="font-mono">plugins/</code> al iniciar PCSuite.
            </p>
          </li>
          <li>
            <p className="font-medium">Comprobar actualizaciones:</p>
            <p className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
              Seleccione un plugin de la lista y pulse <strong>Actualizar</strong> para verificar
              si hay versión disponible. La comprobación usa el servicio
              <code className="font-mono">IPluginService.CheckUpdate</code> con timeout de 15 segundos.
            </p>
          </li>
          <li>
            <p className="font-medium">Lanzar plugin:</p>
            <p className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
              Pulse <strong>Lanzar</strong> para ejecutar la acción principal del plugin.
              Cada plugin define su propia lógica de lanzamiento a través de la
              interfaz <code className="font-mono">IPlugin</code>.
            </p>
          </li>
          <li>
            <p className="font-medium">Estado de los indicadores:</p>
            <p className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
              <span className="font-medium">Activo (green):</span> plugin cargado y operativo.<br />
              <span className="font-medium">Inactivo (gray):</span> plugin instalado pero desactivado.<br />
              <span className="font-medium">Error (red):</span> ocurrió un problema al cargar el plugin.
            </p>
          </li>
        </ul>
      </section>

      <footer
        className="flex flex-col sm:flex-row items-center justify-between gap-2 mt-6 pt-4 border-t"
        style={{ borderColor: 'var(--pcs-border)' }}
      >
        <p className="text-xs" style={{ color: 'var(--pcs-text-muted)' }}>
          Versión 0.1.0 — Sistema de plugins en desarrollo
        </p>
        <button
          type="button"
          onClick={() => load()}
          className="flex items-center gap-2 rounded-md px-2 py-1.5 text-xs font-medium transition-colors pcs-press-feedback"
          style={{ background: 'var(--pcs-accent)', color: '#fff' }}
        >
          <RefreshCw size={12} aria-hidden /> Actualizar lista
        </button>
      </footer>
    </div>
  );
}