namespace PCSuite.Core.PGP;

/// <summary>
/// Fallo controlado de las operaciones pgp.*: código + mensaje aptos para la
/// UI (nunca stack traces, ni material privado, ni passphrases).
/// PgpOperation lo recoge y lo traduce a la respuesta de error del puente.
/// </summary>
public sealed class PgpException : Exception
{
    public PgpException(string errorCode, string errorMessage)
        : base(errorMessage)
    {
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public string ErrorCode { get; }

    public string ErrorMessage { get; }
}
