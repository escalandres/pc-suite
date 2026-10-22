using System.Text.Json;

namespace PCSuite.Contracts;

/// <summary>Resultado de iniciar un escaneo clamav.*: el job queda corriendo.</summary>
public sealed class ClamAvScanStartResult
{
    public string JobId { get; init; } = "";
}

/// <summary>
/// Resultado de clamav.activeScan (Task 025.1): el escaneo en curso para que
/// la UI se re-adjunte tras recargar/navegar. Vacío (todas las propiedades
/// null/ausentes) si no hay escaneo activo.
/// </summary>
public sealed class ClamAvActiveScanResult
{
    /// <summary>Id del job activo; null si no hay escaneo corriendo.</summary>
    public string? JobId { get; init; }

    /// <summary>quick | full | custom; null si no hay escaneo activo.</summary>
    public string? Kind { get; init; }

    /// <summary>Instante de arranque del escaneo activo (para el cronómetro).</summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>Progreso 0..1 conocido hasta el momento.</summary>
    public double? Progress { get; init; }
}

/// <summary>Snapshot de un job expuesto al frontend (job.get / job.event).</summary>
public sealed class JobSnapshotResult
{
    public string Id { get; init; } = "";

    public string Type { get; init; } = "";

    /// <summary>running | completed | failed | cancelled.</summary>
    public string Status { get; init; } = "";

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? FinishedAt { get; init; }

    public int? ExitCode { get; init; }

    /// <summary>Progreso 0..1.</summary>
    public double? Progress { get; init; }

    public string? Error { get; init; }

    /// <summary>Resultado estructurado al completarse (p. ej. outcome del escaneo).</summary>
    public JsonElement? Result { get; init; }
}

/// <summary>Evento de ciclo de vida de un job (job.event).</summary>
public sealed class JobEventResult
{
    public string JobId { get; init; } = "";

    /// <summary>statusChanged | output | progress.</summary>
    public string Type { get; init; } = "";

    public string? Status { get; init; }

    public double? Progress { get; init; }

    public string? Error { get; init; }

    public JsonElement? Result { get; init; }
}

/// <summary>Resultado de job.cancel.</summary>
public sealed class JobCancelResult
{
    /// <summary>false: el job no existe o ya terminó.</summary>
    public bool Cancelled { get; init; }
}
