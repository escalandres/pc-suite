namespace PCSuite.Core.Wsl;

/// <summary>
/// Fallo controlado de las operaciones wsl.*: código + mensaje aptos para
/// la UI (nunca stack traces). WslOperation lo recoge y lo traduce a la
/// respuesta de error del puente.
/// </summary>
public sealed class WslException : Exception
{
    public WslException(string errorCode, string errorMessage)
        : base(errorMessage)
    {
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public string ErrorCode { get; }

    public string ErrorMessage { get; }
}
