using PCSuite.Contracts;
using PCSuite.Core.Elevation;
using PCSuite.Core.Processes;
using PCSuite.Core.Wsl;

namespace PCSuite.Application.Tests;

/// <summary>
/// WslService: estado con degradación controlada, listas contra fixtures
/// reales, mensajes de error aptos para la UI, elevación POR OPERACIÓN solo
/// cuando `wsl --update` falla por permisos (nunca UAC preventivo, jamás en
/// --shutdown/--start) y serialización de 1 invocación.
/// </summary>
public sealed class WslServiceTests
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(120);

    private static readonly TimeSpan UpdateTimeout = TimeSpan.FromSeconds(900);

    // ------------------------------------------------------------------
    // wsl.status (3 lecturas: --version / --status / --list --verbose)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Status_WithoutWsl_ReturnsUnknown_WithoutAnyRequest()
    {
        using var fx = WslFixture.Create(FakeProcessRunner.Returning(""), withWsl: false);

        var status = await fx.Service.GetStatusAsync();

        Assert.False(status.Installed);
        Assert.Null(status.Path);
        Assert.Equal(WslStates.Unknown, status.State);
        Assert.Null(status.Version);
        Assert.Null(status.DefaultDistro);
        Assert.Empty(fx.Runner.Requests);
        Assert.Empty(fx.Elevation.Requests);
    }

    [Fact]
    public async Task Status_WithWsl_ReadsThreeFixtures_FixedArgv30sUtf16Le()
    {
        using var fx = WslFixture.Create(CreateArgvAwareRunner());

        var status = await fx.Service.GetStatusAsync();

        Assert.True(status.Installed);
        Assert.Equal(fx.WslPath, status.Path);
        Assert.Equal(WslStates.Stopped, status.State);
        Assert.Equal("2.7.14.0", status.Version);
        Assert.Equal("docker-desktop", status.DefaultDistro);

        Assert.Equal(3, fx.Runner.Requests.Count);
        Assert.Equal<string>(new[] { "--version" }, fx.Runner.Requests[0].Arguments);
        Assert.Equal<string>(new[] { "--status" }, fx.Runner.Requests[1].Arguments);
        Assert.Equal<string>(new[] { "--list", "--verbose" }, fx.Runner.Requests[2].Arguments);
        Assert.All(fx.Runner.Requests, request =>
        {
            Assert.Equal(ReadTimeout, request.Timeout);
            Assert.NotNull(request.OutputEncoding);
            Assert.Equal(1200, request.OutputEncoding!.CodePage);
            Assert.Empty(request.OutputEncoding.GetPreamble());
        });
        Assert.Empty(fx.Elevation.Requests);
    }

    [Fact]
    public async Task Status_RunningDistro_ReportsRunning()
    {
        using var fx = WslFixture.Create(FakeProcessRunner.Returning(
            "  NAME              STATE           VERSION\r\n* Ubuntu            Running         2"));

        var status = await fx.Service.GetStatusAsync();

        Assert.Equal(WslStates.Running, status.State);
    }

    [Fact]
    public async Task Status_ListReadFails_StateUnknown_OthersStillParsed()
    {
        var runner = new FakeProcessRunner((request, _) => Task.FromResult(
            new ProcessResult
            {
                ExitCode = request.Arguments[0] == "--list" ? 1 : 0,
                StandardOutput = request.Arguments[0] switch
                {
                    "--version" => WslFixtures.Read("wsl-version.txt"),
                    "--status" => WslFixtures.Read("wsl-status.txt"),
                    _ => string.Empty,
                },
            }));
        using var fx = WslFixture.Create(runner);

        var status = await fx.Service.GetStatusAsync();

        Assert.True(status.Installed);
        Assert.Equal(WslStates.Unknown, status.State);
        Assert.Equal("2.7.14.0", status.Version);
        Assert.Equal("docker-desktop", status.DefaultDistro);
    }

    [Fact]
    public async Task Status_ProbeFails_DegradesToNull_StillInstalled()
    {
        using var fx = WslFixture.Create(FakeProcessRunner.Throwing(new IOException("problema")));

        var status = await fx.Service.GetStatusAsync();

        Assert.True(status.Installed);
        Assert.Equal(WslStates.Unknown, status.State);
        Assert.Null(status.Version);
        Assert.Null(status.DefaultDistro);
    }

    [Fact]
    public async Task Status_Timeout_ThrowsWslTimeout_WithReadMessage()
    {
        using var fx = WslFixture.Create(
            FakeProcessRunner.Throwing(new ProcessTimeoutException(TimeSpan.FromSeconds(5))));

        var exception = await Assert.ThrowsAsync<WslException>(() => fx.Service.GetStatusAsync());

        Assert.Equal(BridgeErrorCodes.WslTimeout, exception.ErrorCode);
        Assert.Equal("wsl no respondió a tiempo. Inténtalo de nuevo.", exception.ErrorMessage);
        Assert.DoesNotContain("   at ", exception.ErrorMessage);
    }

    // ------------------------------------------------------------------
    // wsl.list
    // ------------------------------------------------------------------

    [Fact]
    public async Task List_ParsesRealFixture_ExactArgv()
    {
        using var fx = WslFixture.Create(FakeProcessRunner.Returning(WslFixtures.Read("wsl-list.txt")));

        var distros = await fx.Service.ListAsync();

        Assert.Equal(new WslDistro("docker-desktop", "Stopped", 2), Assert.Single(distros));

        var request = Assert.Single(fx.Runner.Requests);
        Assert.Equal<string>(new[] { "--list", "--verbose" }, request.Arguments);
        Assert.Equal(ReadTimeout, request.Timeout);
        Assert.Equal(1200, request.OutputEncoding!.CodePage);
    }

    [Fact]
    public async Task List_ExitZeroWithoutRows_ReturnsEmpty()
    {
        using var fx = WslFixture.Create(FakeProcessRunner.Returning(""));

        Assert.Empty(await fx.Service.ListAsync());
    }

    [Fact]
    public async Task List_WithoutWsl_ThrowsNotInstalled_WithoutAnyRequest()
    {
        using var fx = WslFixture.Create(FakeProcessRunner.Returning(""), withWsl: false);

        var exception = await Assert.ThrowsAsync<WslException>(() => fx.Service.ListAsync());

        Assert.Equal(BridgeErrorCodes.NotInstalled, exception.ErrorCode);
        Assert.Equal("No se encontró wsl.exe en la PATH.", exception.ErrorMessage);
        Assert.Empty(fx.Runner.Requests);
        Assert.Empty(fx.Elevation.Requests);
    }

    [Fact]
    public async Task List_UnknownFailure_ThrowsWslFailed_ControlledMessage()
    {
        using var fx = WslFixture.Create(FakeProcessRunner.Returning("", 4711));

        var exception = await Assert.ThrowsAsync<WslException>(() => fx.Service.ListAsync());

        Assert.Equal(BridgeErrorCodes.WslFailed, exception.ErrorCode);
        Assert.Equal("wsl falló con el código de salida 4711.", exception.ErrorMessage);
        Assert.DoesNotContain("   at ", exception.ErrorMessage);
    }

    [Fact]
    public async Task List_StderrOnlyFailure_UsesLastLineAsMessage()
    {
        var runner = new FakeProcessRunner((_, _) => Task.FromResult(
            new ProcessResult { ExitCode = 9, StandardError = "fallo serio de wsl" }));
        using var fx = WslFixture.Create(runner);

        var exception = await Assert.ThrowsAsync<WslException>(() => fx.Service.ListAsync());

        Assert.Equal(BridgeErrorCodes.WslFailed, exception.ErrorCode);
        Assert.Equal("fallo serio de wsl", exception.ErrorMessage);
    }

    [Fact]
    public async Task List_Timeout_ThrowsWslTimeout_WithReadMessage()
    {
        using var fx = WslFixture.Create(
            FakeProcessRunner.Throwing(new ProcessTimeoutException(TimeSpan.FromSeconds(5))));

        var exception = await Assert.ThrowsAsync<WslException>(() => fx.Service.ListAsync());

        Assert.Equal(BridgeErrorCodes.WslTimeout, exception.ErrorCode);
        Assert.Equal("wsl no respondió a tiempo. Inténtalo de nuevo.", exception.ErrorMessage);
    }

    // ------------------------------------------------------------------
    // wsl.update (única que puede elevar)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Update_Success_SucceedsWithoutUac_900sFixedArgv()
    {
        using var fx = WslFixture.Create(FakeProcessRunner.Returning(""));

        var outcome = await fx.Service.UpdateAsync();

        Assert.True(outcome.Succeeded);
        Assert.Equal(0, outcome.ExitCode);
        Assert.False(outcome.Elevated);
        Assert.Null(outcome.Detail);
        Assert.Empty(fx.Elevation.Requests); // nunca UAC preventivo

        var request = Assert.Single(fx.Runner.Requests);
        Assert.Equal<string>(new[] { "--update" }, request.Arguments);
        Assert.Equal(UpdateTimeout, request.Timeout);
    }

    [Fact]
    public async Task Update_AccessDenied_RetriesElevated_WithAllowlistedId_NoPayload()
    {
        using var fx = WslFixture.Create(FakeProcessRunner.Returning("Acceso denegado", 5));

        var outcome = await fx.Service.UpdateAsync();

        // El fake devuelve una elevación OK con {succeeded,exitCode,elevated},
        // que se deserializa limpiamente en WslMutationResult.
        Assert.True(outcome.Succeeded);
        Assert.True(outcome.Elevated);
        Assert.Equal(0, outcome.ExitCode);

        Assert.Single(fx.Runner.Requests);
        var elevated = Assert.Single(fx.Elevation.Requests);
        Assert.Equal(ElevatedOperationIds.WslUpdate, elevated.OperationId);
        Assert.Contains(elevated.OperationId, ElevatedOperationIds.Allowed);
        Assert.Null(elevated.Payload); // wsl.update es de payload cero
    }

    [Fact]
    public async Task Update_AlreadyAdministrator_FailureStaysStructured_NoUac()
    {
        using var fx = WslFixture.Create(FakeProcessRunner.Returning("Acceso denegado", 5));
        fx.Elevation.IsAdministrator = true;

        var outcome = await fx.Service.UpdateAsync();

        Assert.False(outcome.Succeeded);
        Assert.False(outcome.Elevated);
        Assert.Equal(5, outcome.ExitCode);
        Assert.Equal("Acceso denegado", outcome.Detail);
        Assert.Empty(fx.Elevation.Requests);
    }

    [Fact]
    public async Task Update_InvalidArgFixture_IsStructuredResult_NoUac()
    {
        using var fx = WslFixture.Create(FakeProcessRunner.Returning(
            WslFixtures.Read("wsl-invalidarg.txt"), exitCode: 1));

        var outcome = await fx.Service.UpdateAsync();

        Assert.False(outcome.Succeeded);
        Assert.False(outcome.Elevated);
        Assert.Equal(1, outcome.ExitCode);
        Assert.Equal("Código de error: Wsl/UpdatePackage/E_INVALIDARG", outcome.Detail);
        Assert.Empty(fx.Elevation.Requests);
    }

    [Fact]
    public async Task Update_UacCancelled_PropagatesControlledError()
    {
        using var fx = WslFixture.Create(FakeProcessRunner.Returning("Acceso denegado", 5));
        fx.Elevation.Result = ElevationResult.Fail(
            BridgeErrorCodes.ElevationCancelled,
            "El usuario canceló el control de cuentas de usuario.");

        var exception = await Assert.ThrowsAsync<WslException>(() => fx.Service.UpdateAsync());

        Assert.Equal(BridgeErrorCodes.ElevationCancelled, exception.ErrorCode);
        Assert.Equal("El usuario canceló el control de cuentas de usuario.", exception.ErrorMessage);
        Assert.DoesNotContain("   at ", exception.ErrorMessage);
    }

    [Fact]
    public async Task Update_ElevatedWithoutData_ThrowsElevationFailed_ControlledMessage()
    {
        using var fx = WslFixture.Create(FakeProcessRunner.Returning("Acceso denegado", 5));
        fx.Elevation.Result = ElevationResult.Ok(); // sin datos: resultado inválido

        var exception = await Assert.ThrowsAsync<WslException>(() => fx.Service.UpdateAsync());

        Assert.Equal(BridgeErrorCodes.ElevationFailed, exception.ErrorCode);
        Assert.Equal("El proceso elevado no devolvió un resultado válido.", exception.ErrorMessage);
    }

    [Fact]
    public async Task Update_Timeout_ThrowsWslTimeout_NoUac()
    {
        using var fx = WslFixture.Create(
            FakeProcessRunner.Throwing(new ProcessTimeoutException(TimeSpan.FromSeconds(10))));

        var exception = await Assert.ThrowsAsync<WslException>(() => fx.Service.UpdateAsync());

        Assert.Equal(BridgeErrorCodes.WslTimeout, exception.ErrorCode);
        Assert.Equal("La operación wsl superó el tiempo límite y fue terminada.", exception.ErrorMessage);
        Assert.Empty(fx.Elevation.Requests);
    }

    [Fact]
    public async Task Update_WithoutWsl_ThrowsNotInstalled_WithoutAnyRequest()
    {
        using var fx = WslFixture.Create(FakeProcessRunner.Returning(""), withWsl: false);

        var exception = await Assert.ThrowsAsync<WslException>(() => fx.Service.UpdateAsync());

        Assert.Equal(BridgeErrorCodes.NotInstalled, exception.ErrorCode);
        Assert.Equal("No se encontró wsl.exe en la PATH.", exception.ErrorMessage);
        Assert.Empty(fx.Runner.Requests);
        Assert.Empty(fx.Elevation.Requests);
    }

    // ------------------------------------------------------------------
    // wsl.shutdown / wsl.start (jamás elevan)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Shutdown_PermissionsFailure_NeverElevates_120s()
    {
        using var fx = WslFixture.Create(FakeProcessRunner.Returning("Acceso denegado", 5));

        var outcome = await fx.Service.ShutdownAsync();

        Assert.False(outcome.Succeeded);
        Assert.False(outcome.Elevated);
        Assert.Equal(5, outcome.ExitCode);
        Assert.Equal("Acceso denegado", outcome.Detail);
        Assert.Empty(fx.Elevation.Requests); // --shutdown jamás pide UAC

        var request = Assert.Single(fx.Runner.Requests);
        Assert.Equal<string>(new[] { "--shutdown" }, request.Arguments);
        Assert.Equal(ShutdownTimeout, request.Timeout);
    }

    [Fact]
    public async Task Start_BuildsExecTrueArgv_AndNeverElevates()
    {
        using var fx = WslFixture.Create(FakeProcessRunner.Returning("Acceso denegado", 5));

        var outcome = await fx.Service.StartAsync();

        Assert.False(outcome.Succeeded);
        Assert.Empty(fx.Elevation.Requests);

        var request = Assert.Single(fx.Runner.Requests);
        Assert.Equal<string>(new[] { "--exec", "true" }, request.Arguments);
        Assert.Equal(ShutdownTimeout, request.Timeout);
    }

    // ------------------------------------------------------------------
    // Codificación y serialización
    // ------------------------------------------------------------------

    [Fact]
    public async Task AllRequests_DeclareUtf16LeEncoding_WithoutBom()
    {
        using var fx = WslFixture.Create(CreateArgvAwareRunner());

        await fx.Service.GetStatusAsync();
        await fx.Service.ListAsync();
        await fx.Service.UpdateAsync();
        await fx.Service.ShutdownAsync();
        await fx.Service.StartAsync();

        Assert.NotEmpty(fx.Runner.Requests);
        Assert.All(fx.Runner.Requests, request =>
        {
            Assert.NotNull(request.OutputEncoding);
            Assert.Equal(1200, request.OutputEncoding!.CodePage);
            Assert.Empty(request.OutputEncoding.GetPreamble());
        });
    }

    [Fact]
    public async Task ConcurrentOperations_AreSerializedToASingleProcess()
    {
        // wsl no admite ejecuciones solapadas: el semaphore de 1 garantiza UNA
        // ejecución a la vez (status: 3 lecturas, list: 1, update: 1 = 5).
        var counters = new int[2];
        var runner = new FakeProcessRunner(async (_, _) =>
        {
            var current = Interlocked.Increment(ref counters[0]);
            int seen;
            while (current > (seen = Volatile.Read(ref counters[1])))
            {
                if (Interlocked.CompareExchange(ref counters[1], current, seen) == seen)
                {
                    break;
                }
            }

            await Task.Delay(40).ConfigureAwait(false);
            Interlocked.Decrement(ref counters[0]);
            return new ProcessResult { ExitCode = 0 };
        });
        using var fx = WslFixture.Create(runner);

        await Task.WhenAll(
            fx.Service.GetStatusAsync(),
            fx.Service.ListAsync(),
            fx.Service.UpdateAsync());

        Assert.Equal(5, fx.Runner.Requests.Count);
        Assert.Equal(1, counters[1]);
    }

    private static FakeProcessRunner CreateArgvAwareRunner() =>
        new((request, _) => Task.FromResult(
            new ProcessResult
            {
                ExitCode = 0,
                StandardOutput = request.Arguments[0] switch
                {
                    "--version" => WslFixtures.Read("wsl-version.txt"),
                    "--status" => WslFixtures.Read("wsl-status.txt"),
                    "--list" => WslFixtures.Read("wsl-list.txt"),
                    _ => string.Empty,
                },
            }));
}
