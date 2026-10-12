using System.Text.Json;
using PCSuite.Core.Jobs;

namespace PCSuite.Application.Jobs;

/// <summary>
/// Registro de jobs en memoria con historial acotado. Toda la mutación ocurre
/// bajo un único candado; los eventos se publican fuera del candado para que
/// un suscriptor pueda reentrar sin riesgo de deadlock. Las respuestas son
/// snapshots aislados: el estado interno jamás se filtra al exterior.
/// </summary>
public sealed class JobManager : IJobManager
{
    /// <summary>Tope del historial: al superarlo se descartan los jobs más antiguos.</summary>
    public const int MaxTrackedJobs = 200;

    private readonly object _sync = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly List<string> _order = [];
    private readonly IJobEventPublisher _events;

    public JobManager(IJobEventPublisher events)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
    }

    public Job Start(string type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            throw new ArgumentException("El tipo de job no puede estar vacío.", nameof(type));
        }

        var job = new Job
        {
            Id = Guid.NewGuid().ToString("D"),
            Type = type,
            Status = JobStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
        };

        lock (_sync)
        {
            EvictIfNeeded();
            _entries[job.Id] = new Entry(job, new CancellationTokenSource());
            _order.Add(job.Id);
        }

        _events.Publish(new JobEvent { JobId = job.Id, Type = JobEventType.StatusChanged, Status = JobStatus.Running });
        return Snapshot(job);
    }

    public void Complete(string jobId, int? exitCode = null, JsonElement? result = null)
        => Finish(jobId, JobStatus.Completed, exitCode, error: null, result);

    public void Fail(string jobId, string error, int? exitCode = null)
        => Finish(jobId, JobStatus.Failed, exitCode, error, result: null);

    public void MarkCancelled(string jobId, int? exitCode = null)
        => Finish(jobId, JobStatus.Cancelled, exitCode, error: null, result: null);

    public void SetProgress(string jobId, double progress)
    {
        var clamped = Math.Clamp(progress, 0, 1);

        JobEvent? jobEvent;
        lock (_sync)
        {
            if (!_entries.TryGetValue(jobId, out var entry) || entry.Job.IsFinished)
            {
                return;
            }

            entry.Job.Progress = clamped;
            jobEvent = new JobEvent { JobId = jobId, Type = JobEventType.Progress, Progress = clamped, Status = entry.Job.Status };
        }

        _events.Publish(jobEvent);
    }

    public bool RequestCancel(string jobId)
    {
        CancellationTokenSource cancellation;
        lock (_sync)
        {
            if (!_entries.TryGetValue(jobId, out var entry) || entry.Job.IsFinished)
            {
                return false;
            }

            cancellation = entry.Cancellation;
        }

        try
        {
            cancellation.Cancel();
        }
        catch (AggregateException)
        {
            // Un callback de cancelación que lance no debe tumbar al llamador.
            return false;
        }

        return true;
    }

    public CancellationToken GetCancellationToken(string jobId)
    {
        lock (_sync)
        {
            return _entries.TryGetValue(jobId, out var entry)
                ? entry.Cancellation.Token
                : CancellationToken.None;
        }
    }

    public Job? Get(string jobId)
    {
        lock (_sync)
        {
            return _entries.TryGetValue(jobId, out var entry) ? Snapshot(entry.Job) : null;
        }
    }

    public IReadOnlyList<Job> GetHistory(int limit = 100)
    {
        if (limit <= 0)
        {
            return [];
        }

        lock (_sync)
        {
            var result = new List<Job>(Math.Min(limit, _order.Count));
            for (var i = _order.Count - 1; i >= 0 && result.Count < limit; i--)
            {
                if (_entries.TryGetValue(_order[i], out var entry))
                {
                    result.Add(Snapshot(entry.Job));
                }
            }

            return result;
        }
    }

    private void Finish(string jobId, JobStatus status, int? exitCode, string? error, JsonElement? result = null)
    {
        JobEvent jobEvent;
        lock (_sync)
        {
            if (!_entries.TryGetValue(jobId, out var entry) || entry.Job.IsFinished)
            {
                // Desconocido o ya terminal: las transiciones son idempotentes.
                return;
            }

            entry.Job.Status = status;
            entry.Job.FinishedAt = DateTimeOffset.UtcNow;
            entry.Job.ExitCode = exitCode;
            entry.Job.Error = error;
            entry.Job.Result = result;
            jobEvent = new JobEvent
            {
                JobId = jobId,
                Type = JobEventType.StatusChanged,
                Status = status,
                Error = error,
                Result = result,
            };
        }

        _events.Publish(jobEvent);
    }

    // El llamador debe tener el candado.
    private void EvictIfNeeded()
    {
        while (_entries.Count >= MaxTrackedJobs && _order.Count > 0)
        {
            var oldest = _order[0];
            _order.RemoveAt(0);
            if (_entries.Remove(oldest, out var entry))
            {
                entry.Cancellation.Dispose();
            }
        }
    }

    private static Job Snapshot(Job job) => new()
    {
        Id = job.Id,
        Type = job.Type,
        Status = job.Status,
        StartedAt = job.StartedAt,
        FinishedAt = job.FinishedAt,
        ExitCode = job.ExitCode,
        Progress = job.Progress,
        Error = job.Error,
        Result = job.Result,
    };

    private sealed record Entry(Job Job, CancellationTokenSource Cancellation);
}
