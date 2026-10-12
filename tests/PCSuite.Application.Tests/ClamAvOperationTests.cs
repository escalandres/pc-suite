using System.Text.Json;
using PCSuite.Contracts;
using PCSuite.Core.Jobs;
using PCSuite.Core.Processes;
using PCSuite.Core.WinGet;
using PCSuite.Infrastructure.Operations;

namespace PCSuite.Application.Tests;

/// <summary>
/// Las operaciones clamav.* + job.* por el puente (Task 008): contrato
/// camelCase, payload estructurado SOLO en customScan (malformado →
/// MALFORMED_MESSAGE), fallos controlados (nunca INTERNAL_ERROR ni stack
/// traces) y metadato RequiresElevation solo en install/updateDefinitions.
///
/// Los escaneos son JOBS: responden {"jobId"} al instante y el desenlace
/// (resultado completo, error controlado o cancelación) vive en el job —
/// consultable con job.get y cancelable con job.cancel.
/// </summary>
public sealed class ClamAvOperationTests
{
    // ------------------------------------------------------------------
    // Contrato camelCase
    // ------------------------------------------------------------------

    [Fact]
    public async Task Status_ReturnsTypedResult_CamelCase()
    {
        using var fx = ClamAvFixture.Create(FakeProcessRunner.Returning(ClamAvFixtures.Version));

        var response = await Send(fx.Router, "s1", OperationIds.ClamavStatus);

        Assert.True(response.Success);
        var data = response.Result!.Value;
        Assert.True(data.GetProperty("installed").GetBoolean());
        Assert.Equal(fx.ClamScanPath, data.GetProperty("path").GetString());
        Assert.Equal("1.5.4", data.GetProperty("version").GetString());
        Assert.Equal("7123456", data.GetProperty("definitions").GetString());
        Assert.True(data.GetProperty("canUpdateDefinitions").GetBoolean());
    }

