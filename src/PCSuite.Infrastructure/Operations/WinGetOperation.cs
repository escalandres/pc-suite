using System.Text.Json;
using PCSuite.Contracts;
using PCSuite.Core.Operations;
using PCSuite.Core.WinGet;

namespace PCSuite.Infrastructure.Operations;

/// <summary>
/// Base de las 8 operaciones winget.* del puente: payload tipado (el frontend
/// jamás envía una línea de comando), traducción de <see cref="WinGetException"/>
/// a errores controlados y mapeo dominio → DTO camelCase.
/// </summary>
public abstract class WinGetOperation : IOperation
{
    protected WinGetOperation(IWinGetService service) =>
        Service = service ?? throw new ArgumentNullException(nameof(service));

    protected IWinGetService Service { get; }

    public abstract string Id { get; }

    /// <summary>
    /// Las mutaciones pueden terminar en un proceso elevado. La elevación se
    /// decide en runtime dentro de WinGetService (solo si winget falla por
    /// permisos y el proceso no es ya administrador): nunca UAC preventivo.
    /// </summary>
    public virtual bool RequiresElevation => false;

    public async Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken cancellationToken)
    {
        try
        {
            var data = await ExecuteCoreAsync(context, cancellationToken).ConfigureAwait(false);
            return OperationResult.Ok(data);
        }
        catch (WinGetException exception)
        {
            return OperationResult.Fail(exception.ErrorCode, exception.ErrorMessage);
        }
    }

    protected abstract Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken);

    protected static JsonElement? ToCollection(IReadOnlyList<WinGetPackage> packages) =>
        BridgeJson.ToJson(new WinGetPackageCollectionResult
        {
            Packages = packages.Select(package => new WinGetPackageResult
            {
                Id = package.Id,
                Name = package.Name,
                Version = package.Version,
                Available = package.Available,
                Match = package.Match,
            }).ToList(),
        });

    protected static JsonElement? ToMutation(WinGetMutationOutcome outcome) =>
        BridgeJson.ToJson(new WinGetMutationResult
        {
            PackageId = outcome.PackageId,
            Succeeded = outcome.Succeeded,
            ExitCode = outcome.ExitCode,
            Elevated = outcome.Elevated,
            Detail = outcome.Detail,
        });
}
