namespace PCSuite.Contracts;

/// <summary>Clave SSH inspeccionada o listada (campos de §5.2).</summary>
public sealed class SshKeyInfoResult
{
    /// <summary>Tipo wire: ssh-ed25519 / ssh-rsa / ecdsa-sha2-nistp256|384|521.</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>Algoritmo legible: Ed25519 / RSA / ECDSA.</summary>
    public string Algorithm { get; init; } = string.Empty;

    /// <summary>Tamaño en bits (256 para Ed25519; 521 para nistp521).</summary>
    public int Bits { get; init; }

    /// <summary>Fingerprint SHA256 en el mismo formato que `ssh-keygen -lf`.</summary>
    public string Fingerprint { get; init; } = string.Empty;

    /// <summary>Comentario de la línea (puede estar vacío).</summary>
    public string Comment { get; init; } = string.Empty;

    /// <summary>Formato: "OpenSSH".</summary>
    public string Format { get; init; } = string.Empty;

    /// <summary>Ruta absoluta del archivo inspeccionado.</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>True si existe la clave privada hermana (misma base sin .pub).</summary>
    public bool HasPrivate { get; init; }

    /// <summary>Línea pública completa (clave + comentario) para copiar al portapapeles.</summary>
    public string PublicKey { get; init; } = string.Empty;
}