    [Fact]
    public async Task QuickScan_StartsJob_RespondsJobIdAtOnce_ResultCarriesOutcome()
    {
        using var fx = ClamAvFixture.Create(
            FakeProcessRunner.Returning(ClamAvFixtures.InfectedScan, exitCode: 1));

        var response = await Send(fx.Router, "q1", OperationIds.ClamavQuickScan);

        // Contrato nuevo: {"jobId"} EN EL ACTO; el desenlace va en el job.
        Assert.True(response.Success);
        var jobId = response.Result!.Value.GetProperty("jobId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(jobId));

        var job = await WaitForJobAsync(fx.Jobs, jobId!);
        Assert.Equal(JobStatus.Completed, job.Status);
        Assert.Equal(1, job.ExitCode);

        var data = job.Result!.Value;
        Assert.True(data.GetProperty("succeeded").GetBoolean());
        Assert.Equal(1, data.GetProperty("exitCode").GetInt32());
        Assert.Equal(99, data.GetProperty("scannedFiles").GetInt32());
        Assert.Equal(5, data.GetProperty("scannedDirectories").GetInt32());
        Assert.Equal(2, data.GetProperty("infectedFiles").GetInt32());
        Assert.Equal("1.5.4", data.GetProperty("engineVersion").GetString());
        Assert.Equal(7123456, data.GetProperty("knownViruses").GetInt32());
        Assert.False(data.TryGetProperty("detail", out _)); // exit 1 = hallazgo, sin detalle

        var threats = data.GetProperty("threats").EnumerateArray().ToArray();
        Assert.Equal(2, threats.Length);
        Assert.Equal(@"C:\Users\test\Downloads\evil.com", threats[0].GetProperty("file").GetString());
        Assert.Equal("Win.Trojan.FakeMS-123", threats[0].GetProperty("threat").GetString());
    }

    [Fact]
    public async Task UpdateDefinitions_ReturnsMutationResult_CamelCase()
    {
        using var fx = ClamAvFixture.Create(FakeProcessRunner.Returning(ClamAvFixtures.FreshclamOk));

        var response = await Send(fx.Router, "u1", OperationIds.ClamavUpdateDefinitions);

        Assert.True(response.Success);
        var data = response.Result!.Value;
        Assert.True(data.GetProperty("succeeded").GetBoolean());
        Assert.Equal(0, data.GetProperty("exitCode").GetInt32());
        Assert.False(data.GetProperty("elevated").GetBoolean());
        Assert.False(data.TryGetProperty("detail", out _));
        Assert.Empty(fx.Elevation.Requests); // sin indicios de permisos → sin UAC
    }

    [Fact]
    public async Task Install_DelegatesToWinget_ReturnsMutationResult()
    {
        using var fx = ClamAvFixture.Create(FakeProcessRunner.Returning(""), withClamAv: false);
        fx.Winget.InstallOutcome = new WinGetMutationOutcome(
            "Cisco.ClamAV", Succeeded: true, ExitCode: 0, Elevated: false, Detail: "Se instaló Cisco.ClamAV");

        var response = await Send(fx.Router, "i1", OperationIds.ClamavInstall);

        Assert.True(response.Success);
        Assert.Equal(["Cisco.ClamAV"], fx.Winget.InstallCalls);
        var data = response.Result!.Value;
        Assert.True(data.GetProperty("succeeded").GetBoolean());
        Assert.Equal(0, data.GetProperty("exitCode").GetInt32());
        Assert.False(data.GetProperty("elevated").GetBoolean());
        Assert.Equal("Se instaló Cisco.ClamAV", data.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task History_ReturnsEntries_NewestFirst_CamelCase()
    {
        using var fx = ClamAvFixture.Create(FakeProcessRunner.Returning(ClamAvFixtures.CleanScan));
        await fx.Service.QuickScanAsync();

        var response = await Send(fx.Router, "h1", OperationIds.ClamavHistory);

        Assert.True(response.Success);
        var entry = Assert.Single(response.Result!.Value.GetProperty("entries").EnumerateArray());
        Assert.Equal("quick", entry.GetProperty("kind").GetString());
        Assert.True(entry.GetProperty("succeeded").GetBoolean());
        Assert.Equal(1234, entry.GetProperty("scannedFiles").GetInt32());
        Assert.Equal(0, entry.GetProperty("infectedFiles").GetInt32());
        Assert.False(entry.TryGetProperty("path", out _)); // quick/full sin ruta
        Assert.NotEqual(default, entry.GetProperty("timestamp").GetDateTimeOffset());
    }

    // ------------------------------------------------------------------
    // Payload estructurado SOLO en customScan
    // ------------------------------------------------------------------

    [Fact]
    public async Task CustomScan_WithValidPayload_ScansThatPath()
    {
        using var fx = ClamAvFixture.Create(FakeProcessRunner.Returning(ClamAvFixtures.CleanScan));
        var payloadJson = JsonSerializer.Serialize(new { path = fx.ClamScanPath });

        var response = await Send(fx.Router, "c1", OperationIds.ClamavCustomScan, payloadJson);

        Assert.True(response.Success);
        var jobId = response.Result!.Value.GetProperty("jobId").GetString()!;
        await WaitForJobAsync(fx.Jobs, jobId);

        // El argv fijo viaja como lista: la ruta validada es el único dato
        // que la UI aporta y va SIEMPRE como elemento final.
        var request = Assert.Single(fx.Runner.Requests);
        Assert.Equal("--recursive", request.Arguments[0]);
        Assert.Equal("--verbose", request.Arguments[1]);
        Assert.Equal(fx.ClamScanPath, request.Arguments[2]);
        Assert.Equal(3, request.Arguments.Count);
    }

    [Fact]
    public async Task CustomScan_NonObjectPayload_SurfacesMalformedMessage()
    {
        using var fx = ClamAvFixture.Create(FakeProcessRunner.Returning(""));

        var response = await Send(fx.Router, "c2", OperationIds.ClamavCustomScan, "\"no soy objeto\"");

        Assert.False(response.Success);
        Assert.Equal(BridgeErrorCodes.MalformedMessage, response.Error!.Code);
        Assert.Equal("El payload debe ser un objeto JSON.", response.Error.Message);
        Assert.Empty(fx.Runner.Requests); // ni se intenta lanzar clamscan
    }

    [Fact]
    public async Task CustomScan_WithoutPayload_SurfacesClamAvFailedWithHint()
    {
        using var fx = ClamAvFixture.Create(FakeProcessRunner.Returning(""));

        var response = await Send(fx.Router, "c3", OperationIds.ClamavCustomScan);

        Assert.False(response.Success);
        Assert.Equal(BridgeErrorCodes.ClamAvFailed, response.Error!.Code);
        Assert.Equal("Indica una ruta absoluta para escanear.", response.Error.Message);
        Assert.DoesNotContain("   at ", response.Error.Message);
    }

    [Fact]
    public async Task CustomScan_RelativePath_SurfacesClamAvFailed()
    {
        using var fx = ClamAvFixture.Create(FakeProcessRunner.Returning(""));

        var response = await Send(
            fx.Router, "c4", OperationIds.ClamavCustomScan, """{"path":"descargas\\evil.exe"}""");

        Assert.False(response.Success);
        Assert.Equal(BridgeErrorCodes.ClamAvFailed, response.Error!.Code);
        Assert.StartsWith("La ruta debe ser absoluta", response.Error.Message);
    }

    // ------------------------------------------------------------------
    // Errores controlados (nunca INTERNAL_ERROR para fallos de dominio)
    // ------------------------------------------------------------------

    [Fact]
    public async Task MissingClamAv_JobFailsWithControlledMessage()
    {
        using var fx = ClamAvFixture.Create(FakeProcessRunner.Returning(""), withClamAv: false);

        var response = await Send(fx.Router, "f1", OperationIds.ClamavQuickScan);

        // El arranque responde con jobId (el error ocurre DENTRO del job);
        // el desenlace es el mensaje controlado del servicio, sin stack.
        Assert.True(response.Success);
        var jobId = response.Result!.Value.GetProperty("jobId").GetString()!;
        var job = await WaitForJobAsync(fx.Jobs, jobId);

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal("ClamAV no está instalado. Usa clamav.install para instalarlo.", job.Error);
        Assert.DoesNotContain("   at ", job.Error);
        Assert.Empty(fx.Runner.Requests);
    }

    [Fact]
    public async Task Timeout_SurfacesClamAvTimeout_NotInternalError()
    {
        using var fx = ClamAvFixture.Create(
            FakeProcessRunner.Throwing(new ProcessTimeoutException(TimeSpan.FromSeconds(10))));

        var response = await Send(fx.Router, "f2", OperationIds.ClamavUpdateDefinitions);

        Assert.False(response.Success);
        Assert.Equal(BridgeErrorCodes.ClamAvTimeout, response.Error!.Code);
        Assert.DoesNotContain("   at ", response.Error.Message);
    }

    // ------------------------------------------------------------------
    // §12/§53: payloads hostiles jamás llegan a los argv
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(OperationIds.ClamavStatus, """{"path":"C:\\","command":"--bell"}""")]
    [InlineData(OperationIds.ClamavQuickScan, "\"clamscan --bell calc\"")]
    [InlineData(OperationIds.ClamavUpdateDefinitions, """{"args":["--arg","x"]}""")]
    [InlineData(OperationIds.ClamavHistory, "\"--shutdown\"")]
    public async Task HostilePayload_IsIgnored_FixedArgvStillUsed(string operation, string payloadJson)
    {
        using var fx = ClamAvFixture.Create(FakeProcessRunner.Returning(ClamAvFixtures.FreshclamOk));

        var response = await Send(fx.Router, "h9", operation, payloadJson);

        Assert.True(response.Success);
        if (operation == OperationIds.ClamavHistory)
        {
            Assert.Empty(fx.Runner.Requests); // solo lee el historial: ningún proceso
            return;
        }

        // Los escaneos son jobs: el proceso corre en segundo plano, se espera
        // al desenlace antes de inspeccionar el argv efectivo.
        if (operation == OperationIds.ClamavQuickScan)
        {
            var jobId = response.Result!.Value.GetProperty("jobId").GetString()!;
            await WaitForJobAsync(fx.Jobs, jobId);
        }

        var request = Assert.Single(fx.Runner.Requests);
        switch (operation)
        {
            case OperationIds.ClamavStatus:
                Assert.Equal<string>(["--version"], request.Arguments);
                break;
            case OperationIds.ClamavUpdateDefinitions:
                Assert.Empty(request.Arguments); // freshclam: argv vacío fijo
                break;
            default:
                if (operation == OperationIds.ClamavFullScan)
                {
                    Assert.Equal("--recursive", request.Arguments[0]);
                    Assert.DoesNotContain("--verbose", request.Arguments);
                }
                else
                {
                    Assert.Equal("--recursive", request.Arguments[0]);
                    Assert.Equal("--verbose", request.Arguments[1]);
                }
                break;
        }
    }

    // ------------------------------------------------------------------
    // Metadato de elevación (decisión real en runtime, nunca preventiva)
    // ------------------------------------------------------------------

    [Fact]
    public void OnlyInstallAndUpdateDeclareElevation()
    {
        using var fx = ClamAvFixture.Create(FakeProcessRunner.Returning(""));

        var operations = new ClamAvOperation[]
        {
            new ClamAvStatusOperation(fx.Service),
            new ClamAvInstallOperation(fx.Service),
            new ClamAvUpdateDefinitionsOperation(fx.Service),
            new ClamAvQuickScanOperation(fx.Service, fx.Jobs),
            new ClamAvFullScanOperation(fx.Service, fx.Jobs),
            new ClamAvCustomScanOperation(fx.Service, fx.Jobs),
            new ClamAvHistoryOperation(fx.Service),
        };

        Assert.True(operations.Single(op => op.Id == OperationIds.ClamavInstall).RequiresElevation);
        Assert.True(operations.Single(op => op.Id == OperationIds.ClamavUpdateDefinitions).RequiresElevation);
        Assert.All(
            operations.Where(op =>
                op.Id != OperationIds.ClamavInstall && op.Id != OperationIds.ClamavUpdateDefinitions),
            op => Assert.False(op.RequiresElevation)); // los escaneos jamás elevan
    }

    private static Task<BridgeResponse> Send(
        CommandRouter router,
        string id,
        string operation,
        string? payloadJson = null)
    {
        var payload = payloadJson is null ? string.Empty : $",\"payload\":{payloadJson}";
        return router.RouteAsync(
            $"{{\"id\":\"{id}\",\"operation\":\"{operation}\"{payload}}}");
    }

    /// <summary>Espera (acotado) a que un job llegue a estado terminal.</summary>
    private static async Task<Job> WaitForJobAsync(IJobManager jobs, string jobId)
    {
        for (var i = 0; i < 200; i++)
        {
            if (jobs.Get(jobId) is { IsFinished: true } job)
            {
                return job;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException($"El job '{jobId}' no terminó en el plazo de prueba.");
    }
}
