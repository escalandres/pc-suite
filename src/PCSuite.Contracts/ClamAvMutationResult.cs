namespace PCSuite.Contracts;

/// <summary>
/// Resultado de una mutación ClamAV (install/updateDefinitions). Un fallo del
/// CLI o de winget NO es un error del puente: vuelve como resultado
/// estructurado (success: true con succeeded: false) con el detalle controlado
/// (sin stack traces).
/// </summary>
public sealed class ClamAvMutationResult
{
    /// <summary>True si la operación terminó con código 0.</summary>
    public bool Succeeded { get; init; }

    /// <summary>Código de salida del CLI (o de winget, en install).</summary>
    public int ExitCode { get; init; }

    /// <summary>True si la operación corrió en el proceso elevado (UAC aceptado).</summary>
    public bool Elevated { get; init; }

    /// <summary>Última línea no vacía de la salida (recortada); se omite si no hay.</summary>
    public string? Detail { get; init; }
}
