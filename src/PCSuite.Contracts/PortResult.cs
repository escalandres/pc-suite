namespace PCSuite.Contracts;

/// <summary>Puerto en escucha a la salida de security.getStatus.</summary>
public sealed class PortResult
{
    /// <summary>Protocolo (TCP/UDP).</summary>
    public string Protocol { get; init; } = string.Empty;

    /// <summary>Dirección local (0.0.0.0, ::, 127.0.0.1…).</summary>
    public string LocalAddress { get; init; } = string.Empty;

    /// <summary>Puerto local.</summary>
    public int LocalPort { get; init; }

    /// <summary>PID del proceso dueño (null si la ACL lo niega).</summary>
    public int? Pid { get; init; }

    /// <summary>Nombre del proceso (null si no se pudo resolver).</summary>
    public string? Process { get; init; }

    /// <summary>Task 029: detalle del proceso (ruta + firma) para tooltip.</summary>
    public PortProcessResult? ProcessDetail { get; init; }
}
