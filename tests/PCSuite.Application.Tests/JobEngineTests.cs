using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using PCSuite.Application.Jobs;
using PCSuite.Core.Jobs;
using PCSuite.Core.Processes;
using PCSuite.Infrastructure.Processes;

namespace PCSuite.Application.Tests;

/// <summary>
/// Pruebas de extremo a extremo del motor: JobEngine + JobManager +
/// ProcessRunner reales, ejecutando el proceso mock.
/// </summary>
public sealed class JobEngineTests
{
    private static (JobEngine Engine, JobManager Jobs, List<JobEvent> Events) Create()
    {
        var publisher = new JobEventPublisher(NullLogger<JobEventPublisher>.Instance);
        var events = new List<JobEvent>();
        publisher.Published += (_, jobEvent) =>
        {
            lock (events)
            {
                events.Add(jobEvent);
            }
        };

        var jobs = new JobManager(publisher);
        return (new JobEngine(new ProcessRunner(), jobs, publisher), jobs, events);
    }

    [Fact]
    public async Task RunAsync_ExitZero_CompletesJob_AndPublishesStreamedEventsInOrder()
    {
        var (engine, jobs, events) = Create();

        var job = await engine.RunAsync("mock.all", MockProcess.Request("all", "0"));

        Assert.Equal(JobStatus.Completed, job.Status);
        Assert.Equal(0, job.ExitCode);
        Assert.NotNull(job.StartedAt);
        Assert.NotNull(job.FinishedAt);
        Assert.Null(job.Error);

        // Orden: Running → líneas de salida (stdout y stderr) → Completed.
        lock (events)
        {
            Assert.Equal(JobStatus.Running, events[0].Status);
            Assert.Equal(JobStatus.Completed, events[^1].Status);

            var firstOutput = events.FindIndex(e => e.Type == JobEventType.Output);
            Assert.True(firstOutput > 0, "No se recibieron eventos de salida.");

            Assert.Contains(events, e => e is { Type: JobEventType.Output, Stream: JobLogStream.StandardOutput, Line: "stdout-line-1" });
            Assert.Contains(events, e => e is { Type: JobEventType.Output, Stream: JobLogStream.StandardError, Line: "stderr-line-1" });
            Assert.All(events.Where(e => e.Type == JobEventType.Output), e => Assert.Equal(job.Id, e.JobId));
        }

        Assert.Contains(jobs.GetHistory(), j => j.Id == job.Id);
    }

    [Fact]
    public async Task RunAsync_NonZeroExit_FailsJobWithExitCode()
    {
        var (engine, _, events) = Create();

        var job = await engine.RunAsync("mock.all", MockProcess.Request("all", "3"));

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal(3, job.ExitCode);
        Assert.Contains("3", job.Error);

        lock (events)
        {
            Assert.Equal(JobStatus.Failed, events[^1].Status);
        }
    }

    [Fact]
    public async Task RunAsync_Timeout_FailsJobPromptlyWithControlledMessage()
    {
        var (engine, _, _) = Create();
        var request = MockProcess.Request("sleep", "10000") with
        {
            Timeout = TimeSpan.FromMilliseconds(500),
        };

        var stopwatch = Stopwatch.StartNew();
        var job = await engine.RunAsync("mock.sleep", request);
        stopwatch.Stop();

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Contains("tiempo límite", job.Error);
        Assert.DoesNotContain("   at ", job.Error);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"El timeout tardó {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task RunAsync_JobCancellation_ViaManager_ReturnsCancelledJob()
    {
        var (engine, jobs, _) = Create();

        // Start es síncrono antes del primer await: el job ya existe al volver.
        var execution = engine.RunAsync("mock.sleep", MockProcess.Request("sleep", "10000"));
        var job = Assert.Single(jobs.GetHistory());

        Assert.True(jobs.RequestCancel(job.Id));

        var finished = await execution;

        Assert.Equal(job.Id, finished.Id);
        Assert.Equal(JobStatus.Cancelled, finished.Status);
        Assert.NotNull(finished.FinishedAt);
    }

    [Fact]
    public async Task RunAsync_CallerCancellation_ThrowsAndMarksJobCancelled()
    {
        var (engine, jobs, _) = Create();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => engine.RunAsync("mock.sleep", MockProcess.Request("sleep", "10000"), cts.Token));

        var job = Assert.Single(jobs.GetHistory());
        Assert.Equal(JobStatus.Cancelled, job.Status);
        Assert.NotNull(job.FinishedAt);
    }

    [Fact]
    public async Task RunAsync_MissingExecutable_FailsJobWithControlledMessage_NoStackTraces()
    {
        var (engine, _, _) = Create();
        var request = new ProcessRequest { FileName = @"C:\no\existe\pcsuite-mock.exe" };

        var job = await engine.RunAsync("mock.broken", request);

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.False(string.IsNullOrWhiteSpace(job.Error));
        Assert.DoesNotContain("   at ", job.Error);
    }

    [Fact]
    public async Task RunAsync_RequiresElevation_FailsJobWithTask005Message()
    {
        var (engine, _, _) = Create();
        var request = new ProcessRequest { FileName = "pcsuite-mock", RequiresElevation = true };

        var job = await engine.RunAsync("mock.elevated", request);

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Contains("Task 005", job.Error);
    }
}
