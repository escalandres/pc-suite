namespace PCSuite.Contracts;

/// <summary>Resultado de <see cref="OperationIds.SshDetect"/> (Task 009).</summary>
public sealed class SshDetectResult
{
    /// <summary>True si ssh-keygen.exe se localizó (PATH, System32\OpenSSH u OpenSSH común).</summary>
    public bool Available { get; init; }

    /// <summary>Ruta resolvida de ssh-keygen.exe; se omite si no está disponible.</summary>
    public string? Path { get; init; }
}
