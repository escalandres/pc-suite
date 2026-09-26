namespace PCSuite.Contracts;

/// <summary>Payload de <see cref="OperationIds.SshInspect"/> (Task 009 / §5.2).</summary>
public sealed class SshInspectPayload
{
    /// <summary>Ruta absoluta de un archivo .pub o de un contenedor openssh-key-v1.</summary>
    public string? Path { get; init; }
}
