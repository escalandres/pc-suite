using System.Text.Json;
using PCSuite.Contracts;
using PCSuite.Core.Events;
using PCSuite.Core.Jobs;
using PCSuite.Core.Operations;

namespace PCSuite.Infrastructure.Operations;

// Ciclo de vida de jobs sobre el puente: la UI consulta un snapshot
// (reattach tras recarga) y cancela el job en curso. El mapeo enum → string
// vive aquí porque BridgeJson no usa convertidores de enum.

/// <summary>job.get — snapshot de un job por id (payload: {"jobId"}).</summary>
public sealed class JobGetOperation : IOperation
{
    private readonly IJobManager _jobs;

    public JobGetOperation(IJobManager jobs) => _jobs = jobs;

    public string Id => OperationIds.JobGet;

    public bool RequiresElevation => false;

    public Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken cancellationToken)
    {
        if (!TryReadJobId(context.Payload, out var jobId))
        {
            return Task.FromResult(OperationResult.Fail("error", "El parámetro 'jobId' es obligatorio."));
        }

        var job = _jobs.Get(jobId);
        return Task.FromResult(
            job is null
                ? OperationResult.Fail("not_found", $"No existe el job '{jobId}'.")
                : OperationResult.Ok(BridgeJson.ToJson(ToDto(job))));
    }

    internal static JobSnapshotResult ToDto(Job job) =>
        new()
        {
            Id = job.Id,
            Type = job.Type,
            Status = ToStatus(job.Status),
            StartedAt = job.StartedAt,
            FinishedAt = job.FinishedAt,
            ExitCode = job.ExitCode,
            Progress = job.Progress,
            Error = job.Error,
            Result = job.Result,
        };

    internal static string ToStatus(JobStatus status) => status switch
    {
        JobStatus.Running => "running",
        JobStatus.Completed => "completed",
        JobStatus.Failed => "failed",
        JobStatus.Cancelled => "cancelled",
        _ => "running",
    };

    internal static bool TryReadJobId(JsonElement? payload, out string jobId)
    {
        jobId = string.Empty;
        if (payload is not { ValueKind: JsonValueKind.Object } element
            || !element.TryGetProperty("jobId", out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        jobId = property.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(jobId);
    }
}

/// <summary>
/// Publica los eventos del motor de jobs como "job.event" en el puente.
/// Recibe el IEventPublisher diferido: el orden de arranque no importa.
/// </summary>
public sealed class BridgeJobEventPublisher : IJobEventPublisher
{
    private readonly IEventPublisher _bridge;

    public BridgeJobEventPublisher(IEventPublisher bridge) => _bridge = bridge;

    /// <summary>Suscripción en proceso (tests de JobEngine); la UI va por el puente.</summary>
    public event EventHandler<JobEvent>? Published;

    public void Publish(JobEvent jobEvent)
    {
        ArgumentNullException.ThrowIfNull(jobEvent);

        try
        {
            Published?.Invoke(this, jobEvent);
            _bridge.SendEvent(
                EventIds.JobEvent,
                new JobEventResult
                {
                    JobId = jobEvent.JobId,
                    Type = ToType(jobEvent.Type),
                    Status = jobEvent.Status is { } status ? JobGetOperation.ToStatus(status) : null,
                    Progress = jobEvent.Progress,
                    Error = jobEvent.Error,
                    Result = jobEvent.Result,
                });
        }
        catch (JsonException)
        {
            // Un payload no serializable jamás tumbará el motor de jobs.
        }
    }

    private static string ToType(JobEventType type) => type switch
    {
        JobEventType.StatusChanged => "statusChanged",
        JobEventType.Output => "output",
        JobEventType.Progress => "progress",
        _ => "statusChanged",
    };
}

/// <summary>job.cancel — cancela el job en curso (payload: {"jobId"}).</summary>
public sealed class JobCancelOperation : IOperation
{
    private readonly IJobManager _jobs;

    public JobCancelOperation(IJobManager jobs) => _jobs = jobs;

    public string Id => OperationIds.JobCancel;

    public bool RequiresElevation => false;

    public Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken cancellationToken)
    {
        if (!JobGetOperation.TryReadJobId(context.Payload, out var jobId))
        {
            return Task.FromResult(OperationResult.Fail("error", "El parámetro 'jobId' es obligatorio."));
        }

        return Task.FromResult(
            OperationResult.Ok(BridgeJson.ToJson(new JobCancelResult { Cancelled = _jobs.RequestCancel(jobId) })));
    }
}
