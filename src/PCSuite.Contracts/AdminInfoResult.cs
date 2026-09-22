namespace PCSuite.Contracts;

/// <summary>Resultado de <see cref="ElevatedOperationIds.AdminInfo"/>.</summary>
public sealed class AdminInfoResult
{
    /// <summary>True si el token del proceso elevado pertenece a Administradores (UAC aceptado).</summary>
    public bool IsAdministrator { get; init; }

    /// <summary>Cuenta actual (DOMINIO\usuario del token elevado).</summary>
    public string User { get; init; } = "";

    public string MachineName { get; init; } = "";

    public int ProcessId { get; init; }

    public string OsVersion { get; init; } = "";

    /// <summary>Solo si se pidió en el payload; se omite del JSON si es null.</summary>
    public IReadOnlyList<string>? TokenGroups { get; init; }
}
