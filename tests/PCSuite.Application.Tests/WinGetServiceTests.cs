using System.Text;
using PCSuite.Contracts;
using PCSuite.Core.Elevation;
using PCSuite.Core.Processes;
using PCSuite.Core.WinGet;
using PCSuite.Infrastructure.WinGet;

namespace PCSuite.Application.Tests;

/// <summary>
/// WinGetService: detección, consultas contra fixtures reales, clasificación
/// de códigos de salida, validación ANTES de cualquier proceso, elevación por
/// operación solo cuando el fallo parece de permisos y serialización de 1
/// invocación (winget no admite ejecuciones concurrentes).
/// </summary>
public sealed class WinGetServiceTests
{
    private static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(120);

    private static readonly TimeSpan MutationTimeout = TimeSpan.FromSeconds(900);

    // ------------------------------------------------------------------
    // Detección (Stage 7: Detection + permisos)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Detect_WithoutWinget_ReturnsNotInstalled_WithoutRequests()
    {
        using var fx = WinGetFixture.Create(FakeProcessRunner.Returning(""), withWinGet: false);
        fx.Elevation.IsAdministrator = true;

        var detection = await fx.Service.DetectAsync();

        Assert.False(detection.Installed);
        Assert.Null(detection.Version);
        Assert.Null(detection.Path);
        Assert.True(detection.IsAdministrator);
        Assert.Empty(fx.Runner.Requests);
        Assert.Empty(fx.Elevation.Requests);
    }

    [Fact]
    public async Task Detect_WithWinget_ProbesVersion_WithFixedArgvAndUtf8()
    {
        using var fx = WinGetFixture.Create(FakeProcessRunner.Returning("v1.29.380"));
        fx.Elevation.IsAdministrator = false;

        var detection = await fx.Service.DetectAsync();

        Assert.True(detection.Installed);
        Assert.Equal("1.29.380", detection.Version); // la 'v' inicial se elimina
        Assert.False(detection.IsAdministrator);
        Assert.Equal(fx.WingetPath, detection.Path);

        var request = Assert.Single(fx.Runner.Requests);
        Assert.Equal(fx.WingetPath, request.FileName);
        Assert.Equal<string>(new[] { "--version" }, request.Arguments);
        Assert.Equal(VersionTimeout, request.Timeout);
        Assert.Equal("utf-8", request.OutputEncoding!.WebName);
    }

    [Fact]
    public async Task Detect_VersionProbeFails_StillReportsInstalled()
    {
        using var fx = WinGetFixture.Create(
            FakeProcessRunner.Throwing(new IOException("problema de versión")));

        var detection = await fx.Service.DetectAsync();

        Assert.True(detection.Installed);
        Assert.Null(detection.Version);
        Assert.Equal(fx.WingetPath, detection.Path);
    }

    // ------------------------------------------------------------------
    // Consultas (fixtures reales)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Search_ParsesRealFixture_WithExactArgv()
    {
        using var fx = WinGetFixture.Create(
            FakeProcessRunner.Returning(WinGetFixtures.Read("winget-search.txt")));

        var packages = await fx.Service.SearchAsync("visual studio code");

        Assert.Equal(4, packages.Count);
        Assert.Equal(
            new WinGetPackage("Microsoft.VisualStudioCode", "Microsoft Visual Studio Code", "1.138.0"),
            packages[0]);
        Assert.Equal(
            new WinGetPackage(
                "Microsoft.VisualStudioCode.Insiders.CLI",
                "Microsoft Visual Studio Code Insiders CLI",
                "1.127.0"),
            packages[3]);

        var request = Assert.Single(fx.Runner.Requests);
        Assert.Equal<string>(
            new[]
            {
                "search", "visual studio code", "--source", "winget", "--count", "25",
                "--disable-interactivity", "--accept-source-agreements",
            },
            request.Arguments);
        Assert.Equal(QueryTimeout, request.Timeout);
        Assert.Equal("utf-8", request.OutputEncoding!.WebName);
    }

