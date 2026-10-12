using PCSuite.Core.LocalShare;
using PCSuite.Infrastructure.LocalShare;
using Microsoft.Extensions.Logging.Abstractions;

namespace PCSuite.Application.Tests;

/// <summary>
/// Test de integración end-to-end: dos instancias de LocalShareService
/// en la MISMA máquina (localhost), cada una con su propio directorio
/// y puerto. Verifica el pipeline completo:
///
/// 1. Iniciar ambos servicios (cada uno genera su certificado TLS)
/// 2. Registrar cada equipo en el otro con su huella (simula emparejamiento)
/// 3. Enviar un archivo del A al B
/// 4. B recibe solicitud → A acepta → streaming → SHA-256 → publicar
/// 5. Verificar contenido del archivo en destino
/// </summary>
public class LocalShareEndToEndTests : IDisposable
{
    private readonly string _dirA;
    private readonly string _dirB;
    private readonly LocalShareService _serviceA;
    private readonly LocalShareService _serviceB;
    private const int PortA = 45001;
    private const int PortB = 45002;

    public LocalShareEndToEndTests()
    {
        var tempBase = Path.Combine(Path.GetTempPath(), $"pcshare-e2e-{Guid.NewGuid():N}");
        _dirA = Path.Combine(tempBase, "A");
        _dirB = Path.Combine(tempBase, "B");
        Directory.CreateDirectory(_dirA);
        Directory.CreateDirectory(_dirB);

        _serviceA = new LocalShareService(_dirA, NullLogger.Instance);
        _serviceB = new LocalShareService(_dirB, NullLogger.Instance);

        _serviceA.UpdateSettingsAsync(new LocalShareSettings
        {
            DisplayName = "Equipo A",
            Port = PortA,
            DiscoveryEnabled = false,
            DownloadDirectory = Path.Combine(_dirA, "received"),
            DeviceId = "device-a",
        }).GetAwaiter().GetResult();

        _serviceB.UpdateSettingsAsync(new LocalShareSettings
        {
            DisplayName = "Equipo B",
            Port = PortB,
            DiscoveryEnabled = false,
            DownloadDirectory = Path.Combine(_dirB, "received"),
            DeviceId = "device-b",
        }).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _serviceA.Dispose();
        _serviceB.Dispose();
        try { Directory.Delete(Path.GetDirectoryName(_dirA)!, true); }
        catch { /* best-effort */ }
    }

    /// <summary>Inicia ambos servicios y registra el emparejamiento mutuo con certificados reales.</summary>
    private async Task<(string DeviceBId, string DeviceAId)> StartAndPairAsync()
    {
        // 1. Iniciar ambos servicios (genera certificados)
        await _serviceA.StartAsync();
        await _serviceB.StartAsync();

        // 2. Obtener thumbprints de cada certificado (asignados por StartAsync)
        var thumbA = _serviceA.Settings.CertificateThumbprint
            ?? throw new InvalidOperationException("Service A no generó thumbprint.");
        var thumbB = _serviceB.Settings.CertificateThumbprint
            ?? throw new InvalidOperationException("Service B no generó thumbprint.");

        // 3. Registrar emparejamiento mutuo (simula PIN verificado)
        // A registra a B con el thumbprint de B
        _serviceA.RegisterPairedDevice(
            "device-b", "Equipo B", "127.0.0.1", PortB, thumbB);

        // B registra a A con el thumbprint de A
        _serviceB.RegisterPairedDevice(
            "device-a", "Equipo A", "127.0.0.1", PortA, thumbA);

        return ("device-b", "device-a");
    }

    [Fact]
    public async Task FullTransfer_FileGoesFromAToB()
    {
        // Arrange
        await StartAndPairAsync();
        var fileToSend = Path.Combine(_dirA, "documento.txt");
        File.WriteAllText(fileToSend, "Contenido de prueba para Local Share E2E.");

        // Capturar solicitud entrante en B
        var incomingTcs = new TaskCompletionSource<ShareTransfer>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _serviceB.IncomingRequest += (t, _) => incomingTcs.TrySetResult(t);

        // Capturar desenlace en B
        var finishedTcs = new TaskCompletionSource<ShareTransfer>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _serviceB.TransferFinished += t => finishedTcs.TrySetResult(t);

        // Act: enviar desde A
        var transfer = await _serviceA.SendAsync("device-b", [fileToSend]);

        // Esperar solicitud en B
        var incoming = await incomingTcs.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(TransferDirection.Receiving, incoming.Direction);
        Assert.Equal("Equipo A", incoming.PeerDisplayName);

        // Aceptar en B
        await _serviceB.AcceptAsync(incoming.TransferId);

        // Esperar desenlace
        var result = await finishedTcs.Task.WaitAsync(TimeSpan.FromSeconds(30));

        // Assert
        Assert.Equal(TransferStatus.Completed, result.Status);

        var expectedPath = Path.Combine(_dirB, "received", "documento.txt");
        Assert.True(File.Exists(expectedPath),
            $"Archivo no encontrado en {expectedPath}");
        Assert.Equal(
            "Contenido de prueba para Local Share E2E.",
            File.ReadAllText(expectedPath));
    }

    [Fact]
    public async Task RejectedTransfer_FileNotWritten()
    {
        await StartAndPairAsync();
        var fileToSend = Path.Combine(_dirA, "documento-rechazado.txt");
        File.WriteAllText(fileToSend, "No deberías recibirme.");

        var incomingTcs = new TaskCompletionSource<ShareTransfer>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _serviceB.IncomingRequest += (t, _) => incomingTcs.TrySetResult(t);

        var finishedTcs = new TaskCompletionSource<ShareTransfer>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _serviceB.TransferFinished += t => finishedTcs.TrySetResult(t);

        var transfer = await _serviceA.SendAsync("device-b", [fileToSend]);
        var incoming = await incomingTcs.Task.WaitAsync(TimeSpan.FromSeconds(30));

        // Rechazar
        await _serviceB.RejectAsync(incoming.TransferId);
        var result = await finishedTcs.Task.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(TransferStatus.Rejected, result.Status);

        var receivedDir = Path.Combine(_dirB, "received");
        if (Directory.Exists(receivedDir))
        {
            Assert.Empty(Directory.GetFiles(receivedDir, "*", SearchOption.AllDirectories));
        }
    }

    [Fact]
    public async Task MaliciousPath_DoesNotEscape()
    {
        await StartAndPairAsync();
        var writer = new SafeFileWriter(Path.Combine(_dirB, "received"));

        Assert.Throws<ArgumentException>(() => writer.ValidateRelativePath("../../../etc/passwd"));
        Assert.Throws<ArgumentException>(() => writer.ValidateRelativePath("C:\\Windows\\evil.exe"));
        Assert.Throws<ArgumentException>(() => writer.ValidateRelativePath("\\\\server\\share\\file.txt"));
        Assert.Throws<ArgumentException>(() => writer.ValidateRelativePath("archivo\x00.txt"));

        writer.Dispose();
    }

    [Fact]
    public async Task StartStop_CycleWorks()
    {
        await _serviceA.StartAsync();
        Assert.Equal(LocalShareServiceStatus.Running, _serviceA.Status);

        await _serviceA.StopAsync();
        Assert.Equal(LocalShareServiceStatus.Stopped, _serviceA.Status);

        await _serviceA.StartAsync();
        Assert.Equal(LocalShareServiceStatus.Running, _serviceA.Status);
    }
}
