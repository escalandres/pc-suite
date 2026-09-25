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
}
