namespace PCSuite.Contracts;

/// <summary>Una línea del diagnóstico de seguridad (Task 012 / §14).</summary>
public sealed class SecurityCheckResult
{
    /// <summary>Id estable del check (p. ej. "uac", "rdp", "realtime").</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Etiqueta visible para la UI.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>ok / warn / unknown.</summary>
    public string Status { get; init; } = "unknown";

    /// <summary>Detalle legible (p. ej. "desde 2026-09-01"); null si no aplica. Nunca stack traces.</summary>
    public string? Detail { get; init; }
}
