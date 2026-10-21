namespace PCSuite.Contracts;

/// <summary>Payload de system.writeClipboard: texto a copiar al portapapeles.</summary>
public sealed class SystemWriteClipboardPayload
{
    /// <summary>Texto a copiar (no vacío; tope 1 MB defensivo).</summary>
    public string Text { get; init; } = string.Empty;
}
