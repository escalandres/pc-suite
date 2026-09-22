using System.Security.Principal;
using PCSuite.Contracts;
using PCSuite.Core.Elevation;
using PCSuite.Core.Operations;
using PCSuite.Infrastructure.Operations;

namespace PCSuite.Infrastructure.Elevation;

/// <summary>
/// Operación PoC <see cref="ElevatedOperationIds.AdminInfo"/>: solo lectura,
/// sin side effects. Devuelve datos de la cuenta/proceso elevado que la app no
/// elevada no puede conocer (p. ej. si el token es administrador real). El
/// payload debe ser un objeto JSON; cualquier otra forma → MALFORMED_MESSAGE.
/// </summary>
public sealed class AdminInfoElevatedOperation : IElevatedOperation
{
    public string Id => ElevatedOperationIds.AdminInfo;

    public Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(OperationResult.Fail(BridgeErrorCodes.NotSupported, "Solo disponible en Windows."));
        }

        if (!ToolMapper.TryParsePayload(context.Payload, out AdminInfoPayload payload))
        {
            return Task.FromResult(
                OperationResult.Fail(BridgeErrorCodes.MalformedMessage, "El payload debe ser un objeto JSON."));
        }

        using var identity = WindowsIdentity.GetCurrent();
        var isAdministrator = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);

        var result = new AdminInfoResult
        {
            IsAdministrator = isAdministrator,
            User = identity.Name,
            MachineName = Environment.MachineName,
            ProcessId = Environment.ProcessId,
            OsVersion = Environment.OSVersion.VersionString,
            TokenGroups = payload.IncludeTokenGroups ? GetTokenGroups(identity) : null,
        };

        return Task.FromResult(OperationResult.Ok(BridgeJson.ToJson(result)));
    }

    /// <summary>SIDs del token traducidos a nombres de cuenta (si no se traducen, el SID literal).</summary>
    private static IReadOnlyList<string> GetTokenGroups(WindowsIdentity identity)
    {
        var groups = new List<string>();

        // Guard local: los miembros de WindowsIdentity (Groups, Translate,
        // IdentityReference.Value) solo existen en Windows (CA1416).
        if (!OperatingSystem.IsWindows())
        {
            return groups;
        }

        var tokenGroups = identity.Groups;
        if (tokenGroups is null)
        {
            return groups;
        }

        foreach (var group in tokenGroups)
        {
            try
            {
                groups.Add(group.Translate(typeof(NTAccount)).Value);
            }
            catch (IdentityNotMappedException)
            {
                groups.Add(group.Value);
            }
        }

        return groups;
    }
}
