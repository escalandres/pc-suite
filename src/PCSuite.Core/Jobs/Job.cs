namespace PCSuite.Core.Jobs;

/// <summary>
/// Estado de una operación de larga duración. El <c>JobManager</c> nunca
/// expone esta instancia viva: siempre entrega copias (snapshots).
/// </summary>
public sealed class Job
{
    public required string Id { get; init; }

    /// <summary>Categoría del job, p. ej. "winget.install", "mock.sleep".</summary>
    public required string Type { get; init; }

    public JobStatus Status { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public int? ExitCode { get; set; }

    /// <summary>Progreso 0..1, si la operación lo reporta.</summary>
    public double? Progress { get; set; }

    /// <summary>Detalle del error (mensaje controlado, sin stack traces).</summary>
    public string? Error { get; set; }

    public bool IsFinished => Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled;
}
