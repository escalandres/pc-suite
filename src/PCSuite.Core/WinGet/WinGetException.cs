namespace PCSuite.Core.WinGet;

/// <summary>
/// Fallo controlado de las operaciones winget.*: código + mensaje aptos para
/// la UI (nunca stack traces). CommandRouter lo recoge y lo traduce a la
/// respuesta de error del puente.
/// </summary>
public sealed class WinGetException : Exception
{
    public WinGetException(string errorCode, string errorMessage)
        : base(errorMessage)
    {
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public string ErrorCode { get; }

    public string ErrorMessage { get; }
}
