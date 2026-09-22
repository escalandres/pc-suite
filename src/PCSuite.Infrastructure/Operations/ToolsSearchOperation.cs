using PCSuite.Application.Tools;
using PCSuite.Contracts;
using PCSuite.Core.Operations;

namespace PCSuite.Infrastructure.Operations;

/// <summary>tools.search — busca tools por texto libre sobre sus metadatos.</summary>
public sealed class ToolsSearchOperation : IOperation
{
    private readonly ToolSearchService _search;

    public ToolsSearchOperation(ToolSearchService search)
    {
        _search = search;
    }

    public string Id => OperationIds.ToolsSearch;

    public bool RequiresElevation => false;

    public Task<OperationResult> ExecuteAsync(
        OperationContext context,
        CancellationToken cancellationToken)
    {
        if (!ToolMapper.TryParsePayload<ToolSearchPayload>(context.Payload, out var payload))
        {
            return Task.FromResult(
                OperationResult.Fail(BridgeErrorCodes.MalformedMessage, "El payload debe ser un objeto JSON."));
        }

        var collection = ToolMapper.ToCollection(_search.Search(payload.Query));
        return Task.FromResult(OperationResult.Ok(BridgeJson.ToJson(collection)));
    }
}