    [Fact]
    public async Task Search_NoMatchExitCode_ReturnsEmpty()
    {
        using var fx = WinGetFixture.Create(FakeProcessRunner.Returning(
            WinGetFixtures.Read("winget-nomatch.txt"),
            WinGetExitCodes.NoApplicationsFound));

        Assert.Empty(await fx.Service.SearchAsync("zzz-sin-coincidencias"));
    }

    [Fact]
    public async Task Search_RowsOnNonzeroExit_Win()
    {
        // Filas interpretables se devuelven aunque winget salga con un
        // código de salida no cero.
        using var fx = WinGetFixture.Create(FakeProcessRunner.Returning(
            WinGetFixtures.Read("winget-search.txt"),
            WinGetExitCodes.NoUpdateAvailable));

        Assert.Equal(4, (await fx.Service.SearchAsync("visual studio code")).Count);
    }

    [Fact]
    public async Task Search_UnknownFailureWithoutRows_ThrowsWingetFailed_ControlledMessage()
    {
        using var fx = WinGetFixture.Create(FakeProcessRunner.Returning("", 4711));

        var exception = await Assert.ThrowsAsync<WinGetException>(
            () => fx.Service.SearchAsync("x"));

        Assert.Equal(BridgeErrorCodes.WingetFailed, exception.ErrorCode);
        Assert.Equal("winget falló con el código de salida 4711.", exception.ErrorMessage);
        Assert.DoesNotContain("   at ", exception.ErrorMessage);
    }

    [Fact]
    public async Task List_StderrOnlyFailure_UsesLastLineAsMessage()
    {
        var runner = new FakeProcessRunner((_, _) => Task.FromResult(
            new ProcessResult { ExitCode = 9, StandardError = "fallo serio de winget" }));
        using var fx = WinGetFixture.Create(runner);

        var exception = await Assert.ThrowsAsync<WinGetException>(() => fx.Service.ListAsync());

        Assert.Equal(BridgeErrorCodes.WingetFailed, exception.ErrorCode);
        Assert.Equal("fallo serio de winget", exception.ErrorMessage);
    }

    [Fact]
    public async Task List_ParsesRealFixture_97Packages()
    {
        using var fx = WinGetFixture.Create(
            FakeProcessRunner.Returning(WinGetFixtures.Read("winget-list.txt")));

        var packages = await fx.Service.ListAsync();

        Assert.Equal(97, packages.Count);

        var git = Assert.Single(packages, package => package.Id == "Git.Git");
        Assert.Equal("2.55.0.5", git.Version);
        Assert.Null(git.Available);

        var chrome = Assert.Single(packages, package => package.Id == "Google.Chrome.EXE");
        Assert.Equal("154.0.8037.58", chrome.Available);

        var epic = Assert.Single(packages, package => package.Id == "EpicGames.EpicOnlineServices");
        Assert.Equal("Epic Online Services", epic.Name);
        Assert.Equal("4.3.1", epic.Available);

        // Espacio interno en la celda de versión: no se parte.
        Assert.Contains(packages, package => package.Id == "Python.Launcher" && package.Version == "< 3.14.7");
    }

