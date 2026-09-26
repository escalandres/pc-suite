namespace PCSuite.Contracts;

/// <summary>Payload de <see cref="OperationIds.PgpExportKey"/> (Task 009 / §6).</summary>
public sealed class PgpExportKeyPayload
{
    /// <summary>Fingerprint (o prefijo) de la clave a exportar.</summary>
    public string? Fingerprint { get; init; }

    /// <summary>
    /// Exportar la clave privada (armoured); solo con la privada presente.
    /// El material sale como texto al frontend — nunca a logs.
    /// </summary>
    public bool IncludePrivate { get; init; }
}

/// <summary>Resultado de <see cref="OperationIds.PgpExportKey"/>.</summary>
public sealed class PgpExportKeyResult
{
    /// <summary>Bloque armoured completo (-----BEGIN ... KEY BLOCK-----).</summary>
    public string ArmoredKey { get; init; } = string.Empty;

    /// <summary>True si lo exportado es la clave privada (clave pública en caso contrario).</summary>
    public bool IsPrivate { get; init; }
}
