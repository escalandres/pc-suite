namespace PCSuite.Contracts;

/// <summary>
/// Resultado de una mutación wsl (update/shutdown/start). Un fallo del CLI NO
/// es un error del puente: vuelve como resultado estructurado (success: true
/// con succeeded: false) con el detalle controlado (sin stack traces).
/// </summary>
public sealed class WslMutationResult
{
    /// <summary>True si wsl terminó con código 0.</summary>
    public bool Succeeded { get; init; }

    /// <summary>Código de salida de wsl.exe.</summary>
    public int ExitCode { get; init; }

    /// <summary>True si la operación corrió en el proceso elevado (UAC aceptado).</summary>
    public bool Elevated { get; init; }

    /// <summary>Última línea no vacía de la salida de wsl (recortada); se omite si no hay.</summary>
    public string? Detail { get; init; }
}
