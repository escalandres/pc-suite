namespace PCSuite.Contracts;

/// <summary>Payload de <see cref="OperationIds.SshGenerate"/> (Task 009 / §5.1).</summary>
public sealed class SshGeneratePayload
{
    /// <summary>ed25519 | rsa | ecdsa (validado contra allow-list en el servicio).</summary>
    public string? Algorithm { get; init; }

    /// <summary>Bits para RSA (2048/3072/4096) o ECDSA (256/384/521); se omite en Ed25519.</summary>
    public int? Bits { get; init; }

    /// <summary>Comentario de la clave (p. ej. usuario@equipo).</summary>
    public string? Comment { get; init; }

    /// <summary>Carpeta de salida; debe ser ruta absoluta (por defecto ~/.ssh).</summary>
    public string? Directory { get; init; }

    /// <summary>Passphrase opcional para proteger la clave privada; jamás se registra.</summary>
    public string? Passphrase { get; init; }
}

/// <summary>Resultado de <see cref="OperationIds.SshGenerate"/>.</summary>
public sealed class SshGenerateResult
{
    /// <summary>True si ssh-keygen generó el par; false para fallos controlados.</summary>
    public bool Succeeded { get; init; }

    /// <summary>Motivo controlado cuando Succeeded es false; se omite al tener éxito.</summary>
    public string? Detail { get; init; }

    /// <summary>Ruta de la clave privada creada.</summary>
    public string? PrivateKeyPath { get; init; }

    /// <summary>Ruta de la clave pública creada.</summary>
    public string? PublicKeyPath { get; init; }

    /// <summary>Clave pública resultante, cuando pudo inspeccionarse.</summary>
    public SshKeyInfoResult? Key { get; init; }
}
