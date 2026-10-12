namespace PCSuite.Contracts;

/// <summary>Payload de la operación explorer.openUrl: abre una URL http/https en el navegador del sistema.</summary>
public sealed class ExplorerOpenUrlPayload
{
    /// <summary>URL absoluta http/https a abrir.</summary>
    public string Url { get; init; } = string.Empty;
}
