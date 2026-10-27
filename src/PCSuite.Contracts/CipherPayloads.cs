namespace PCSuite.Contracts;

/// <summary>
/// Resultado de texto para las operaciones cipher.* (Task 034): el resultado
/// serializado es <c>{ "output": "..." }</c> en camelCase.
/// </summary>
public sealed class CipherTextResult
{
    /// <summary>Texto de salida (codificado, hash o ciphertext según la op).</summary>
    public string Output { get; init; } = string.Empty;
}

/// <summary>Payload de <see cref="OperationIds.CipherEncode"/> (Task 034).</summary>
public sealed class CipherEncodePayload
{
    /// <summary>Algoritmo: base64 | hex | url | rot13 | caesar | binary | morse.</summary>
    public string? Algorithm { get; init; }

    /// <summary>Texto de entrada.</summary>
    public string? Input { get; init; }

    /// <summary>Desplazamiento para César (1–25); ROT13 es fijo.</summary>
    public int? Shift { get; init; }

    /// <summary>encode | decode — el frontend envía la dirección explícita.</summary>
    public string? Direction { get; init; }

    /// <summary>Clave (reservado; ningún codificador actual la usa).</summary>
    public string? Key { get; init; }
}

/// <summary>Payload de <see cref="OperationIds.CipherHash"/> (Task 034).</summary>
public sealed class CipherHashPayload
{
    /// <summary>Texto a hashear (MD5/SHA-1/SHA-256/SHA-512 en una sola pasada).</summary>
    public string? Input { get; init; }
}

/// <summary>Payload de <see cref="OperationIds.CipherEncryptAes"/> y
/// <see cref="OperationIds.CipherDecryptAes"/> (Task 034).</summary>
public sealed class CipherAesPayload
{
    /// <summary>Texto plano (cifrar) o ciphertext base64 (descifrar).</summary>
    public string? Input { get; init; }

    /// <summary>Contraseña PBKDF2; jamás se registra.</summary>
    public string? Password { get; init; }
}

/// <summary>Payload de <see cref="OperationIds.CipherXor"/> (Task 034).</summary>
public sealed class CipherXorPayload
{
    /// <summary>Texto plano (cifrar) o base64 (descifrar).</summary>
    public string? Input { get; init; }

    /// <summary>Clave XOR repetida.</summary>
    public string? Key { get; init; }

    /// <summary>encrypt | decrypt — cifrar devuelve base64, descifrar texto.</summary>
    public string? Direction { get; init; }
}
