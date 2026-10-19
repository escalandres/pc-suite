using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PCSuite.Application.Jobs;
using PCSuite.Contracts;
using PCSuite.Core.ClamAV;
using PCSuite.Core.Jobs;
using PCSuite.Core.Operations;
using PCSuite.Core.Processes;
using PCSuite.Infrastructure.ClamAV;
using PCSuite.Infrastructure.Developers;
using PCSuite.Infrastructure.Operations;
using PCSuite.Infrastructure.Processes;

namespace PCSuite.Application.Tests;

/// <summary>
/// job.get / job.cancel por el puente: snapshot camelCase con el resultado
/// del escaneo, 404 controlado para jobs desconocidos y cancelación real
/// de un escaneo en curso (el token del job mata el proceso).
/// </summary>
public sealed class JobOperationsTests
{
    private static CommandRouter Router(IJobManager jobs, IClamAvService service) =>
        new(
            new IOperation[]
            {
                new ClamAvQuickScanOperation(service, jobs),
                new JobGetOperation(jobs),
                new JobCancelOperation(jobs),
            },
            NullLogger<CommandRouter>.Instance);

    private static (CommandRouter Router, IJobManager Jobs, TempExecutablePath Temp) Create(FakeProcessRunner runner)
    {
        var temp = new TempExecutablePath();
        temp.Add("clamscan.exe"); // sin él el servicio responde NOT_INSTALLED
        var service = new ClamAvService(
            new ExecutableLocator(temp.DirectoryPath),
            runner,
            new FakeElevationManager(),
            new FakeWinGetService(),
            new ClamAvHistoryStore(Path.Combine(temp.DirectoryPath, "h.json")),
            installDirectories: Array.Empty<string>());
        var jobs = new JobManager(
            new BridgeJobEventPublisher(ClamAvFixture.NoOpEventPublisher.Instance));
        return (Router(jobs, service), jobs, temp);
    }

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

    private static async Task<BridgeResponse> Send(
        CommandRouter router, string id, string operation, string? payloadJson = null)
    {
        var payload = payloadJson is null ? string.Empty : $",\"payload\":{payloadJson}";
        return await router.RouteAsync(
            $"{{\"id\":\"{id}\",\"operation\":\"{operation}\"{payload}}}");
    }

    [Fact]
    public async Task JobGet_ReturnsSnapshot_CamelCase_WithResult()
    {
        // Los escaneos van elevados (2026-10-18): el resultado se parsea del
        // --log, así que el fake debe escribirlo (WritingScanLog).
        var (router, jobs, _) = Create(
            FakeProcessRunner.WritingScanLog(ClamAvFixtures.CleanScan));

        var start = await Send(router, "j1", OperationIds.ClamavQuickScan);
        var jobId = start.Result!.Value.GetProperty("jobId").GetString()!;
        await WaitForJobAsync(jobs, jobId);

        var response = await Send(router, "j2", OperationIds.JobGet, $$"""{"jobId":"{{jobId}}"}""");

        Assert.True(response.Success);
        var data = response.Result!.Value;
        Assert.Equal(jobId, data.GetProperty("id").GetString());
        Assert.Equal("clamav.quickScan", data.GetProperty("type").GetString());
        Assert.Equal("completed", data.GetProperty("status").GetString());
        Assert.Equal(0, data.GetProperty("exitCode").GetInt32());
        Assert.NotEqual(default, data.GetProperty("startedAt").GetDateTimeOffset());
        Assert.NotEqual(default, data.GetProperty("finishedAt").GetDateTimeOffset());

        // El resultado del escaneo viaja completo en el snapshot.
        var result = data.GetProperty("result");
        Assert.True(result.GetProperty("succeeded").GetBoolean());
        Assert.Equal(1234, result.GetProperty("scannedFiles").GetInt32());
    }

    [Fact]
    public async Task JobGet_UnknownJob_FailsControlled()
    {
        var (router, _, _) = Create(FakeProcessRunner.Returning(ClamAvFixtures.CleanScan));

        var response = await Send(router, "j3", OperationIds.JobGet, """{"jobId":"no-existe"}""");

        Assert.False(response.Success);
        Assert.Equal("not_found", response.Error!.Code);
        Assert.DoesNotContain("   at ", response.Error.Message);
    }

    [Fact]
    public async Task JobGet_MissingJobId_FailsControlled()
    {
        var (router, _, _) = Create(FakeProcessRunner.Returning(ClamAvFixtures.CleanScan));

        var response = await Send(router, "j4", OperationIds.JobGet, "{}");

        Assert.False(response.Success);
        Assert.Equal("error", response.Error!.Code);
        Assert.Contains("jobId", response.Error.Message);
    }

    [Fact]
    public async Task JobCancel_CancelsRunningScan()
    {
        // Escaneo "eterno" que respeta el token: al cancelar, el runner
        // lanza OCE y el job termina en estado cancelled.
        var (router, jobs, _) = Create(
            new FakeProcessRunner(
                async (_, ct) =>
                {
                    await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                    return new ProcessResult { ExitCode = 0, StandardOutput = ClamAvFixtures.CleanScan };
                }));

        var start = await Send(router, "j5", OperationIds.ClamavQuickScan);
        var jobId = start.Result!.Value.GetProperty("jobId").GetString()!;

        // El job está corriendo cuando se pide la cancelación.
        Assert.Equal(JobStatus.Running, jobs.Get(jobId)!.Status);

        var cancel = await Send(router, "j6", OperationIds.JobCancel, $$"""{"jobId":"{{jobId}}"}""");
        Assert.True(cancel.Success);
        Assert.True(cancel.Result!.Value.GetProperty("cancelled").GetBoolean());

        var job = await WaitForJobAsync(jobs, jobId);
        Assert.Equal(JobStatus.Cancelled, job.Status);

        // Re-cancelar un job terminal: false, no lanza.
        var again = await Send(router, "j7", OperationIds.JobCancel, $$"""{"jobId":"{{jobId}}"}""");
        Assert.True(again.Success);
        Assert.False(again.Result!.Value.GetProperty("cancelled").GetBoolean());
    }

    [Fact]
    public async Task JobCancel_UnknownJob_ReturnsFalse()
    {
        var (router, _, _) = Create(FakeProcessRunner.Returning(ClamAvFixtures.CleanScan));

        var response = await Send(router, "j8", OperationIds.JobCancel, """{"jobId":"no-existe"}""");

        Assert.True(response.Success);
        Assert.False(response.Result!.Value.GetProperty("cancelled").GetBoolean());
    }
}
