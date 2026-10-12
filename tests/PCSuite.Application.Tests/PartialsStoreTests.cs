using PCSuite.Infrastructure.LocalShare;

namespace PCSuite.Application.Tests;

/// <summary>
/// Tests de PartialsStore (Task 016.7.3 — reanudación): manifiesto de
/// prefijos recibidos, persistencia a disco y poda de entradas caducadas.
/// </summary>
public class PartialsStoreTests : IDisposable
{
    private readonly string _dir;

    public PartialsStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"partials-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best-effort */ }
    }

    private string TempFile(string name, int size)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, new byte[size]);
        return path;
    }

    [Fact]
    public void Get_SinEntrada_DevuelveNull()
    {
        var store = new PartialsStore(_dir);

        Assert.Null(store.Get("peer-a", "archivo.txt"));
    }

    [Fact]
    public void Set_Y_Get_RoundTrip()
    {
        var store = new PartialsStore(_dir);
        var temp = TempFile("t1.part", 100);

        store.Set("peer-a", "docs/archivo.txt", new PartialsStore.PartialEntry
        {
            TempPath = temp,
            Bytes = 100,
        });

        var entry = store.Get("peer-a", "docs/archivo.txt");
        Assert.NotNull(entry);
        Assert.Equal(temp, entry.TempPath);
        Assert.Equal(100, entry.Bytes);
    }

    [Fact]
    public void Claves_SensiblesASeparacion_PeerYRuta()
    {
        var store = new PartialsStore(_dir);
        store.Set("peer-a", "x.txt", new PartialsStore.PartialEntry { TempPath = "a", Bytes = 1 });
        store.Set("peer-b", "x.txt", new PartialsStore.PartialEntry { TempPath = "b", Bytes = 2 });

        Assert.Equal("a", store.Get("peer-a", "x.txt").TempPath);
        Assert.Equal("b", store.Get("peer-b", "x.txt").TempPath);
    }

    [Fact]
    public void Remove_EliminaSoloLaEntradaIndicada()
    {
        var store = new PartialsStore(_dir);
        store.Set("peer-a", "x.txt", new PartialsStore.PartialEntry { TempPath = "a", Bytes = 1 });
        store.Set("peer-a", "y.txt", new PartialsStore.PartialEntry { TempPath = "b", Bytes = 2 });

        store.Remove("peer-a", "x.txt");

        Assert.Null(store.Get("peer-a", "x.txt"));
        Assert.NotNull(store.Get("peer-a", "y.txt"));
    }

    [Fact]
    public void Save_Y_Load_PersistenEntreInstancias()
    {
        var temp = TempFile("t2.part", 50);
        var store1 = new PartialsStore(_dir);
        store1.Set("peer-a", "carpeta/archivo.bin", new PartialsStore.PartialEntry
        {
            TempPath = temp,
            Bytes = 12345,
        });
        store1.Save();

        // Nueva instancia desde el mismo directorio: como tras un reinicio.
        var store2 = new PartialsStore(_dir);
        var entry = store2.Get("peer-a", "carpeta/archivo.bin");

        Assert.NotNull(entry);
        Assert.Equal(temp, entry.TempPath);
        Assert.Equal(12345, entry.Bytes);
    }

    [Fact]
    public void Load_ManifiestoCorrupto_EmpiezaLimpio()
    {
        File.WriteAllText(Path.Combine(_dir, "partials.json"), "{ esto no es json ]]]");

        var store = new PartialsStore(_dir);

        Assert.Null(store.Get("peer-a", "x.txt"));
    }

    [Fact]
    public void ActiveTempFiles_ListaLosVivos()
    {
        var store = new PartialsStore(_dir);
        var t1 = TempFile("a.part", 1);
        var t2 = TempFile("b.part", 1);
        store.Set("p1", "x", new PartialsStore.PartialEntry { TempPath = t1, Bytes = 1 });
        store.Set("p2", "y", new PartialsStore.PartialEntry { TempPath = t2, Bytes = 1 });

        var active = store.ActiveTempFiles();

        Assert.Contains(t1, active);
        Assert.Contains(t2, active);
        Assert.Equal(2, active.Count);
    }

    [Fact]
    public void Prune_EliminaEntradasConTempDesaparecido()
    {
        var store = new PartialsStore(_dir);
        var fantasma = Path.Combine(_dir, "no-existe.part");
        store.Set("p1", "x", new PartialsStore.PartialEntry { TempPath = fantasma, Bytes = 10 });

        store.Prune(TimeSpan.FromDays(7));

        Assert.Null(store.Get("p1", "x"));
    }

    [Fact]
    public void Prune_EliminaEntradasViejasYPideBorrarSuTemp()
    {
        var store = new PartialsStore(_dir);
        var temp = TempFile("viejo.part", 10);
        store.Set("p1", "x", new PartialsStore.PartialEntry
        {
            TempPath = temp,
            Bytes = 10,
            UpdatedAtUtc = DateTimeOffset.UtcNow.AddDays(-10),
        });

        var toDelete = store.Prune(TimeSpan.FromDays(7));

        Assert.Null(store.Get("p1", "x"));
        Assert.Contains(temp, toDelete);
        Assert.True(File.Exists(temp), "Prune lista; no borra (lo hace el caller).");
    }

    [Fact]
    public void Prune_ConservaEntradasRecientesConTemp()
    {
        var store = new PartialsStore(_dir);
        var temp = TempFile("vivo.part", 10);
        store.Set("p1", "x", new PartialsStore.PartialEntry
        {
            TempPath = temp,
            Bytes = 10,
            UpdatedAtUtc = DateTimeOffset.UtcNow.AddHours(-1),
        });

        store.Prune(TimeSpan.FromDays(7));

        Assert.NotNull(store.Get("p1", "x"));
    }
}
