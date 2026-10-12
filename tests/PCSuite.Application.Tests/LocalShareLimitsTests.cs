using PCSuite.Core.LocalShare;
using PCSuite.Infrastructure.LocalShare;

namespace PCSuite.Application.Tests;

/// <summary>
/// Tests de los límites de Local Share (Task 016.7.2):
/// validación de tamaño/espacio del receptor y regulación de ancho de banda.
/// </summary>
public class LocalShareLimitsTests
{
    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"pcshare-limits-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    // ------------------------------------------------------------------
    // ValidateIncoming: límite de tamaño configurado
    // ------------------------------------------------------------------

    [Fact]
    public void ValidateIncoming_SinLimite_Pasa()
    {
        var dir = TempDir();
        try
        {
            var settings = new LocalShareSettings { MaxTransferSizeBytes = 0 };

            var result = LocalShareLimits.ValidateIncoming(1_000_000_000L, settings, dir);

            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ValidateIncoming_SobreLimiteTamanio_Rechaza()
    {
        var dir = TempDir();
        try
        {
            var settings = new LocalShareSettings { MaxTransferSizeBytes = 1024 };

            var result = LocalShareLimits.ValidateIncoming(2048, settings, dir);

            Assert.NotNull(result);
            Assert.Equal("size_limit", result.Value.Code);
            Assert.Contains("límite", result.Value.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ValidateIncoming_BajoLimite_Pasa()
    {
        var dir = TempDir();
        try
        {
            var settings = new LocalShareSettings { MaxTransferSizeBytes = 1024 };

            var result = LocalShareLimits.ValidateIncoming(512, settings, dir);

            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // ------------------------------------------------------------------
    // ValidateIncoming: espacio disponible en disco
    // ------------------------------------------------------------------

    [Fact]
    public void ValidateIncoming_TamanoImposible_RechazaPorEspacio()
    {
        var dir = TempDir();
        try
        {
            // 2 EB: ninguna unidad real tiene ese espacio libre.
            var settings = new LocalShareSettings { MaxTransferSizeBytes = 0 };

            var result = LocalShareLimits.ValidateIncoming(long.MaxValue / 4, settings, dir);

            Assert.NotNull(result);
            Assert.Equal("no_space", result.Value.Code);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ValidateIncoming_TamanoNormal_HayEspacio_Pasa()
    {
        var dir = TempDir();
        try
        {
            var settings = new LocalShareSettings { MaxTransferSizeBytes = 0 };

            var result = LocalShareLimits.ValidateIncoming(1024, settings, dir);

            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ValidateIncoming_UnidadInexistente_BestEffortPasa()
    {
        // Solo determinista si la unidad Q: no existe en la máquina de test.
        if (System.IO.DriveInfo.GetDrives().Any(d => d.Name == "Q:\\"))
        {
            return; // entorno atípico: no assertFalse de algo no controlable
        }

        var settings = new LocalShareSettings { MaxTransferSizeBytes = 0 };

        var result = LocalShareLimits.ValidateIncoming(1024, settings, "Q:\\carpeta");

        Assert.Null(result);
    }

    // ------------------------------------------------------------------
    // ComputeThrottleDelayMs: regulación de ancho de banda
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(0)]          // sin límite
    [InlineData(-5)]         // valores negativos = sin límite
    public void Throttle_SinLimite_NuncaEspera(long maxBytesPerSecond)
    {
        Assert.Equal(0, TransferClient.ComputeThrottleDelayMs(10_000_000, maxBytesPerSecond, elapsedMs: 0));
    }

    [Fact]
    public void Throttle_CeroBytesEnviados_NoEspera()
    {
        Assert.Equal(0, TransferClient.ComputeThrottleDelayMs(0, 1000, elapsedMs: 0));
    }

    [Fact]
    public void Throttle_VamosRapido_EsperaLoProporcional()
    {
        // 1000 bytes a 1000 B/s: el tiempo ideal es 1000ms; con 0ms
        // transcurridos debe esperar ~1000ms.
        var delay = TransferClient.ComputeThrottleDelayMs(1000, 1000, elapsedMs: 0);
        Assert.InRange(delay, 950, 1050);
    }

    [Fact]
    public void Throttle_VamosLentoQueElLimite_NoEspera()
    {
        // 1000 bytes a 1000 B/s pero ya pasaron 3000ms: sobra margen.
        Assert.Equal(0, TransferClient.ComputeThrottleDelayMs(1000, 1000, elapsedMs: 3000));
    }

    [Fact]
    public void Throttle_RetrasoEnorme_TopePorChunk()
    {
        // 100MB a 1000 B/s = 100000ms ideales: el tope por iteración es 2000ms
        // (mantener la capacidad de cancelar a tiempo).
        var delay = TransferClient.ComputeThrottleDelayMs(100_000_000, 1000, elapsedMs: 0);
        Assert.Equal(2000, delay);
    }

    [Fact]
    public void Throttle_LimiteEnorme_RetrasoSubmilisegundo_NoEspera()
    {
        // 1 byte a 10 GB/s: el retraso ideal es 0.0001ms → 0.
        Assert.Equal(0, TransferClient.ComputeThrottleDelayMs(1, 10_000_000_000, elapsedMs: 0));
    }
}
