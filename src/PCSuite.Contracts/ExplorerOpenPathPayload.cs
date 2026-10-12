namespace PCSuite.Contracts;

/// <summary>Payload de la operación explorer.openPath: abre una ruta en el Explorador de Windows.</summary>
public sealed class ExplorerOpenPathPayload
{
    /// <summary>Ruta absoluta a abrir (archivo o directorio).</summary>
    public string Path { get; init; } = string.Empty;
}