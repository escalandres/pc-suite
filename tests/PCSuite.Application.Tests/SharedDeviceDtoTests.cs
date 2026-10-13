using PCSuite.Core.LocalShare;
using PCSuite.Infrastructure.Operations;

namespace PCSuite.Application.Tests;

/// <summary>
/// Tests de la heurística de "en línea" del listado de dispositivos:
/// un dispositivo es Online si se anunció en los últimos 45 s.
/// </summary>
public class SharedDeviceDtoTests
{
    private static SharedDevice Make(DeviceTrust trust, DateTimeOffset lastSeen) => new()
    {
        DeviceId = "dev-1",
        DisplayName = "Equipo",
        KnownAddresses = ["192.168.0.10"],
        Port = 45100,
        Trust = trust,
        LastSeenAt = lastSeen,
    };

    [Fact]
    public void Online_AcabaDeAnunciarse_True()
    {
        var dto = LocalShareDevicesOperation.ToDeviceDto(
            Make(DeviceTrust.Unknown, DateTimeOffset.UtcNow.AddSeconds(-10)));

        Assert.True(dto.Online);
    }

    [Fact]
    public void Online_SinSenal45Segundos_False()
    {
        // El caso del reporte: la app del otro equipo está cerrada y su
        // entrada quedó persistida de la última vez que anunció.
        var dto = LocalShareDevicesOperation.ToDeviceDto(
            Make(DeviceTrust.Unknown, DateTimeOffset.UtcNow.AddMinutes(-3)));

        Assert.False(dto.Online);
    }

    [Fact]
    public void Online_EmparejadoReciente_True()
    {
        // RegisterPairedDevice marca LastSeenAt=ahora: el ciclo de
        // emparejamiento acaba de conectar con él.
        var dto = LocalShareDevicesOperation.ToDeviceDto(
            Make(DeviceTrust.Paired, DateTimeOffset.UtcNow));

        Assert.True(dto.Online);
    }

    [Fact]
    public void Online_EmparejadoApagado_False()
    {
        // Emparejado de ayer con la app cerrada: confianza intacta, pero
        // no está disponible — la UI debe mostrarlo como "Sin señal".
        var dto = LocalShareDevicesOperation.ToDeviceDto(
            Make(DeviceTrust.Paired, DateTimeOffset.UtcNow.AddHours(-20)));

        Assert.False(dto.Online);
    }
}
