using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PCSuite.Application;
using PCSuite.Application.Jobs;
using PCSuite.Contracts;
using PCSuite.Core.Operations;
using PCSuite.Core.Processes;
using PCSuite.Core.Windows;
using PCSuite.Infrastructure.Operations;
using PCSuite.Infrastructure.Windows;

namespace PCSuite.Application.Tests;

/// <summary>
/// Las 2 operaciones windows.* por el puente (Task 036): el catálogo llega
/// camelCase, runCommand responde { jobId } en el acto y el desenlace
/// completa el job con el resultado estructurado (sondeado vía job.get,
/// igual que la UI). Id desconocido y payload no-objeto fallan controlados
/// SIN job residual ni UAC.
/// </summary>
public sealed class WindowsOperationTests
{
    private readonly FakeProcessRunner _runner = FakeProcessRunner.Returning(string.Empty, exitCode: 0);
    private readonly CommandRouter _router;

    public WindowsOperationTests()
    {
        var service = new WindowsRepairService(_runner);
        var jobs = new JobManager(new BridgeJobEventPublisher(ClamAvFixture.NoOpEventPublisher.Instance));

        _router = new CommandRouter(
            new IOperation[]
            {
                new WindowsListCommandsOperation(service),
                new WindowsRunCommandOperation(service, jobs),
                new JobGetOperation(jobs),
                new JobCancelOperation(jobs),
            },
            NullLogger<CommandRouter>.Instance);
    }

    // ------------------------------------------------------------------

    [Fact]
    public async Task ListCommands_ReturnsTheCatalogInCamelCase()
    {
        var response = await Send(_router, "l1", OperationIds.WindowsListCommands);

        Assert.True(response.Success);
        var commands = response.Result!.Value.GetProperty("commands");
        Assert.Equal(7, commands.GetArrayLength());

        var sfc = commands[0];
        Assert.Equal(WindowsCommandCatalog.SfcScannow, sfc.GetProperty("id").GetString());
        Assert.Equal("sfc /scannow", sfc.GetProperty("displayCommand").GetString());
        Assert.Equal("10–30 min", sfc.GetProperty("durationHint").GetString());
        Assert.True(sfc.GetProperty("heavy").GetBoolean());
        Assert.EndsWith("CBS.log", sfc.GetProperty("logPath").GetString());
    }

    [Fact]
    public async Task RunCommand_RespondsWithJobIdAndCompletesWithTheOutcome()
    {
        var start = await Send(
            _router,
            "r1",
            OperationIds.WindowsRunCommand,
            JsonSerializer.Serialize(new { commandId = WindowsCommandCatalog.SfcScannow }));

        Assert.True(start.Success);
        var jobId = start.Result!.Value.GetProperty("jobId").GetString();
        Assert.NotNull(jobId);

        var job = await WaitForJobAsync(_router, jobId!);
        Assert.NotNull(job);
        Assert.Equal("completed", job.Value.GetProperty("status").GetString());

        // El argv del catálogo fue lo que salió del proceso .NET — jamás
        // algo que venga del payload. (Assert DESPUÉS de esperar el job:
        // el runner corre en el fire-and-forget del op.)
        var request = Assert.Single(_runner.Requests);
        Assert.Equal(new[] { "/scannow" }, request.Arguments);

        var result = job.Value.GetProperty("result");
        Assert.True(result.GetProperty("succeeded").GetBoolean());
        Assert.Equal(0, result.GetProperty("exitCode").GetInt32());
        Assert.Contains("no encontró violaciones", result.GetProperty("message").GetString());
        Assert.Contains("CBS.log", result.GetProperty("logHint").GetString());
    }

    [Fact]
    public async Task RunCommand_UnknownId_FailsControlledWithoutJob()
    {
        var response = await Send(
            _router,
            "r2",
            OperationIds.WindowsRunCommand,
            JsonSerializer.Serialize(new { commandId = "rm -rf /" }));

        Assert.False(response.Success);
        Assert.Equal(BridgeErrorCodes.WindowsFailed, response.Error!.Code);
        Assert.Empty(_runner.Requests); // cero UAC, cero proceso
    }

    [Fact]
    public async Task RunCommand_NonObjectPayload_IsMalformedMessage()
    {
        var response = await Send(
            _router,
            "r3",
            OperationIds.WindowsRunCommand,
            "\"sfc-scannow\"");

        Assert.False(response.Success);
        Assert.Equal(BridgeErrorCodes.MalformedMessage, response.Error!.Code);
    }

    [Fact]
    public async Task RunCommand_ServiceFailure_CompletesTheJobAsFailed()
    {
        var failingRunner = FakeProcessRunner.Throwing(new ElevationDeclinedException());
        var service = new WindowsRepairService(failingRunner);
        var jobs = new JobManager(new BridgeJobEventPublisher(ClamAvFixture.NoOpEventPublisher.Instance));
        var router = new CommandRouter(
            new IOperation[] { new WindowsRunCommandOperation(service, jobs), new JobGetOperation(jobs) },
            NullLogger<CommandRouter>.Instance);

        var start = await Send(
            router,
            "r4",
            OperationIds.WindowsRunCommand,
            JsonSerializer.Serialize(new { commandId = WindowsCommandCatalog.DismCheckHealth }));
        var jobId = start.Result!.Value.GetProperty("jobId").GetString();

        var job = await WaitForJobAsync(router, jobId!);
        Assert.NotNull(job);
        Assert.Equal("failed", job.Value.GetProperty("status").GetString());
        Assert.Contains("canceló el control de cuentas", job.Value.GetProperty("error").GetString());
    }

    // ------------------------------------------------------------------

    private static async Task<BridgeResponse> Send(
        CommandRouter router,
        string id,
        string operation,
        string? payloadJson = null)
    {
        var payload = payloadJson is null ? string.Empty : $",\"payload\":{payloadJson}";
        return await router.RouteAsync(
            $"{{\"id\":\"{id}\",\"operation\":\"{operation}\"{payload}}}");
    }

    /// <summary>Sondea job.get (como la UI) hasta que el job deje de correr.</summary>
    private static async Task<JsonElement?> WaitForJobAsync(CommandRouter router, string jobId)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var response = await router.RouteAsync(
                $"{{\"id\":\"w{attempt}\",\"operation\":\"job.get\",\"payload\":{{\"jobId\":\"{jobId}\"}}}}");
            if (response.Success)
            {
                var status = response.Result!.Value.GetProperty("status").GetString();
                if (status != "running")
                {
                    return response.Result;
                }
            }

            await Task.Delay(20);
        }

        return null;
    }
}
