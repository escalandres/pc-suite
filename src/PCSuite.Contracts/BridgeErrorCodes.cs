namespace PCSuite.Contracts;

public static class BridgeErrorCodes
{
    public const string MalformedMessage = "MALFORMED_MESSAGE";
    public const string OperationNotFound = "OPERATION_NOT_FOUND";
    public const string OperationFailed = "OPERATION_FAILED";
    public const string InternalError = "INTERNAL_ERROR";

    // Códigos controlados de las operaciones developer.*: provienen de los
    // IDeveloperToolProvider y siempre describen un fallo sin stack trace.
    public const string NotSupported = "NOT_SUPPORTED";
    public const string NotInstalled = "NOT_INSTALLED";
    public const string ElevationRequired = "ELEVATION_REQUIRED";
    public const string UpdateFailed = "UPDATE_FAILED";
    public const string UpdateTimeout = "UPDATE_TIMEOUT";
    public const string LaunchFailed = "LAUNCH_FAILED";

    // Códigos del marco de elevación (Task 005): describen el resultado de
    // pedir UAC y del IPC con el proceso elevado, siempre sin stack traces.
    public const string ElevationCancelled = "ELEVATION_CANCELLED";
    public const string ElevationTimeout = "ELEVATION_TIMEOUT";
    public const string ElevationFailed = "ELEVATION_FAILED";

    /// <summary>Identidad de cliente o token de sesión IPC no válidos.</summary>
    public const string Unauthorized = "UNAUTHORIZED";

    // Códigos de las operaciones winget.* (Task 006): fallas al invocar el
    // CLI (sin fila de tabla interpretable) y exceso de tiempo, siempre con
    // mensajes controlados aptos para la UI.
    public const string WingetFailed = "WINGET_FAILED";
    public const string WingetTimeout = "WINGET_TIMEOUT";

    // Códigos de las operaciones wsl.* (Task 007): fallas al invocar el CLI
    // (sin fila de distribuciones interpretable) y exceso de tiempo, siempre
    // con mensajes controlados aptos para la UI.
    public const string WslFailed = "WSL_FAILED";
    public const string WslTimeout = "WSL_TIMEOUT";

    // Códigos de las operaciones clamav.* (Task 008): fallas al invocar el CLI
    // (sin resumen interpretable, ruta inválida para el escaneo) y exceso de
    // tiempo, siempre con mensajes controlados aptos para la UI.
    public const string ClamAvFailed = "CLAMAV_FAILED";
    public const string ClamAvTimeout = "CLAMAV_TIMEOUT";

    // Códigos de las operaciones ssh.* (Task 009): fallas al invocar
    // ssh-keygen (incluida una clave que ya existe, que jamás se sobrescribe)
    // y exceso de tiempo, siempre con mensajes controlados aptos para la UI.
    public const string SshFailed = "SSH_FAILED";
    public const string SshTimeout = "SSH_TIMEOUT";

    // Código de las operaciones cleaner.* (Task 011): entrada inválida (id de
    // categoría desconocido o payload que no es objeto) con mensaje apto para
    // la UI; nunca rutas ni stack traces en el error.
    public const string CleanerFailed = "CLEANER_FAILED";

    // Código de las operaciones pgp.* (Task 009): cualquier fallo controlado
    // (entrada inválida, clave inexistente, passphrase erróneo, material
    // ilegible) con mensaje apto para la UI y jamás material privado ni
    // passphrases. Todo ocurre en proceso, así que no hay timeout propio.
    public const string PgpFailed = "PGP_FAILED";

    // Códigos de uninstall.* (Task 020): fallos del desinstalador forzado.
    public const string UninstallFailed = "UNINSTALL_FAILED";

    // Código de las operaciones files.* (Task 033): fallos controlados de
    // seguridad de archivos (ruta inválida, contraseña vacía, descifrado
    // fallido, error de disco) con mensaje apto para la UI; jamás rutas de
    // otros archivos ni stack traces.
    public const string FilesFailed = "FILES_FAILED";

    // Código de las operaciones cipher.* (Task 034): fallos controlados de
    // codificación/cifrado de texto (algoritmo desconocido, entrada no
    // válida, contraseña vacía, descifrado fallido) con mensaje apto para
    // la UI. Todo es .NET en proceso: no hay timeout propio.
    public const string CipherFailed = "CIPHER_FAILED";

    // Código de las operaciones windows.* (Task 036): fallos controlados del
    // catálogo de comandos de reparación (id desconocido, UAC cancelado,
    // ejecutable ausente, salida con error) con mensaje apto para la UI.
    public const string WindowsFailed = "WINDOWS_FAILED";

    // Códigos de las operaciones ai.* (Task 038): detección/instalación de
    // markItDown y conversión de archivos a Markdown; mensajes aptos para la UI.
    public const string AiFailed = "AI_FAILED";
    public const string AiNotInstalled = "AI_NOT_INSTALLED";
    public const string AiConversionFailed = "AI_CONVERSION_FAILED";
}
