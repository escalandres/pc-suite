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

    [Fact]
    public async Task SimultaneousTransfers_TwoPendingThirdGetsBusy()
    {
        // Límite de B: 2 transferencias entrantes simultáneas.
        var settings = _serviceB.Settings;
        settings.MaxSimultaneousTransfers = 2;
        await _serviceB.UpdateSettingsAsync(settings);

        await StartAndPairAsync();

        // Contador de solicitudes entrantes en B.
        var incomingCount = 0;
        var incomingIds = new List<string>();
        var countLock = new object();
        _serviceB.IncomingRequest += (t, _) =>
        {
            lock (countLock)
            {
                incomingCount++;
                incomingIds.Add(t.TransferId);
            }
        };

        // Tres archivos distintos, tres envíos desde A.
        for (var i = 1; i <= 3; i++)
        {
            var path = Path.Combine(_dirA, $"lote-{i}.txt");
            File.WriteAllText(path, $"contenido {i}");
            await _serviceA.SendAsync("device-b", [path]);
        }

        // El tercer envío debe fallar en A con el error "busy" del receptor.
        var failedTcs = new TaskCompletionSource<ShareTransfer>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _serviceA.TransferFinished += t =>
        {
            if (t.Status == TransferStatus.Failed)
            {
                failedTcs.TrySetResult(t);
            }
        };

        var failed = await failedTcs.Task.WaitAsync(TimeSpan.FromSeconds(30));

        // El error llega limpio (frame ERROR), no como EOF críptico.
        Assert.NotNull(failed.ErrorMessage);
        Assert.Contains("ocupado", failed.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        // Los dos primeros envíos SÍ llegaron a B y siguen esperando decisión:
        // con el gate antiguo (1 conexión), el segundo habría quedado bloqueado.
        await Task.Delay(TimeSpan.FromSeconds(2));
        lock (countLock)
        {
            Assert.Equal(2, incomingCount);
        }

        // Limpiar: rechazar los dos pendientes para que los handlers terminen.
        foreach (var id in incomingIds)
        {
            await _serviceB.RejectAsync(id);
        }
    }

    [Fact]
    public async Task ReceiverSizeLimit_RejectsWithCleanError()
    {
        // El receptor solo acepta transferencias de hasta 100 bytes.
        var bSettings = _serviceB.Settings;
        bSettings.MaxTransferSizeBytes = 100;
        await _serviceB.UpdateSettingsAsync(bSettings);

        await StartAndPairAsync();

        // El usuario de B nunca debe ser molestado.
        var bWasAsked = false;
        _serviceB.IncomingRequest += (_, _) => bWasAsked = true;

        var file = Path.Combine(_dirA, "grande.txt");
        File.WriteAllText(file, new string('x', 1024)); // 1 KB > 100 B

        var aFinished = new TaskCompletionSource<ShareTransfer>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _serviceA.TransferFinished += t => aFinished.TrySetResult(t);

        await _serviceA.SendAsync("device-b", [file]);

        var failed = await aFinished.Task.WaitAsync(TimeSpan.FromSeconds(30));

        // El emisor recibe el error LIMPIO del receptor (frame ERROR), no un EOF.
        Assert.Equal(TransferStatus.Failed, failed.Status);
        Assert.Contains("límite", failed.ErrorMessage ?? "", StringComparison.Ordinal);
        Assert.False(bWasAsked);

        var receivedDir = Path.Combine(_dirB, "received");
        if (Directory.Exists(receivedDir))
        {
            Assert.Empty(Directory.GetFiles(receivedDir, "*", SearchOption.AllDirectories));
        }
    }

    [Fact]
    public async Task AutoAcceptFromPaired_SkipsUserPrompt()
    {
        var bSettings = _serviceB.Settings;
        bSettings.AutoAcceptFromPaired = true;
        await _serviceB.UpdateSettingsAsync(bSettings);

        await StartAndPairAsync();

        var bWasAsked = false;
        _serviceB.IncomingRequest += (_, _) => bWasAsked = true;

        var file = Path.Combine(_dirA, "auto-aceptado.txt");
        File.WriteAllText(file, "contenido automático");

        var aFinished = new TaskCompletionSource<ShareTransfer>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _serviceA.TransferFinished += t => aFinished.TrySetResult(t);

        await _serviceA.SendAsync("device-b", [file]);
        var result = await aFinished.Task.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(TransferStatus.Completed, result.Status);
        Assert.False(bWasAsked, "AutoAcceptFromPaired no debería pedir confirmación al usuario.");
        Assert.Equal(
            "contenido automático",
            File.ReadAllText(Path.Combine(_dirB, "received", "auto-aceptado.txt")));
    }

    [Fact]
    public async Task BandwidthLimit_ThrottlesTransfer()
    {
        // Emisor limitado a 32 KB/s: 128 KB deberían tardar ~4s.
        var aSettings = _serviceA.Settings;
        aSettings.MaxBandwidthBytesPerSecond = 32 * 1024;
        await _serviceA.UpdateSettingsAsync(aSettings);

        // Auto-aceptar en B para no depender del prompt en el test de ritmo.
        var bSettings = _serviceB.Settings;
        bSettings.AutoAcceptFromPaired = true;
        await _serviceB.UpdateSettingsAsync(bSettings);

        await StartAndPairAsync();

        var file = Path.Combine(_dirA, "throttled.bin");
        File.WriteAllBytes(file, new byte[128 * 1024]);

        var aFinished = new TaskCompletionSource<ShareTransfer>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _serviceA.TransferFinished += t => aFinished.TrySetResult(t);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        await _serviceA.SendAsync("device-b", [file]);
        var result = await aFinished.Task.WaitAsync(TimeSpan.FromSeconds(30));
        watch.Stop();

        Assert.Equal(TransferStatus.Completed, result.Status);

        // Sin límite, loopback transfiere 128KB en <1s. Con 32KB/s: >=3.5s.
        Assert.True(
            watch.Elapsed >= TimeSpan.FromSeconds(3.5),
            $"Esperado >= 3.5s con límite de 32KB/s, tomó {watch.Elapsed.TotalMilliseconds:F0}ms");

        // El archivo llegó íntegro pese a la regulación.
        var received = Path.Combine(_dirB, "received", "throttled.bin");
        Assert.True(File.Exists(received));
        Assert.Equal(128 * 1024, new FileInfo(received).Length);
    }

    [Fact]
    public async Task MultiItemTransfer_AllFilesPublishedInOrder()
    {
        // Regresión del bug de la "lectura extra": un ReadFrameAsync de más
        // tras el bucle de datos consumía el FILE_START del siguiente archivo
        // y rompía toda transferencia de 2+ archivos.
        await StartAndPairAsync();

        var contents = new Dictionary<string, string>();
        var files = new List<string>();
        for (var i = 1; i <= 3; i++)
        {
            var path = Path.Combine(_dirA, $"lote-{i}.txt");
            var content = $"contenido del archivo {i}";
            File.WriteAllText(path, content);
            files.Add(path);
            contents[$"lote-{i}.txt"] = content;
        }

        var incomingTcs = new TaskCompletionSource<ShareTransfer>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _serviceB.IncomingRequest += (t, items) =>
        {
            Assert.Equal(3, items.Count);
            incomingTcs.TrySetResult(t);
        };

        var aFinished = new TaskCompletionSource<ShareTransfer>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _serviceA.TransferFinished += t => aFinished.TrySetResult(t);

        await _serviceA.SendAsync("device-b", files);
        var incoming = await incomingTcs.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await _serviceB.AcceptAsync(incoming.TransferId);

        var result = await aFinished.Task.WaitAsync(TimeSpan.FromSeconds(30));

        // "Completed" del emisor ahora es fin-a-fin: significa publicado.
        Assert.Equal(TransferStatus.Completed, result.Status);

        foreach (var (name, content) in contents)
        {
            var path = Path.Combine(_dirB, "received", name);
            Assert.True(File.Exists(path), $"Falta {name}");
            Assert.Equal(content, File.ReadAllText(path));
        }
    }
}
