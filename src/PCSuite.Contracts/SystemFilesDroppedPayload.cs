namespace PCSuite.Contracts;

/// <summary>
/// Payload del evento system.filesDropped (Task 026.3): rutas absolutas de
/// los archivos/carpetas soltados sobre PCSuite, capturadas por el HOST
/// nativo (la página no puede verlas — limitación de Chromium en WebView2).
/// </summary>
public sealed class SystemFilesDroppedPayload
{
    /// <summary>Rutas absolutas del CF_HDROP real del drop.</summary>
    public IReadOnlyList<string> Paths { get; init; } = [];
}
