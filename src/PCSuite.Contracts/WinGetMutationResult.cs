namespace PCSuite.Contracts;

/// <summary>
/// Resultado de una mutación winget (install/upgrade/upgradeAll/uninstall).
/// Un fallo del CLI NO es un error del puente: vuelve como resultado
/// estructurado (success: true con succeeded: false) para que la UI muestre
/// el detalle controlado (última línea de winget, sin stack traces).
/// </summary>
public sealed class WinGetMutationResult
{
    /// <summary>Id del paquete afectado; se omite en upgradeAll.</summary>
    public string? PackageId { get; init; }

    /// <summary>True si winget terminó con código 0.</summary>
    public bool Succeeded { get; init; }

    /// <summary>Código de salida de winget (p. ej. -1978335212 = sin coincidencias).</summary>
    public int ExitCode { get; init; }

    /// <summary>True si la operación corrió en el proceso elevado (UAC aceptado).</summary>
    public bool Elevated { get; init; }

    /// <summary>Última línea no vacía de la salida de winget (recortada); se omite si no hay.</summary>
    public string? Detail { get; init; }
}
