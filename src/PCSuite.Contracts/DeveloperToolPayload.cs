namespace PCSuite.Contracts;

/// <summary>Payload de developer.checkUpdate, developer.update y developer.launch.</summary>
public sealed class DeveloperToolPayload
{
    /// <summary>
    /// Id de la tool de desarrollador; debe existir en el registro. Jamás es
    /// un comando arbitrario.
    /// </summary>
    public string? ToolId { get; init; }
}