    [Fact]
    public async Task Updates_ParsesBothTables()
    {
        using var fx = WinGetFixture.Create(
            FakeProcessRunner.Returning(WinGetFixtures.Read("winget-upgrade.txt")));

        var packages = await fx.Service.UpdatesAsync();

        Assert.Equal(6, packages.Count);
        var msys = Assert.Single(packages, package => package.Id == "MSYS2.MSYS2");
        Assert.Equal("20251213", msys.Version);
        Assert.Equal("20260611", msys.Available);
        Assert.DoesNotContain(
            packages,
            package => package.Id.Contains("actualizaciones", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Updates_NoUpdateExitCode_ReturnsEmpty()
    {
        using var fx = WinGetFixture.Create(
            FakeProcessRunner.Returning("", WinGetExitCodes.NoUpdateAvailable));

        Assert.Empty(await fx.Service.UpdatesAsync());
    }

    [Fact]
    public async Task List_WithoutWinget_ThrowsNotInstalled_WithoutAnyRequest()
    {
        using var fx = WinGetFixture.Create(FakeProcessRunner.Returning(""), withWinGet: false);

        var exception = await Assert.ThrowsAsync<WinGetException>(() => fx.Service.ListAsync());

        Assert.Equal(BridgeErrorCodes.NotInstalled, exception.ErrorCode);
        Assert.Empty(fx.Runner.Requests);
        Assert.Empty(fx.Elevation.Requests);
    }

    [Fact]
    public async Task Query_Timeout_ThrowsWingetTimeout()
    {
        using var fx = WinGetFixture.Create(
            FakeProcessRunner.Throwing(new ProcessTimeoutException(TimeSpan.FromSeconds(5))));

        var exception = await Assert.ThrowsAsync<WinGetException>(() => fx.Service.ListAsync());

        Assert.Equal(BridgeErrorCodes.WingetTimeout, exception.ErrorCode);
    }

    // ------------------------------------------------------------------
    // Validación sin tocar procesos (§10.1: nunca un comando arbitrario)
    // ------------------------------------------------------------------

    [Fact]
    public void Search_InvalidQuery_ThrowsBeforeAnyRequest()
    {
        using var fx = WinGetFixture.Create(FakeProcessRunner.Returning(""));

        var exception = Assert.Throws<WinGetException>(
            () => { _ = fx.Service.SearchAsync("-borrar"); });

        Assert.Equal(BridgeErrorCodes.MalformedMessage, exception.ErrorCode);
        Assert.Empty(fx.Runner.Requests);
        Assert.Empty(fx.Elevation.Requests);
    }

    [Fact]
    public void Install_InvalidPackageId_ThrowsBeforeAnyRequest()
    {
        using var fx = WinGetFixture.Create(FakeProcessRunner.Returning(""));

        var exception = Assert.Throws<WinGetException>(
            () => { _ = fx.Service.InstallAsync("id con espacios"); });

        Assert.Equal(BridgeErrorCodes.MalformedMessage, exception.ErrorCode);
        Assert.Empty(fx.Runner.Requests);
        Assert.Empty(fx.Elevation.Requests);
    }

    [Fact]
    public async Task Install_WithoutWinget_ThrowsNotInstalled_WithoutAnyRequest()
    {
        using var fx = WinGetFixture.Create(FakeProcessRunner.Returning(""), withWinGet: false);

        var exception = await Assert.ThrowsAsync<WinGetException>(
            () => fx.Service.InstallAsync("7zip.7zip"));

        Assert.Equal(BridgeErrorCodes.NotInstalled, exception.ErrorCode);
        Assert.Empty(fx.Runner.Requests);
        Assert.Empty(fx.Elevation.Requests);
    }

    // ------------------------------------------------------------------
    // Mutaciones: primero SIN privilegios, elevación solo si hace falta
    // ------------------------------------------------------------------

    [Fact]
    public async Task Install_Success_SucceedsWithoutUac_ExactArgv()
    {
        using var fx = WinGetFixture.Create(FakeProcessRunner.Returning(""));

        var outcome = await fx.Service.InstallAsync("7zip.7zip");

        Assert.True(outcome.Succeeded);
        Assert.Equal("7zip.7zip", outcome.PackageId);
        Assert.Equal(0, outcome.ExitCode);
        Assert.False(outcome.Elevated);
        Assert.Null(outcome.Detail);
        Assert.Empty(fx.Elevation.Requests); // nunca UAC preventivo

        var request = Assert.Single(fx.Runner.Requests);
        Assert.Equal<string>(
            new[]
            {
                "install", "--id", "7zip.7zip", "--exact", "--source", "winget", "--silent",
                "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity",
            },
            request.Arguments);
        Assert.Equal(MutationTimeout, request.Timeout);
    }

    [Fact]
    public async Task Upgrade_BuildsUpgradeArgv()
    {
        using var fx = WinGetFixture.Create(FakeProcessRunner.Returning(""));

        var outcome = await fx.Service.UpgradeAsync("Git.Git");

        Assert.True(outcome.Succeeded);
        Assert.Equal<string>(
            new[]
            {
                "upgrade", "--id", "Git.Git", "--exact", "--source", "winget", "--silent",
                "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity",
            },
            Assert.Single(fx.Runner.Requests).Arguments);
    }

    [Fact]
    public async Task Uninstall_OmitsPackageAgreementsFlag()
    {
        using var fx = WinGetFixture.Create(FakeProcessRunner.Returning(""));

        var outcome = await fx.Service.UninstallAsync("7zip.7zip");

        Assert.True(outcome.Succeeded);
        var request = Assert.Single(fx.Runner.Requests);
        Assert.Equal<string>(
            new[]
            {
                "uninstall", "--id", "7zip.7zip", "--exact", "--source", "winget", "--silent",
                "--disable-interactivity", "--accept-source-agreements",
            },
            request.Arguments);
        Assert.DoesNotContain("--accept-package-agreements", request.Arguments);
    }

    [Fact]
    public async Task Install_AccessDeniedExit_RetriesElevated_WithPayloadRoundtrip()
    {
        using var fx = WinGetFixture.Create(FakeProcessRunner.Returning("Acceso denegado", 5));
        fx.Elevation.Result = ElevationResult.Ok(BridgeJson.ToJson(new WinGetMutationResult
        {
            PackageId = "7zip.7zip",
            Succeeded = true,
            ExitCode = 0,
            Elevated = true,
        }));

        var outcome = await fx.Service.InstallAsync("7zip.7zip");

        Assert.True(outcome.Succeeded);
        Assert.True(outcome.Elevated);
        Assert.Equal(0, outcome.ExitCode);

        Assert.Single(fx.Runner.Requests); // un intento sin privilegios
        var elevated = Assert.Single(fx.Elevation.Requests);
        Assert.Equal(ElevatedOperationIds.WingetInstall, elevated.OperationId);
        Assert.Equal(
            "7zip.7zip",
            elevated.Payload!.Value.GetProperty("packageId").GetString());
    }

    [Fact]
    public async Task Install_OutputAccessDeniedMarker_RetriesElevated()
    {
        // Código de salida genérico pero HRESULT en la salida → candidato a UAC.
        using var fx = WinGetFixture.Create(
            FakeProcessRunner.Returning("Error 0x80070005 al escribir el destino", 1));

        var outcome = await fx.Service.InstallAsync("7zip.7zip");

        Assert.True(outcome.Elevated); // resultado por defecto del fake: OK
        Assert.Equal(
            ElevatedOperationIds.WingetInstall,
            Assert.Single(fx.Elevation.Requests).OperationId);
    }

    [Fact]
    public async Task Install_AlreadyAdministrator_FailureStaysStructured_NoUac()
    {
        using var fx = WinGetFixture.Create(FakeProcessRunner.Returning("Acceso denegado", 5));
        fx.Elevation.IsAdministrator = true;

        var outcome = await fx.Service.InstallAsync("7zip.7zip");

        Assert.False(outcome.Succeeded);
        Assert.False(outcome.Elevated);
        Assert.Equal(5, outcome.ExitCode);
        Assert.Equal("Acceso denegado", outcome.Detail);
        Assert.Empty(fx.Elevation.Requests); // ya admin: reintentar elevado no aporta
    }

    [Fact]
    public async Task Install_UnknownFailure_IsStructuredResult_NoUac()
    {
        using var fx = WinGetFixture.Create(FakeProcessRunner.Returning(
            "No se encontró el paquete.",
            WinGetExitCodes.NoApplicationsFound));

        var outcome = await fx.Service.InstallAsync("paquete.que.no-existe");

        Assert.False(outcome.Succeeded);
        Assert.False(outcome.Elevated);
        Assert.Equal(WinGetExitCodes.NoApplicationsFound, outcome.ExitCode);
        Assert.Equal("No se encontró el paquete.", outcome.Detail);
        Assert.Empty(fx.Elevation.Requests);
    }

    [Fact]
    public async Task Install_UacCancelled_PropagatesControlledError()
    {
        using var fx = WinGetFixture.Create(FakeProcessRunner.Returning("Acceso denegado", 5));
        fx.Elevation.Result = ElevationResult.Fail(
            BridgeErrorCodes.ElevationCancelled,
            "El usuario canceló el control de cuentas de usuario.");

        var exception = await Assert.ThrowsAsync<WinGetException>(
            () => fx.Service.InstallAsync("7zip.7zip"));

        Assert.Equal(BridgeErrorCodes.ElevationCancelled, exception.ErrorCode);
        Assert.Equal(
            "El usuario canceló el control de cuentas de usuario.",
            exception.ErrorMessage);
        Assert.DoesNotContain("   at ", exception.ErrorMessage);
    }

    [Fact]
    public async Task Install_ElevatedWithoutData_ThrowsElevationFailed()
    {
        using var fx = WinGetFixture.Create(FakeProcessRunner.Returning("Acceso denegado", 5));
        fx.Elevation.Result = ElevationResult.Ok(); // sin datos: resultado inválido

        var exception = await Assert.ThrowsAsync<WinGetException>(
            () => fx.Service.InstallAsync("7zip.7zip"));

        Assert.Equal(BridgeErrorCodes.ElevationFailed, exception.ErrorCode);
    }

    [Fact]
    public async Task Install_Timeout_ThrowsWingetTimeout_NoUac()
    {
        using var fx = WinGetFixture.Create(
            FakeProcessRunner.Throwing(new ProcessTimeoutException(TimeSpan.FromSeconds(10))));

        var exception = await Assert.ThrowsAsync<WinGetException>(
            () => fx.Service.InstallAsync("7zip.7zip"));

        Assert.Equal(BridgeErrorCodes.WingetTimeout, exception.ErrorCode);
        Assert.Empty(fx.Elevation.Requests); // timeout no es falta de permisos
    }

    [Fact]
    public async Task UpgradeAll_ElevatesWithEmptyPackageId()
    {
        using var fx = WinGetFixture.Create(FakeProcessRunner.Returning("", 740));

        var outcome = await fx.Service.UpgradeAllAsync();

        Assert.True(outcome.Succeeded);
        Assert.True(outcome.Elevated);
        Assert.Null(outcome.PackageId);

        var elevated = Assert.Single(fx.Elevation.Requests);
        Assert.Equal(ElevatedOperationIds.WingetUpgradeAll, elevated.OperationId);
        Assert.Equal(
            string.Empty,
            elevated.Payload!.Value.GetProperty("packageId").GetString());
    }

    // ------------------------------------------------------------------
    // Codificación y serialización
    // ------------------------------------------------------------------

    [Fact]
    public async Task AllRequests_DeclareUtf8Encoding_WithoutBom()
    {
        var runner = new FakeProcessRunner((request, _) => Task.FromResult(
            new ProcessResult
            {
                ExitCode = 0,
                StandardOutput = request.Arguments.Count > 0 && request.Arguments[0] == "--version"
                    ? "v1.29.380"
                    : WinGetFixtures.Read("winget-search.txt"),
            }));
        using var fx = WinGetFixture.Create(runner);

        await fx.Service.DetectAsync();
        await fx.Service.SearchAsync("visual studio code");
        await fx.Service.ListAsync();
        await fx.Service.InstallAsync("7zip.7zip");

        Assert.NotEmpty(fx.Runner.Requests);
        Assert.All(fx.Runner.Requests, request =>
        {
            Assert.NotNull(request.OutputEncoding);
            Assert.Equal("utf-8", request.OutputEncoding!.WebName);
            Assert.Empty(request.OutputEncoding.GetPreamble());
        });
    }

    [Fact]
    public async Task ConcurrentQueries_AreSerializedToASingleProcess()
    {
        // winget en paralelo corrompe el índice de fuentes (0x80071130):
        // el semaphore de 1 debe garantizar UNA sola ejecución a la vez.
        var counters = new int[2]; // [0] en curso, [1] máximo visto
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
        using var fx = WinGetFixture.Create(runner);

        await Task.WhenAll(
            fx.Service.SearchAsync("uno"),
            fx.Service.ListAsync(),
            fx.Service.UpdatesAsync());

        Assert.Equal(3, fx.Runner.Requests.Count);
        Assert.Equal(1, counters[1]);
    }
}
