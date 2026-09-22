namespace PCSuite.Core.Jobs;

public enum JobEventType
{
    /// <summary>Cambio de estado del job (Status lleva el nuevo estado).</summary>
    StatusChanged,

    /// <summary>Línea de stdout/stderr en streaming (Stream + Line).</summary>
    Output,

    /// <summary>Cambio de progreso (Progress 0..1).</summary>
    Progress,
}

public enum JobLogStream
{
    StandardOutput,
    StandardError,
}

/// <summary>
/// Evento inmutable emitido por el motor de jobs: cambios de estado,
/// líneas de salida en streaming y progreso. Es el puente entre el motor
/// nativo y cualquier suscriptor (p. ej. la UI vía WebView2).
/// </summary>
public sealed class JobEvent
{
    public required string JobId { get; init; }

    public JobEventType Type { get; init; }

    public JobStatus? Status { get; init; }

    public double? Progress { get; init; }

    public JobLogStream? Stream { get; init; }

    public string? Line { get; init; }

    /// <summary>Detalle del error cuando el job pasa a estado fallido.</summary>
    public string? Error { get; init; }
}
