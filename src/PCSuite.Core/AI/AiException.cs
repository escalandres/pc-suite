namespace PCSuite.Core.AI;

/// <summary>
/// Fallo controlado de las operaciones ai.*: código + mensaje aptos para la UI
/// (nunca stack traces). AiOperation lo recoge y lo traduce a la respuesta de
/// error del puente.
/// </summary>
public sealed class AiException : Exception
{
    public AiException(string errorCode, string errorMessage)
        : base(errorMessage)
    {
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public string ErrorCode { get; }

    public string ErrorMessage { get; }
}
