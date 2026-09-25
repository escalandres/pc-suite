namespace PCSuite.Core.ClamAV;

/// <summary>
/// Fallo controlado de las operaciones clamav.*: código + mensaje aptos para
/// la UI (nunca stack traces). ClamAvOperation lo recoge y lo traduce a la
/// respuesta de error del puente.
/// </summary>
public sealed class ClamAvException : Exception
{
    public ClamAvException(string errorCode, string errorMessage)
        : base(errorMessage)
    {
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public string ErrorCode { get; }

    public string ErrorMessage { get; }
}
