namespace PCSuite.Core.Cleaner;

/// <summary>
/// Fallo controlado de las operaciones cleaner.*: código + mensaje aptos para
/// la UI (nunca rutas arbitrarias ni stack traces). CleanerOperation lo
/// recoge y lo traduce a la respuesta de error del puente.
/// </summary>
public sealed class CleanerException : Exception
{
    public CleanerException(string errorCode, string errorMessage)
        : base(errorMessage)
    {
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public string ErrorCode { get; }

    public string ErrorMessage { get; }
}
