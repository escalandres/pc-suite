namespace PCSuite.Contracts;

/// <summary>Payload de clamav.customScan: la ÚNICA ruta que viaja desde el frontend.</summary>
public sealed class ClamAvScanPayload
{
    /// <summary>Ruta absoluta existente (archivo o carpeta) a escanear.</summary>
    public string? Path { get; init; }
}
