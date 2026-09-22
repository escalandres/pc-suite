using PCSuite.Application.Tools;
using PCSuite.Contracts;
using PCSuite.Core.Operations;

namespace PCSuite.Infrastructure.Operations;

/// <summary>tools.list — lista los metadatos de todas las tools registradas.</summary>
public sealed class ToolsListOperation : IOperation
{
    private readonly ToolRegistry _registry;

    public ToolsListOperation(ToolRegistry registry)
    {
        _registry = registry;
    }

    public string Id => OperationIds.ToolsList;

    public bool RequiresElevation => false;

    public Task<OperationResult> ExecuteAsync(
        OperationContext context,
        CancellationToken cancellationToken)
    {
        var collection = ToolMapper.ToCollection(_registry.Tools);
        return Task.FromResult(OperationResult.Ok(BridgeJson.ToJson(collection)));
    }
}
