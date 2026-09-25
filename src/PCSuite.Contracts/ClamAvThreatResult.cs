namespace PCSuite.Contracts;

/// <summary>Una amenaza detectada por clamscan (archivo + firma).</summary>
public sealed class ClamAvThreatResult
{
    /// <summary>Ruta del archivo marcado como infectado.</summary>
    public string File { get; init; } = string.Empty;

    /// <summary>Firma de virus que lo detectó (p. ej. Eicar-Test-Signature).</summary>
    public string Threat { get; init; } = string.Empty;
}
