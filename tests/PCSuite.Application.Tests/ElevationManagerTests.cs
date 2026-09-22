using System.Text.Json;
using PCSuite.Contracts;
using PCSuite.Core.Elevation;
using PCSuite.Core.Processes;
using PCSuite.Infrastructure.Elevation;

namespace PCSuite.Application.Tests;

/// <summary>
/// Autorización, allow-list y mapeo de errores del ElevationManager — sin UAC
/// real: el "hijo elevado" es un fake runner que, cuando toca, atiende el
/// IPC real en el mismo proceso.
/// </summary>
public sealed class ElevationManagerTests
{
    // Un archivo existente sirve de "helper": en la mayoría de tests nunca se
    // llega a lanzar, y en el e2e el runner es fake.
    private static readonly string ExistingHelperPath = typeof(ElevationManager).Assembly.Location;

    private static ElevationManager NewManager(
        FakeProcessRunner runner,
        string? helperPath = null,
        TimeSpan? responseGracePeriod = null) =>
        new(runner, helperPath ?? ExistingHelperPath, responseGracePeriod);

    [Fact]
    public async Task OperacionFueraDeLaListaBlanca_FallaSinLanzar_SinUac()
    {
        var runner = FakeProcessRunner.Returning(string.Empty);
        var manager = NewManager(runner);

        var result = await manager.RunElevatedAsync(
            new ElevatedOperation { OperationId = "elevated.inventada" },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(BridgeErrorCodes.OperationNotFound, result.ErrorCode);
        Assert.Empty(runner.Requests); // cero lanzamientos = cero prompts UAC
    }

    [Fact]
    public async Task IdVacio_FallaMalformedSinLanzar()
    {
        var runner = FakeProcessRunner.Returning(string.Empty);
        var manager = NewManager(runner);

        var result = await manager.RunElevatedAsync(
            new ElevatedOperation { OperationId = "  " },
            CancellationToken.None);

        Assert.Equal(BridgeErrorCodes.MalformedMessage, result.ErrorCode);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task HelperInexistente_FallaElevationFailedSinLanzar()
    {
        var runner = FakeProcessRunner.Returning(string.Empty);
        var manager = NewManager(
            runner,
            Path.Combine(Path.GetTempPath(), $"pcsuite-no-existe-{Guid.NewGuid():N}.exe"));

        var result = await manager.RunElevatedAsync(
            new ElevatedOperation { OperationId = ElevatedOperationIds.AdminInfo },
            CancellationToken.None);

        Assert.Equal(BridgeErrorCodes.ElevationFailed, result.ErrorCode);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task UsuarioRechazaUac_FallaElevationCancelled_SinStackTraces()
    {
        var runner = FakeProcessRunner.Throwing(new ElevationDeclinedException());
        var manager = NewManager(runner);

        var result = await manager.RunElevatedAsync(
            new ElevatedOperation { OperationId = ElevatedOperationIds.AdminInfo },
            CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.False(result.Success);
        Assert.Equal(BridgeErrorCodes.ElevationCancelled, result.ErrorCode);
        Assert.Contains("UAC", result.ErrorMessage);
        Assert.DoesNotContain("   at ", result.ErrorMessage); // sin stack trace
        Assert.Single(runner.Requests);
    }

    [Fact]
    public async Task FalloDeLanzamiento_FallaElevationFailed_SinDetallesInternos()
    {
        var runner = FakeProcessRunner.Throwing(new InvalidOperationException("detalle interno sensible"));
        var manager = NewManager(runner);

        var result = await manager.RunElevatedAsync(
            new ElevatedOperation { OperationId = ElevatedOperationIds.AdminInfo },
            CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.False(result.Success);
        Assert.Equal(BridgeErrorCodes.ElevationFailed, result.ErrorCode);
        Assert.DoesNotContain("detalle interno sensible", result.ErrorMessage); // el detalle no cruza a la UI
    }

    [Fact]
    public async Task HijoQueNoAtiendeElPipe_FallaDentroDeLaHolgura_NoEspera90Segundos()
    {
        // El hijo "arrancó" pero nunca crea el pipe: el manager debe fallar
        // al agotarse la holgura corta, no tras el timeout de conexión.
        var runner = FakeProcessRunner.Returning(string.Empty);
        var manager = NewManager(runner, responseGracePeriod: TimeSpan.FromMilliseconds(300));

        var result = await manager.RunElevatedAsync(
            new ElevatedOperation { OperationId = ElevatedOperationIds.AdminInfo },
            CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.False(result.Success);
        Assert.Equal(BridgeErrorCodes.ElevationFailed, result.ErrorCode);
        Assert.Contains("sin responder", result.ErrorMessage);
    }

    [Fact]
    public async Task IntercambioCompleto_IpcReal_YFalsoHijo_ExecutaAdminInfoConPayload()
    {
        // El fake runner hace de hijo elevado: recibe pipeName + nonce por
        // argumentos y atiende el IPC real con el registro real → e2e sin UAC.
        var runner = new FakeProcessRunner(async (request, cancellationToken) =>
        {
            var registry = new ElevatedOperationRegistry();
            registry.Register(new AdminInfoElevatedOperation());
            var server = new ElevatedIpcServer(registry, TimeSpan.FromSeconds(15));

            var response = await server.ServeAsync(
                request.Arguments[0],
                request.Arguments[1],
                Environment.UserName,
                cancellationToken);

            return new ProcessResult { ExitCode = response.Success ? 0 : 1 };
        });

        var manager = NewManager(runner);
        using var payload = JsonDocument.Parse("""{"includeTokenGroups":true}""");

        var result = await manager.RunElevatedAsync(
            new ElevatedOperation
            {
                OperationId = ElevatedOperationIds.AdminInfo,
                Payload = payload.RootElement,
            },
            CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(Environment.MachineName, result.Data!.Value.GetProperty("machineName").GetString());
        Assert.True(result.Data!.Value.TryGetProperty("tokenGroups", out _)); // payload ida y vuelta

        var request = Assert.Single(runner.Requests);
        Assert.True(request.RequiresElevation);
        Assert.Equal(ExistingHelperPath, request.FileName);
        Assert.Equal(2, request.Arguments.Count);
        Assert.StartsWith("pcsuite-elev-", request.Arguments[0]);
        Assert.Equal(43, request.Arguments[1].Length); // nonce base64url de 32 bytes
    }

    [Fact]
    public async Task CancelacionExterna_SePropagaComoOperationCanceled()
    {
        var runner = FakeProcessRunner.Returning(string.Empty);
        var manager = NewManager(runner);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // WaitAsync: si la cancelación no se propagara, el test falla en 30 s
        // en lugar de colgarse esperando el timeout de conexión (90 s).
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => manager.RunElevatedAsync(
                new ElevatedOperation { OperationId = ElevatedOperationIds.AdminInfo },
                cts.Token).WaitAsync(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task IsAdministrator_CoincideConElTokenActual()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var manager = NewManager(FakeProcessRunner.Returning(string.Empty));

        var actual = await manager.IsAdministratorAsync();

        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var expected = new System.Security.Principal.WindowsPrincipal(identity)
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

        Assert.Equal(expected, actual);
    }
}
