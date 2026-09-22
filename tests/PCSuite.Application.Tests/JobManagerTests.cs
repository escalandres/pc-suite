using Microsoft.Extensions.Logging.Abstractions;
using PCSuite.Application.Jobs;
using PCSuite.Core.Jobs;

namespace PCSuite.Application.Tests;

public sealed class JobManagerTests
{
    private static (JobManager Manager, List<JobEvent> Events) Create()
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

        return (new JobManager(publisher), events);
    }

    [Fact]
    public void Start_CreatesRunningJob_WithTokenAndEvent()
    {
        var (manager, events) = Create();

        var job = manager.Start("demo.task");

        Assert.Equal(JobStatus.Running, job.Status);
        Assert.Equal("demo.task", job.Type);
        Assert.NotNull(job.StartedAt);
        Assert.Null(job.FinishedAt);
        Assert.False(manager.GetCancellationToken(job.Id).IsCancellationRequested);

        var jobEvent = Assert.Single(events);
        Assert.Equal(JobEventType.StatusChanged, jobEvent.Type);
        Assert.Equal(JobStatus.Running, jobEvent.Status);
        Assert.Equal(job.Id, jobEvent.JobId);

        Assert.NotNull(manager.Get(job.Id));
    }

    [Fact]
    public void Start_WithInvalidType_Throws()
    {
        var (manager, _) = Create();

        Assert.Throws<ArgumentException>(() => manager.Start("   "));
    }

    [Fact]
    public void Complete_SetsStatusExitCodeAndTimestamp()
    {
        var (manager, events) = Create();
        var job = manager.Start("demo.task");

        manager.Complete(job.Id, 0);

        var snapshot = manager.Get(job.Id)!;
        Assert.Equal(JobStatus.Completed, snapshot.Status);
        Assert.Equal(0, snapshot.ExitCode);
        Assert.NotNull(snapshot.FinishedAt);
        Assert.Null(snapshot.Error);

        Assert.Equal(2, events.Count);
        Assert.Equal(JobStatus.Completed, events[^1].Status);
    }

    [Fact]
    public void Fail_SetsControlledErrorAndEvent()
    {
        var (manager, events) = Create();
        var job = manager.Start("demo.task");

        manager.Fail(job.Id, "Algo salió mal.", exitCode: 3);

        var snapshot = manager.Get(job.Id)!;
        Assert.Equal(JobStatus.Failed, snapshot.Status);
        Assert.Equal("Algo salió mal.", snapshot.Error);
        Assert.Equal(3, snapshot.ExitCode);

        var jobEvent = events[^1];
        Assert.Equal(JobEventType.StatusChanged, jobEvent.Type);
        Assert.Equal(JobStatus.Failed, jobEvent.Status);
        Assert.Equal("Algo salió mal.", jobEvent.Error);
    }

    [Fact]
    public void MarkCancelled_SetsStatus()
    {
        var (manager, events) = Create();
        var job = manager.Start("demo.task");

        manager.MarkCancelled(job.Id);

        Assert.Equal(JobStatus.Cancelled, manager.Get(job.Id)!.Status);
        Assert.Equal(JobStatus.Cancelled, events[^1].Status);
    }

    [Fact]
    public void TerminalJob_IgnoresFurtherTransitions()
    {
        var (manager, events) = Create();
        var job = manager.Start("demo.task");

        manager.Complete(job.Id, 0);
        manager.Fail(job.Id, "tarde demaisio", exitCode: 9);
        manager.MarkCancelled(job.Id);
        manager.SetProgress(job.Id, 0.5);

        var snapshot = manager.Get(job.Id)!;
        Assert.Equal(JobStatus.Completed, snapshot.Status);
        Assert.Equal(0, snapshot.ExitCode);
        Assert.Null(snapshot.Error);
        Assert.Null(snapshot.Progress);
        Assert.Equal(2, events.Count); // Start + Complete
    }

    [Fact]
    public void SetProgress_ClampsRange_AndPublishesEvent()
    {
        var (manager, events) = Create();
        var job = manager.Start("demo.task");

        manager.SetProgress(job.Id, 2.5);
        Assert.Equal(1, manager.Get(job.Id)!.Progress);

        manager.SetProgress(job.Id, -0.5);
        Assert.Equal(0, manager.Get(job.Id)!.Progress);

        var progressEvents = events.Where(e => e.Type == JobEventType.Progress).ToList();
        Assert.Equal(2, progressEvents.Count);
        Assert.Equal(1, progressEvents[0].Progress);
        Assert.Equal(0, progressEvents[1].Progress);
    }

    [Fact]
    public void Get_ReturnsIsolatedSnapshots()
    {
        var (manager, _) = Create();
        var job = manager.Start("demo.task");

        var first = manager.Get(job.Id)!;
        manager.Complete(job.Id, 0);

        // La copia tomada antes no cambia…
        Assert.Equal(JobStatus.Running, first.Status);

        // …y mutar una copia no toca el estado interno.
        var second = manager.Get(job.Id)!;
        second.Error = "adulterado";
        second.Status = JobStatus.Running;
        var third = manager.Get(job.Id)!;
        Assert.Equal(JobStatus.Completed, third.Status);
        Assert.Null(third.Error);
    }

    [Fact]
    public void Get_UnknownJob_ReturnsNull()
    {
        var (manager, _) = Create();

        Assert.Null(manager.Get("no-existe"));
        Assert.Equal(CancellationToken.None, manager.GetCancellationToken("no-existe"));
    }

    [Fact]
    public void RequestCancel_TriggersJobToken_OnlyForLiveJobs()
    {
        var (manager, _) = Create();
        var job = manager.Start("demo.task");

        Assert.True(manager.RequestCancel(job.Id));
        Assert.True(manager.GetCancellationToken(job.Id).IsCancellationRequested);
        Assert.False(manager.RequestCancel("no-existe"));

        // La cancelación solo dispara el token: el job sigue vivo hasta que
        // el motor cierre su estado.
        Assert.Equal(JobStatus.Running, manager.Get(job.Id)!.Status);
        manager.Complete(job.Id, 0);
        Assert.False(manager.RequestCancel(job.Id));
    }

    [Fact]
    public void GetHistory_ReturnsNewestFirst_AndRespectsLimit()
    {
        var (manager, _) = Create();
        var first = manager.Start("a");
        var second = manager.Start("b");
        var third = manager.Start("c");

        var history = manager.GetHistory();

        Assert.Equal(new[] { third.Id, second.Id, first.Id }, history.Select(j => j.Id));
        Assert.Equal(new[] { third.Id, second.Id }, manager.GetHistory(2).Select(j => j.Id));
        Assert.Empty(manager.GetHistory(0));
    }

    [Fact]
    public void Start_EvictsOldestJobs_WhenHistoryExceedsLimit()
    {
        var (manager, _) = Create();
        Job? oldest = null;

        for (var i = 0; i < JobManager.MaxTrackedJobs + 1; i++)
        {
            var job = manager.Start($"demo.{i}");
            oldest ??= job;
        }

        Assert.Null(manager.Get(oldest!.Id));
        Assert.Equal(JobManager.MaxTrackedJobs, manager.GetHistory(limit: int.MaxValue).Count);
    }
}
