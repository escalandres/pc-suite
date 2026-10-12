using System.Text.Json;

namespace PCSuite.Infrastructure.LocalShare;

/// <summary>
/// Estado de archivos parcialmente recibidos (Task 016.7.3 — reanudación).
/// Un "partial" es un prefijo secuencial de un archivo: (peer, ruta) →
/// (temp, bytes). Para streaming secuencial, "bytes recibidos" ES el
/// manifiesto de bloques completados — un prefijo no necesita mapa por
/// bloque. La integridad final la garantiza Publish (SHA-256 del archivo
/// completo), de modo que un desalineo de offset nunca publica basura.
///
/// Persistencia: partials.json en el directorio de datos del servicio
/// (no en la carpeta de recepción: el usuario no debe verlo).
/// </summary>
public sealed class PartialsStore
{
    private readonly string _path;
    private readonly object _lock = new();
    private readonly Dictionary<string, Dictionary<string, PartialEntry>> _state = new(StringComparer.Ordinal);

    public sealed record PartialEntry
    {
        public string TempPath { get; init; } = "";
        public long Bytes { get; set; }
        public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    }

    public PartialsStore(string storageDirectory)
    {
        _path = Path.Combine(storageDirectory, "partials.json");
        Load();
    }

    // ------------------------------------------------------------------
    // Consulta y mutación (el lock externo del servicio ya serializa el
    // acceso por conexión; este lock protege la persistencia cruzada)
    // ------------------------------------------------------------------

    /// <summary>Offset ya recibido de (peer, relativePath), o null si no hay estado resumible.</summary>
    public PartialEntry? Get(string peerDeviceId, string relativePath)
    {
        lock (_lock)
        {
            return _state.TryGetValue(peerDeviceId, out var byPath)
                && byPath.TryGetValue(relativePath, out var entry)
                    ? entry
                    : null;
        }
    }

    /// <summary>Registra/actualiza el progreso de un archivo en curso.</summary>
    public void Set(string peerDeviceId, string relativePath, PartialEntry entry)
    {
        lock (_lock)
        {
            if (!_state.TryGetValue(peerDeviceId, out var byPath))
            {
                byPath = new Dictionary<string, PartialEntry>(StringComparer.Ordinal);
                _state[peerDeviceId] = byPath;
            }

            byPath[relativePath] = entry;
        }
    }

    /// <summary>Elimina el estado de un archivo (publicado, descartado o inválido).</summary>
    public void Remove(string peerDeviceId, string relativePath)
    {
        lock (_lock)
        {
            if (_state.TryGetValue(peerDeviceId, out var byPath))
            {
                byPath.Remove(relativePath);
                if (byPath.Count == 0)
                {
                    _state.Remove(peerDeviceId);
                }
            }
        }
    }

    /// <summary>Temporales vivos referenciados por el manifiesto (para no borrarlos al arrancar).</summary>
    public IReadOnlySet<string> ActiveTempFiles()
    {
        lock (_lock)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var byPath in _state.Values)
            {
                foreach (var entry in byPath.Values)
                {
                    set.Add(entry.TempPath);
                }
            }

            return set;
        }
    }

    /// <summary>
    /// Poda de entradas inválidas: temporales desaparecidos o más antiguos
    /// que maxAge. Los archivos de los pares no cambian solos; una entrada
    /// vieja solo ocupa espacio. Devuelve los temps podidos (para borrarlos).
    /// </summary>
    public IReadOnlyList<string> Prune(TimeSpan maxAge)
    {
        var removedTemps = new List<string>();
        var now = DateTimeOffset.UtcNow;

        lock (_lock)
        {
            var peerIds = _state.Keys.ToList();
            foreach (var peerId in peerIds)
            {
                var byPath = _state[peerId];
                var keys = byPath.Keys.ToList();
                foreach (var relPath in keys)
                {
                    var entry = byPath[relPath];
                    var stale = !File.Exists(entry.TempPath) || now - entry.UpdatedAtUtc > maxAge;
                    if (stale)
                    {
                        byPath.Remove(relPath);
                        if (File.Exists(entry.TempPath))
                        {
                            removedTemps.Add(entry.TempPath);
                        }
                    }
                }

                if (byPath.Count == 0)
                {
                    _state.Remove(peerId);
                }
            }

            SaveLocked();
        }

        return removedTemps;
    }

    /// <summary>Persiste el manifiesto a disco (best-effort: nunca lanza).</summary>
    public void Save()
    {
        lock (_lock)
        {
            SaveLocked();
        }
    }

    private void SaveLocked()
    {
        try
        {
            var json = JsonSerializer.Serialize(_state, LocalShareJson.Options);
            File.WriteAllText(_path, json);
        }
        catch
        {
            // Best-effort: perder el manifiesto solo cuesta reanudar desde cero.
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return;
            }

            var json = File.ReadAllText(_path);
            var loaded = JsonSerializer.Deserialize<
                Dictionary<string, Dictionary<string, PartialEntry>>>(json, LocalShareJson.Options);
            if (loaded is not null)
            {
                foreach (var (peer, byPath) in loaded)
                {
                    _state[peer] = new Dictionary<string, PartialEntry>(byPath, StringComparer.Ordinal);
                }
            }
        }
        catch
        {
            // Manifiesto corrupto: empezar limpio (temps quedarán huérfanos
            // y los borrará CleanupOrphanedTempFiles al arrancar).
        }
    }
}
