namespace PCSuite.Core.Security;

/// <summary>
/// Fachada del Security Center (Task 012 / §14): SOLO LECTURA — estado de
/// Defender, firewall por perfil, puertos en escucha, antivirus registrados y
/// diagnóstico de seguridad. Nunca modifica ajustes de seguridad sensibles
/// (§14 exige confirmación explícita del usuario; las acciones vendrán en una
/// task futura con confirmación y registro). Sin elevación: cualquier fuente
/// inaccesible degrada a estado <c>unknown</c> con detalle legible.
/// </summary>
public interface ISecurityService
{
    /// <summary>Snapshot completo del Centro de seguridad (payload cero).</summary>
    Task<SecuritySnapshot> GetStatusAsync(CancellationToken cancellationToken = default);
}
