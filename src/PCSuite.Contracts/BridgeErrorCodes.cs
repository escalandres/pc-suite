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
}
