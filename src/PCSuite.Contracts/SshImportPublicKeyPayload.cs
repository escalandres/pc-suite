namespace PCSuite.Contracts;

/// <summary>Payload de <see cref="OperationIds.SshImportPublicKey"/> (Task 009 / §5.1).</summary>
public sealed class SshImportPublicKeyPayload
{
    /// <summary>Línea de clave pública (type base64 [comentario]) ya parseable.</summary>
    public string? Key { get; init; }

    /// <summary>Nombre del archivo .pub a crear; se omite para derivarlo del fingerprint.</summary>
    public string? FileName { get; init; }

    /// <summary>Carpeta de destino; por defecto ~/.ssh (creada si falta).</summary>
    public string? Directory { get; init; }
}

/// <summary>Resultado de <see cref="OperationIds.SshImportPublicKey"/>.</summary>
public sealed class SshImportPublicKeyResult
{
    /// <summary>Ruta absoluta del archivo .pub escrito.</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>Clave importada, ya inspeccionada.</summary>
    public SshKeyInfoResult Key { get; init; } = new();
}
