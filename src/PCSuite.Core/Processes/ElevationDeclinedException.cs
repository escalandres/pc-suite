namespace PCSuite.Core.Processes;

/// <summary>
/// Windows rechazó el lanzamiento elevado (UAC cancelado o denegado —
/// ERROR_CANCELLED/1223). Mensaje controlado: apto para la UI, sin stack
/// traces ni detalles internos.
/// </summary>
public sealed class ElevationDeclinedException : Exception
{
    public ElevationDeclinedException()
        : base("El usuario canceló la solicitud de elevación (UAC).")
    {
    }

    public ElevationDeclinedException(string message)
        : base(message)
    {
    }
}
