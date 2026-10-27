namespace PCSuite.Contracts;

/// <summary>Payload de <see cref="OperationIds.FilesEncrypt"/> (Task 033).</summary>
public sealed class FilesEncryptPayload
{
    /// <summary>Ruta absoluta del archivo a encriptar (requerido).</summary>
    public string? InputPath { get; init; }

    /// <summary>Contraseña; jamás se registra (Task 033 sin ILogger a propósito).</summary>
    public string? Password { get; init; }

    /// <summary>Ruta de salida; por defecto <input>.pcsuite-enc.</summary>
    public string? OutputPath { get; init; }

    /// <summary>Borrar el original de forma segura (3 pasadas) tras encriptar.</summary>
    public bool? DeleteOriginal { get; init; }
}

/// <summary>Payload de <see cref="OperationIds.FilesDecrypt"/> (Task 033).</summary>
public sealed class FilesDecryptPayload
{
    /// <summary>Ruta absoluta del archivo .pcsuite-enc (requerido).</summary>
    public string? InputPath { get; init; }

    /// <summary>La misma contraseña con la que se encriptó.</summary>
    public string? Password { get; init; }

    /// <summary>Ruta de salida; por defecto el nombre sin .pcsuite-enc.</summary>
    public string? OutputPath { get; init; }
}

/// <summary>Payload de <see cref="OperationIds.FilesShred"/> (Task 033).</summary>
public sealed class FilesShredPayload
{
    /// <summary>Ruta absoluta del archivo a sobrescribir y borrar (requerido).</summary>
    public string? Path { get; init; }

    /// <summary>Pasadas de sobrescritura (1–7); por defecto 3.</summary>
    public int? Passes { get; init; }
}

/// <summary>Payload de <see cref="OperationIds.FilesHash"/> (Task 033).</summary>
public sealed class FilesHashPayload
{
    /// <summary>Ruta absoluta del archivo a hashear (requerido).</summary>
    public string? Path { get; init; }
}
