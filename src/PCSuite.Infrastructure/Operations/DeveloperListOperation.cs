using PCSuite.Application.Developers;
using PCSuite.Contracts;
using PCSuite.Core.Operations;

namespace PCSuite.Infrastructure.Operations;

/// <summary>
/// developer.list — detecta todas las herramientas de desarrollador
/// registradas (instalación, versión y ruta). La detección corre en paralelo:
/// una herramienta lenta no bloquea a las demás.
/// </summary>
public sealed class DeveloperListOperation : IOperation
{
    private readonly DeveloperToolRegistry _registry;

    public DeveloperListOperation(DeveloperToolRegistry registry)
    {
        _registry = registry;
    }

    public string Id => OperationIds.DeveloperList;

    public bool RequiresElevation => false;

    public async Task<OperationResult> ExecuteAsync(
        OperationContext context,
        CancellationToken cancellationToken)
    {
        var providers = _registry.Providers.ToArray();
        var statuses = await Task
            .WhenAll(providers.Select(provider => provider.GetStatusAsync(cancellationToken)))
            .ConfigureAwait(false);

        var tools = providers
            .Zip(statuses, (provider, status) => new DeveloperToolInfoResult
            {
                Id = provider.Id,
                Name = provider.Name,
                IsInstalled = status.IsInstalled,
                Version = status.Version,
                Path = status.Path,
            })
            .ToList();

        return OperationResult.Ok(BridgeJson.ToJson(new DeveloperToolCollectionResult { Tools = tools }));
    }
}
