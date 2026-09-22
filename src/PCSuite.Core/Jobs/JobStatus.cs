namespace PCSuite.Core.Jobs;

public enum JobStatus
{
    /// <summary>Reservado para futuros jobs en cola.</summary>
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled,
}
