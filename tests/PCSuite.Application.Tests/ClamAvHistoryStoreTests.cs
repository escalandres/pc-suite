using PCSuite.Core.ClamAV;
using PCSuite.Infrastructure.ClamAV;

namespace PCSuite.Application.Tests;

/// <summary>
/// Historial de escaneos en JSON (§13): tope de 50, la más reciente primero,
/// round-trip de acentos y rutas, y tolerancia a archivo ausente/corrupto.
/// </summary>
public sealed class ClamAvHistoryStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(),
        "pcsuite-clamav-history-" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        try
        {
            File.Delete(_path);
        }
        catch (IOException)
        {
            // Mejor esfuerzo: el archivo temporal de test da igual.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static ClamAvScanHistoryEntry Entry(int scannedFiles = 10, string kind = ClamAvScanKinds.Quick) =>
        new(
            new DateTimeOffset(2026, 9, 25, 10, 30, 0, TimeSpan.Zero),
            kind,
            @"C:\destino",
            Succeeded: true,
            scannedFiles,
            InfectedFiles: 0,
            Threats: [],
            Detail: null);

    [Fact]
    public void Read_MissingFile_ReturnsEmptyList()
    {
        var store = new ClamAvHistoryStore(_path);

        Assert.Empty(store.Read());
    }

    [Fact]
    public void Add_Read_RoundTripsAccentedFieldsNewestFirst()
    {
        var store = new ClamAvHistoryStore(_path);
        var entry = new ClamAvScanHistoryEntry(
            new DateTimeOffset(2026, 9, 25, 10, 30, 0, TimeSpan.Zero),
            ClamAvScanKinds.Custom,
            @"C:\Niño\escaneos\evil.com",
            Succeeded: true,
            ScannedFiles: 42,
            InfectedFiles: 1,
            Threats: [new ClamAvThreat(@"C:\Niño\escaneos\evil.com", "Win.Trojan.FakeMS-123")],
            Detail: "Escaneo parcial: alguno de los archivos o carpetas no se pudo leer.");

        store.Add(Entry(kind: ClamAvScanKinds.Quick));
        store.Add(entry);

        var entries = store.Read();
        Assert.Equal(2, entries.Count);
        Assert.Equal(ClamAvScanKinds.Custom, entries[0].Kind); // lo último, primero
        Assert.Equal(ClamAvScanKinds.Quick, entries[1].Kind);

        Assert.Equal(entry.Timestamp, entries[0].Timestamp);
        Assert.Equal(@"C:\Niño\escaneos\evil.com", entries[0].Path);
        Assert.True(entries[0].Succeeded);
        Assert.Equal(42, entries[0].ScannedFiles);
        Assert.Equal(1, entries[0].InfectedFiles);
        Assert.Equal("Win.Trojan.FakeMS-123", entries[0].Threats[0].Threat);
        Assert.Equal(entry.Detail, entries[0].Detail);
    }

    [Fact]
    public void Add_CapsAtMaxEntries_DroppingTheOldest()
    {
        var store = new ClamAvHistoryStore(_path);
        for (var index = 0; index < ClamAvHistoryStore.MaxEntries + 10; index++)
        {
            store.Add(Entry(scannedFiles: index));
        }

        var entries = store.Read();

        Assert.Equal(ClamAvHistoryStore.MaxEntries, entries.Count);
        Assert.Equal(ClamAvHistoryStore.MaxEntries + 9, entries[0].ScannedFiles); // la más reciente
        Assert.Equal(10, entries[^1].ScannedFiles); // la más antigua superviviente
    }

    [Fact]
    public void CorruptFile_ReturnsEmpty_AndAddRecovers()
    {
        File.WriteAllText(_path, "esto no es json {");
        var store = new ClamAvHistoryStore(_path);

        Assert.Empty(store.Read()); // nunca rompe al consultante

        store.Add(Entry());
        var entry = Assert.Single(store.Read());
        Assert.Equal(ClamAvScanKinds.Quick, entry.Kind);
    }
}
