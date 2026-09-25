using System.Text.Json;
using PCSuite.Contracts;
using PCSuite.Core.ClamAV;

namespace PCSuite.Infrastructure.ClamAV;

/// <summary>
/// Historial de escaneos persistido en JSON (§13 "scan history"):
/// - Tope de entradas, la más reciente primero (el archivo no crece sin límite);
/// - Escritura atómica (tmp + rename) para no dejar el archivo a medias si la
///   app se cierra en seco;
/// - Archivo ausente/corrupto/ilegible → historial vacío (nunca rompe la
///   operación que lo consulta).
/// IO síncrona deliberada (ponytail): es un archivo de ≤50 entradas pequeño y
/// se lee/escribe bajo su propio lock (status/history no pasan por el
/// semaphore de escaneos, así que este lock es el que serializa).
/// </summary>
public sealed class ClamAvHistoryStore
{
    internal const int MaxEntries = 50;

    private readonly string _filePath;
    private readonly object _sync = new();

    public ClamAvHistoryStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(PCSuitePaths.DataDirectory, "clamav-scan-history.json");
    }

    public IReadOnlyList<ClamAvScanHistoryEntry> Read()
    {
        lock (_sync)
        {
            return Load();
        }
    }

    public void Add(ClamAvScanHistoryEntry entry)
    {
        lock (_sync)
        {
            var entries = Load();
            entries.Insert(0, entry);
            if (entries.Count > MaxEntries)
            {
                entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);
            }

            Save(entries);
        }
    }

    private List<ClamAvScanHistoryEntry> Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return [];
            }

            return JsonSerializer.Deserialize<List<ClamAvScanHistoryEntry>>(
                File.ReadAllText(_filePath),
                BridgeJson.Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private void Save(List<ClamAvScanHistoryEntry> entries)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = _filePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(entries, BridgeJson.Options));
        File.Move(temporary, _filePath, overwrite: true);
    }
}
