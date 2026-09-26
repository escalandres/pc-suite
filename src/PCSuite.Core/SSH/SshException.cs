namespace PCSuite.Core.SSH;

/// <summary>
/// Fallo controlado de las operaciones ssh.*: código + mensaje aptos para la
/// UI (nunca stack traces). SshOperation lo recoge y lo traduce a la
/// respuesta de error del puente.
/// </summary>
public sealed class SshException : Exception
{
    public SshException(string errorCode, string errorMessage)
        : base(errorMessage)
    {
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public string ErrorCode { get; }

    public string ErrorMessage { get; }
}
