namespace PCSuite.Core.SSH;

/// <summary>
/// Fachada SSH (Task 009 / §5.1–5.2). La generación de pares usa la
/// implementación madura <c>ssh-keygen</c> de Windows con argv fijo
/// construido en Infrastructure (nunca shell ni argumentos arbitrarios desde
/// el frontend; si no está disponible → error controlado NOT_INSTALLED).
/// Inspección, fingerprint SHA256, listado e importación son 100% .NET
/// (<see cref="SshKeyParser"/>), sin binarios. El passphrase y el material
/// privado jamás se registran en logs; nada requiere elevación (todo escribe
/// en directorios de usuario).
/// </summary>
public interface ISshKeyService
{
    /// <summary>Detección: ssh-keygen localizado/no y ruta resuelta (§5.1).</summary>
    Task<SshDetectStatus> DetectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Claves existentes (§5.1 "detect existing keys"): por defecto en ~/.ssh;
    /// si la carpeta no existe, lista vacía.
    /// </summary>
    Task<IReadOnlyList<SshKeyInfo>> ListAsync(string? directory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inspección (§5.2) de un archivo `.pub` o de un contenedor privado
    /// <c>openssh-key-v1</c>, cuya parte pública va en claro.
    /// </summary>
    Task<SshKeyInfo> InspectAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generación del par (§5.1) con argv fijo de ssh-keygen. Nunca
    /// sobrescribe archivos existentes ni dispara prompts interactivos.
    /// </summary>
    Task<SshGenerateOutcome> GenerateAsync(SshGenerateRequest request, CancellationToken cancellationToken = default);

    /// <summary>Importación de una clave pública (línea authorized_keys) al directorio indicado.</summary>
    Task<SshImportOutcome> ImportPublicKeyAsync(SshImportRequest request, CancellationToken cancellationToken = default);

    /// <summary>Crea ~/.ssh si falta, lo abre en el Explorador y devuelve la ruta (§5.1).</summary>
    Task<string> OpenSshDirectoryAsync(CancellationToken cancellationToken = default);
}
