namespace PCSuite.Contracts;

/// <summary>Resultado de <see cref="OperationIds.WingetDetect"/> (Stage 7, Detection).</summary>
public sealed class WinGetDetectResult
{
    /// <summary>True si winget.AppInstaller se encontró en la PATH.</summary>
    public bool Installed { get; init; }

    /// <summary>Versión informada por <c>winget --version</c> (sin la 'v'); se omite si es desconocida.</summary>
    public string? Version { get; init; }

    /// <summary>Ruta resolvida del ejecutable; se omite si no está instalado.</summary>
    public string? Path { get; init; }

    /// <summary>True si el proceso actual ya corre elevado (permisos: sin pedir UAC).</summary>
    public bool IsAdministrator { get; init; }
}
