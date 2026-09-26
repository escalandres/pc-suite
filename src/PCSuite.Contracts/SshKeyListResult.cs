namespace PCSuite.Contracts;

/// <summary>Resultado de <see cref="OperationIds.SshList"/> (Task 009).</summary>
public sealed class SshKeyListResult
{
    /// <summary>Claves públicas encontradas (lista vacía si la carpeta no existe).</summary>
    public SshKeyInfoResult[] Keys { get; init; } = [];
}

/// <summary>Payload de <see cref="OperationIds.SshList"/> (opcional; por defecto ~/.ssh).</summary>
public sealed class SshListPayload
{
    /// <summary>Carpeta a listar; se omite para usar ~/.ssh.</summary>
    public string? Directory { get; init; }
}
