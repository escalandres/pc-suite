namespace PCSuite.Contracts;

/// <summary>Resultado de <see cref="OperationIds.SshOpenDirectory"/> (Task 009 / §5.1).</summary>
public sealed class SshOpenDirectoryResult
{
    /// <summary>Ruta del directorio .ssh abierto en el Explorador.</summary>
    public string Path { get; init; } = string.Empty;
}
