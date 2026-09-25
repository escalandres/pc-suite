using PCSuite.Contracts;
using PCSuite.Core.ClamAV;
using PCSuite.Core.Elevation;
using PCSuite.Core.Processes;
using PCSuite.Core.WinGet;

namespace PCSuite.Application.Tests;

/// <summary>
/// ClamAvService (Task 008): detección con degradación controlada, instalación
/// delegada en winget con id de paquete fijo, freshclam con elevación POR
/// OPERACIÓN solo si falla por permisos (nunca UAC preventivo y nunca si ya es
/// administrador), escaneos SIN elevación, validación de la ruta del
/// customScan, historial solo en resultados estructurados y serie de 1 para
/// escaneos (status/history quedan FUERA de la puerta).
/// </summary>
public sealed class ClamAvServiceTests
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan UpdateTimeout = TimeSpan.FromSeconds(900);
    private static readonly TimeSpan QuickScanTimeout = TimeSpan.FromSeconds(900);
    private static readonly TimeSpan CustomScanTimeout = TimeSpan.FromSeconds(3600);

    // ------------------------------------------------------------------
    // clamav.status
    // ------------------------------------------------------------------

    [Fact]
    public async Task Status_WithoutClamAv_ReturnsNotInstalled_WithoutAnyRequest()
    {
        using var fx = ClamAvFixture.Create(FakeProcessRunner.Returning(""), withClamAv: false);

        var status = await fx.Service.GetStatusAsync();

        Assert.False(status.Installed);
        Assert.Null(status.Path);
        Assert.Null(status.Version);
        Assert.Null(status.Definitions);
        Assert.False(status.CanUpdateDefinitions);
        Assert.Empty(fx.Runner.Requests);
        Assert.Empty(fx.Elevation.Requests);
    }

    [Fact]
    public async Task Status_WithClamAv_ParsesVersion_UsesOemEncodingAndReadTimeout()
    {
        using var fx = ClamAvFixture.Create(FakeProcessRunner.Returning(ClamAvFixtures.Version));

        var status = await fx.Service.GetStatusAsync();

        Assert.True(status.Installed);
        Assert.Equal(fx.ClamScanPath, status.Path);
        Assert.Equal("1.5.4", status.Version);
        Assert.Equal("7123456", status.Definitions);
        Assert.True(status.CanUpdateDefinitions);

        var request = Assert.Single(fx.Runner.Requests);
        Assert.Equal(fx.ClamScanPath, request.FileName);
        Assert.Equal<string>(["--version"], request.Arguments);
        Assert.Equal(ReadTimeout, request.Timeout);
        Assert.Null(request.OutputEncoding); // binarios CRT: codepage OEM (a diferencia de wsl)
    }

    [Fact]
    public async Task Status_WithoutFreshclam_ReportsCannotUpdateDefinitions()
    {
        using var fx = ClamAvFixture.Create(
            FakeProcessRunner.Returning(ClamAvFixtures.Version), withFreshclam: false);

        var status = await fx.Service.GetStatusAsync();

        Assert.True(status.Installed);
        Assert.False(status.CanUpdateDefinitions);
    }

    [Fact]
    public async Task Status_VersionFailure_StaysInstalledWithoutVersion()
    {
        using var fx = ClamAvFixture.Create(FakeProcessRunner.Returning("", exitCode: 1));

        var status = await fx.Service.GetStatusAsync();

        Assert.True(status.Installed);
        Assert.Null(status.Version);
        Assert.Null(status.Definitions);
    }

    [Fact]
    public async Task Status_Timeout_ThrowsClamAvTimeout_WithControlledMessage()
    {
        using var fx = ClamAvFixture.Create(
            FakeProcessRunner.Throwing(new ProcessTimeoutException(TimeSpan.FromSeconds(10))));

        var exception = await Assert.ThrowsAsync<ClamAvException>(() => fx.Service.GetStatusAsync());

        Assert.Equal(BridgeErrorCodes.ClamAvTimeout, exception.ErrorCode);
        Assert.Equal("ClamAV no respondió a tiempo. Inténtalo de nuevo.", exception.ErrorMessage);
        Assert.DoesNotContain("   at ", exception.ErrorMessage);
    }

    [Fact]
    public async Task Status_ProcessFailure_StaysInstalledWithoutVersion()
    {
        // Degradación controlada: un fallo de proceso no es "no instalado"
        // (clamscan ya se localizó) ni un error interno para la UI.
        using var fx = ClamAvFixture.Create(FakeProcessRunner.Throwing(new IOException("boom")));

        var status = await fx.Service.GetStatusAsync();

        Assert.True(status.Installed);
        Assert.Null(status.Version);
    }

    // ------------------------------------------------------------------
    // clamav.install (delega en winget con id fijo)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Install_WhenAlreadyInstalled_SkipsWinget()
    {
        using var fx = ClamAvFixture.Create(FakeProcessRunner.Returning(""));

        var outcome = await fx.Service.InstallAsync();

        Assert.True(outcome.Succeeded);
        Assert.Equal(0, outcome.ExitCode);
        Assert.False(outcome.Elevated);
        Assert.Equal("ClamAV ya está instalado.", outcome.Detail);
        Assert.Empty(fx.Winget.InstallCalls);
    }

    [Fact]
    public async Task Install_WhenMissing_DelegatesToWingetWithFixedPackageId()
    {
        using var fx = ClamAvFixture.Create(FakeProcessRunner.Returning(""), withClamAv: false);
        fx.Winget.InstallOutcome = new WinGetMutationOutcome(
            "Cisco.ClamAV", Succeeded: true, ExitCode: 0, Elevated: true, Detail: "Se instaló Cisco.ClamAV");

        var outcome = await fx.Service.InstallAsync();

        Assert.Equal(["Cisco.ClamAV"], fx.Winget.InstallCalls); // id fijo, nunca input del usuario
        Assert.True(outcome.Succeeded);
        Assert.Equal(0, outcome.ExitCode);
        Assert.True(outcome.Elevated);
        Assert.Equal("Se instaló Cisco.ClamAV", outcome.Detail);
        // El UAC, si hizo falta, lo resolvió el Task 006 dentro de winget.
        Assert.Empty(fx.Elevation.Requests);
    }

    [Fact]
    public async Task Install_WhenWingetFailsControlled_MapsCodeAndMessageWithoutLeak()
    {
        using var fx = ClamAvFixture.Create(FakeProcessRunner.Returning(""), withClamAv: false);
        fx.Winget.InstallException = new WinGetException(
            BridgeErrorCodes.ElevationCancelled,
            "El usuario canceló el control de cuentas de usuario.");

        var exception = await Assert.ThrowsAsync<ClamAvException>(() => fx.Service.InstallAsync());

        Assert.Equal(BridgeErrorCodes.ElevationCancelled, exception.ErrorCode);
        Assert.Equal("El usuario canceló el control de cuentas de usuario.", exception.ErrorMessage);
        Assert.DoesNotContain("   at ", exception.ErrorMessage);
    }

    // ------------------------------------------------------------------
    // clamav.updateDefinitions (freshclam; eleva SOLO si falla por permisos)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Update_Success_UsesFreshclamEmptyArgvAndItsDirectory_NoUac()
    {
        using var fx = ClamAvFixture.Create(FakeProcessRunner.Returning(ClamAvFixtures.FreshclamOk));

        var outcome = await fx.Service.UpdateDefinitionsAsync();

        Assert.True(outcome.Succeeded);
        Assert.Equal(0, outcome.ExitCode);
        Assert.False(outcome.Elevated);
        Assert.Null(outcome.Detail); // sin errores → sin detalle
        Assert.Empty(fx.Elevation.Requests);

        var request = Assert.Single(fx.Runner.Requests);
        Assert.Equal(fx.FreshclamPath, request.FileName);
        Assert.Empty(request.Arguments);
        Assert.Equal(Path.GetDirectoryName(fx.FreshclamPath), request.WorkingDirectory);
        Assert.Equal(UpdateTimeout, request.Timeout);
        Assert.Null(request.OutputEncoding);
    }

    [Fact]
    public async Task Update_PermissionFailure_RetriesElevated_WithAllowListedIdAndNoPayload()
    {
        using var fx = ClamAvFixture.Create(
            FakeProcessRunner.Returning(ClamAvFixtures.PermissionDenied, exitCode: 1));

        var outcome = await fx.Service.UpdateDefinitionsAsync();

        Assert.True(outcome.Elevated);
        Assert.True(outcome.Succeeded); // resultado del hijo elevado (fake por defecto: ok)

        var elevated = Assert.Single(fx.Elevation.Requests);
        Assert.Equal(ElevatedOperationIds.ClamavUpdateDefinitions, elevated.OperationId);
        Assert.Contains(elevated.OperationId, ElevatedOperationIds.Allowed); // doble barrera
        Assert.Null(elevated.Payload); // payload cero: el argv lo fija el proceso elevado
        // Solo freshclam corrió en este proceso: la operación elevada vive en otro.
        Assert.Single(fx.Runner.Requests);
    }

    [Fact]
    public async Task Update_NonPermissionFailure_NoUac_StructuredFailure()
    {
        using var fx = ClamAvFixture.Create(
            FakeProcessRunner.Returning("ERROR: freshclam.conf not found", exitCode: 1));

        var outcome = await fx.Service.UpdateDefinitionsAsync();

        Assert.False(outcome.Succeeded);
        Assert.False(outcome.Elevated);
        Assert.Equal(1, outcome.ExitCode);
        Assert.Equal("ERROR: freshclam.conf not found", outcome.Detail);
        Assert.Empty(fx.Elevation.Requests);
    }

    [Fact]
    public async Task Update_AlreadyAdministrator_NoUacRetry()
    {
        using var fx = ClamAvFixture.Create(
            FakeProcessRunner.Returning(ClamAvFixtures.PermissionDenied, exitCode: 1));
        fx.Elevation.IsAdministrator = true;

        var outcome = await fx.Service.UpdateDefinitionsAsync();

        Assert.False(outcome.Succeeded);
        Assert.False(outcome.Elevated);
        Assert.Empty(fx.Elevation.Requests); // siendo admin, un retry elevado no arregla nada
    }

    [Fact]
    public async Task Update_UacCancelled_PropagatesControlledError()
    {
        using var fx = ClamAvFixture.Create(
            FakeProcessRunner.Returning(ClamAvFixtures.PermissionDenied, exitCode: 1));
        fx.Elevation.Result = ElevationResult.Fail(
            BridgeErrorCodes.ElevationCancelled,
            "El usuario canceló el control de cuentas de usuario.");

        var exception = await Assert.ThrowsAsync<ClamAvException>(() => fx.Service.UpdateDefinitionsAsync());

        Assert.Equal(BridgeErrorCodes.ElevationCancelled, exception.ErrorCode);
        Assert.DoesNotContain("   at ", exception.ErrorMessage);
    }

    [Fact]
    public async Task Update_ElevatedWithoutData_ThrowsElevationFailed()
    {
        using var fx = ClamAvFixture.Create(
            FakeProcessRunner.Returning(ClamAvFixtures.PermissionDenied, exitCode: 1));
        fx.Elevation.Result = ElevationResult.Ok();

        var exception = await Assert.ThrowsAsync<ClamAvException>(() => fx.Service.UpdateDefinitionsAsync());

        Assert.Equal(BridgeErrorCodes.ElevationFailed, exception.ErrorCode);
        Assert.Equal("El proceso elevado no devolvió un resultado válido.", exception.ErrorMessage);
    }

    [Fact]
    public async Task Update_Timeout_ThrowsClamAvTimeout_WithoutUac()
    {
        using var fx = ClamAvFixture.Create(
            FakeProcessRunner.Throwing(new ProcessTimeoutException(TimeSpan.FromSeconds(10))));

        var exception = await Assert.ThrowsAsync<ClamAvException>(() => fx.Service.UpdateDefinitionsAsync());

        Assert.Equal(BridgeErrorCodes.ClamAvTimeout, exception.ErrorCode);
        Assert.Equal("La actualización de definiciones superó el tiempo límite y fue terminada.", exception.ErrorMessage);
        Assert.Empty(fx.Elevation.Requests);
    }

    [Fact]
    public async Task Update_WithoutClamAv_ThrowsNotInstalled_WithoutAnyRequest()
    {
        using var fx = ClamAvFixture.Create(FakeProcessRunner.Returning(""), withClamAv: false);

        var exception = await Assert.ThrowsAsync<ClamAvException>(() => fx.Service.UpdateDefinitionsAsync());

        Assert.Equal(BridgeErrorCodes.NotInstalled, exception.ErrorCode);
        Assert.Equal("ClamAV no está instalado. Usa clamav.install para instalarlo.", exception.ErrorMessage);
        Assert.Empty(fx.Runner.Requests);
        Assert.Empty(fx.Elevation.Requests);
    }

    [Fact]
    public async Task Update_WithoutFreshclam_ThrowsNotInstalled()
    {
        using var fx = ClamAvFixture.Create(FakeProcessRunner.Returning(""), withFreshclam: false);

        var exception = await Assert.ThrowsAsync<ClamAvException>(() => fx.Service.UpdateDefinitionsAsync());

        Assert.Equal(BridgeErrorCodes.NotInstalled, exception.ErrorCode);
        Assert.Equal("No se encontró freshclam.exe junto a clamscan.exe.", exception.ErrorMessage);
    }

    // ------------------------------------------------------------------
    // Escaneos (nunca elevan)
    // ------------------------------------------------------------------

    [Fact]
    public async Task QuickScan_Success_BuildsFixedArgv_900s_NoUac()
    {
        using var fx = ClamAvFixture.Create(FakeProcessRunner.Returning(ClamAvFixtures.CleanScan));

        var outcome = await fx.Service.QuickScanAsync();

        Assert.True(outcome.Succeeded);
        Assert.Equal(0, outcome.ExitCode);
        Assert.Equal(1234, outcome.ScannedFiles);
        Assert.Equal(42, outcome.ScannedDirectories);
        Assert.Equal(0, outcome.InfectedFiles);
        Assert.Equal("1.5.4", outcome.EngineVersion);
        Assert.Equal(7123456, outcome.KnownViruses);
        Assert.Null(outcome.Detail);
        Assert.Empty(fx.Elevation.Requests);

        var request = Assert.Single(fx.Runner.Requests);
        Assert.Equal("--infected", request.Arguments[0]);
        Assert.Equal("--recursive", request.Arguments[1]);
        Assert.All(request.Arguments.Skip(2), target => Assert.True(Path.IsPathRooted(target)));
        Assert.Equal(QuickScanTimeout, request.Timeout);
    }

    [Fact]
    public async Task QuickScan_WithThreats_Succeeds_ReportsThreatsAndSummaryCounts()
    {
        using var fx = ClamAvFixture.Create(
            FakeProcessRunner.Returning(ClamAvFixtures.InfectedScan, exitCode: 1));

        var outcome = await fx.Service.QuickScanAsync();

        Assert.True(outcome.Succeeded); // exit 1 = amenazas halladas, NO fallo
        Assert.Equal(1, outcome.ExitCode);
        Assert.Equal(2, outcome.Threats.Count);
        Assert.Equal(2, outcome.InfectedFiles);
        Assert.Equal(@"C:\Users\test\Downloads\evil.com", outcome.Threats[0].File);
        Assert.Equal("Win.Trojan.FakeMS-123", outcome.Threats[0].Threat);
        Assert.Null(outcome.Detail);
        Assert.Empty(fx.Elevation.Requests);
    }

    [Fact]
    public async Task FullScan_Exit2WithSummary_SucceedsAsPartial_WithFixedDetail()
    {
        using var fx = ClamAvFixture.Create(
            FakeProcessRunner.Returning(ClamAvFixtures.PartialScan, exitCode: 2));

        var outcome = await fx.Service.FullScanAsync();

        Assert.True(outcome.Succeeded);
        Assert.Equal(2, outcome.ExitCode);
        Assert.Equal(
            "Escaneo parcial: alguno de los archivos o carpetas no se pudo leer.",
            outcome.Detail); // mensaje fijo: jamás salida cruda
        Assert.Equal(10, outcome.ScannedFiles);
        Assert.Empty(fx.Elevation.Requests);

        var request = Assert.Single(fx.Runner.Requests);
        Assert.Equal("--infected", request.Arguments[0]);
        Assert.True(request.Arguments.Count >= 3); // al menos un disco fijo listo (C:\)
        Assert.All(request.Arguments.Skip(2), root => Assert.True(Path.IsPathRooted(root)));
        Assert.Equal(TimeSpan.FromSeconds(7200), request.Timeout);
    }

    [Fact]
    public async Task FullScan_Exit2WithoutSummary_FailsStructured_WithSanitizedDetail()
    {
        using var fx = ClamAvFixture.Create(
            FakeProcessRunner.Returning(ClamAvFixtures.ErrorNoSummary, exitCode: 2));

        var outcome = await fx.Service.FullScanAsync();

        Assert.False(outcome.Succeeded);
        Assert.Equal(2, outcome.ExitCode);
        Assert.Equal(ClamAvFixtures.ErrorNoSummary, outcome.Detail);
    }

    [Fact]
    public async Task Scan_UnknownExitCode_FailsWithExitCodeFallbackMessage()
    {
        using var fx = ClamAvFixture.Create(FakeProcessRunner.Returning("", exitCode: 3));

        var outcome = await fx.Service.QuickScanAsync();

        Assert.False(outcome.Succeeded);
        Assert.Equal("ClamAV falló con el código de salida 3.", outcome.Detail);
    }

    [Fact]
    public async Task Scan_WithoutClamAv_ThrowsNotInstalled_WithoutRequestOrHistory()
    {
        using var fx = ClamAvFixture.Create(FakeProcessRunner.Returning(""), withClamAv: false);

        var exception = await Assert.ThrowsAsync<ClamAvException>(() => fx.Service.QuickScanAsync());

        Assert.Equal(BridgeErrorCodes.NotInstalled, exception.ErrorCode);
        Assert.Equal("ClamAV no está instalado. Usa clamav.install para instalarlo.", exception.ErrorMessage);
        Assert.Empty(fx.Runner.Requests);
        Assert.Empty(fx.History.Read()); // las excepciones no registran entrada
    }

    [Fact]
    public async Task Scan_Timeout_ThrowsClamAvTimeout_WithoutHistoryEntry()
    {
        using var fx = ClamAvFixture.Create(
            FakeProcessRunner.Throwing(new ProcessTimeoutException(TimeSpan.FromSeconds(10))));

        var exception = await Assert.ThrowsAsync<ClamAvException>(() => fx.Service.QuickScanAsync());

        Assert.Equal(BridgeErrorCodes.ClamAvTimeout, exception.ErrorCode);
        Assert.Equal("El escaneo superó el tiempo límite y fue terminado.", exception.ErrorMessage);
        Assert.Empty(fx.History.Read());
    }

    // ------------------------------------------------------------------
    // clamav.customScan: la ÚNICA entrada estructurada del frontend
    // ------------------------------------------------------------------

    [Fact]
    public async Task CustomScan_ValidFile_PassesPathAsSingleArgvElement_3600s()
    {
        using var fx = ClamAvFixture.Create(FakeProcessRunner.Returning(ClamAvFixtures.CleanScan));

        var outcome = await fx.Service.CustomScanAsync(fx.ClamScanPath!);

        Assert.True(outcome.Succeeded);
        var request = Assert.Single(fx.Runner.Requests);
        Assert.Equal(3, request.Arguments.Count);
        Assert.Equal(fx.ClamScanPath, request.Arguments[2]);
        Assert.Equal(CustomScanTimeout, request.Timeout);
    }

    [Fact]
    public async Task CustomScan_ShellMetacharactersStayInASingleElement_NoShell()
    {
        using var fx = ClamAvFixture.Create(FakeProcessRunner.Returning(ClamAvFixtures.CleanScan));
        var target = Path.Combine(fx.TempDirectory, "informe & calc.exe");
        File.WriteAllText(target, string.Empty);

        await fx.Service.CustomScanAsync(target);

        var request = Assert.Single(fx.Runner.Requests);
        Assert.Equal(3, request.Arguments.Count);
        Assert.Equal(Path.GetFullPath(target), request.Arguments[2]); // "…& calc.exe" viaja entero
    }

    [Fact]
    public async Task CustomScan_EmptyPath_ThrowsClamAvFailed()
    {
        using var fx = ClamAvFixture.Create(FakeProcessRunner.Returning(""));

        var exception = await Assert.ThrowsAsync<ClamAvException>(
            () => fx.Service.CustomScanAsync(string.Empty));

        Assert.Equal(BridgeErrorCodes.ClamAvFailed, exception.ErrorCode);
        Assert.Equal("Indica una ruta absoluta para escanear.", exception.ErrorMessage);
        Assert.Empty(fx.Runner.Requests);
    }

    [Fact]
    public async Task CustomScan_NullByte_ThrowsClamAvFailed()
    {
        using var fx = ClamAvFixture.Create(FakeProcessRunner.Returning(""));

        var exception = await Assert.ThrowsAsync<ClamAvException>(
            () => fx.Service.CustomScanAsync("C:\\safe\\f" + '\0' + "ile"));

        Assert.Equal(BridgeErrorCodes.ClamAvFailed, exception.ErrorCode);
        Assert.Equal("La ruta no es válida.", exception.ErrorMessage);
    }

    [Fact]
    public async Task CustomScan_RelativePath_ThrowsClamAvFailed()
    {
        using var fx = ClamAvFixture.Create(FakeProcessRunner.Returning(""));

        var exception = await Assert.ThrowsAsync<ClamAvException>(
            () => fx.Service.CustomScanAsync(@"descargas\evil.exe"));

        Assert.Equal(BridgeErrorCodes.ClamAvFailed, exception.ErrorCode);
        Assert.StartsWith("La ruta debe ser absoluta", exception.ErrorMessage);
        Assert.Empty(fx.Runner.Requests);
    }

    [Fact]
    public async Task CustomScan_NonexistentPath_ThrowsClamAvFailed()
    {
        using var fx = ClamAvFixture.Create(FakeProcessRunner.Returning(""));

        var exception = await Assert.ThrowsAsync<ClamAvException>(
            () => fx.Service.CustomScanAsync(Path.Combine(fx.TempDirectory, "no-existe.bin")));

        Assert.Equal(BridgeErrorCodes.ClamAvFailed, exception.ErrorCode);
        Assert.Equal("La ruta indicada no existe.", exception.ErrorMessage);
    }

    // ------------------------------------------------------------------
    // Historial (solo resultados estructurados; lo más reciente primero)
    // ------------------------------------------------------------------

    [Fact]
    public async Task History_AfterScans_ReturnsNewestFirst_WithCounts()
    {
        using var fx = ClamAvFixture.Create(
            FakeProcessRunner.Returning(ClamAvFixtures.InfectedScan, exitCode: 1));

        await fx.Service.QuickScanAsync();
        await fx.Service.CustomScanAsync(fx.ClamScanPath!);

        var history = await fx.Service.GetHistoryAsync();

        Assert.Equal(2, history.Count);
        Assert.Equal(ClamAvScanKinds.Custom, history[0].Kind); // lo último, primero
        Assert.Equal(ClamAvScanKinds.Quick, history[1].Kind);
        Assert.Equal(fx.ClamScanPath, history[0].Path);
        Assert.Null(history[1].Path); // quick/full sin ruta concreta
        Assert.All(history, entry => Assert.True(entry.Succeeded));
        Assert.Equal(99, history[0].ScannedFiles);
        Assert.Equal(2, history[0].InfectedFiles);
        Assert.Equal("Win.Trojan.FakeMS-123", history[0].Threats[0].Threat);
    }

    [Fact]
    public async Task History_RecordsFailedScan_AsStructuredEntry()
    {
        using var fx = ClamAvFixture.Create(
            FakeProcessRunner.Returning(ClamAvFixtures.ErrorNoSummary, exitCode: 2));

        await fx.Service.QuickScanAsync();

        var entry = Assert.Single(fx.History.Read());
        Assert.False(entry.Succeeded);
        Assert.Equal(ClamAvFixtures.ErrorNoSummary, entry.Detail);
    }

    // ------------------------------------------------------------------
    // Puerta de serie: escaneos entre sí; status/history FUERA de ella
    // ------------------------------------------------------------------

    [Fact]
    public async Task Status_RespondsWhileAScanIsStillRunning()
    {
        var scanStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new FakeProcessRunner(async (request, _) =>
        {
            if (request.Arguments.Count == 1 && request.Arguments[0] == "--version")
            {
                return new ProcessResult { ExitCode = 0, StandardOutput = ClamAvFixtures.Version };
            }

            scanStarted.TrySetResult();
            await release.Task.ConfigureAwait(false);
            return new ProcessResult { ExitCode = 0, StandardOutput = ClamAvFixtures.CleanScan };
        });
        using var fx = ClamAvFixture.Create(runner);

        var scan = fx.Service.QuickScanAsync();
        await scanStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // status NO pasa por la puerta: responde mientras clamscan sigue corriendo
        var status = await fx.Service.GetStatusAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(status.Installed);

        release.SetResult();
        var outcome = await scan.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(outcome.Succeeded);
    }

    [Fact]
    public async Task ConcurrentScans_AreSerializedToASingleClamscan()
    {
        var counters = new int[2];
        var runner = new FakeProcessRunner(async (_, _) =>
        {
            // Mismo patrón que WslServiceTests: cuenta ejecuciones solapadas.
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
            return new ProcessResult { ExitCode = 0, StandardOutput = ClamAvFixtures.CleanScan };
        });
        using var fx = ClamAvFixture.Create(runner);

        await Task.WhenAll(fx.Service.QuickScanAsync(), fx.Service.FullScanAsync());

        Assert.Equal(2, fx.Runner.Requests.Count);
        Assert.Equal(1, counters[1]); // nunca dos clamscan solapados
    }
}
