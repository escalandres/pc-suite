using System.Text.Json;
using PCSuite.Contracts;
using PCSuite.Core.Operations;
using PCSuite.Core.WinGet;

namespace PCSuite.Infrastructure.Operations;

/// <summary>winget.search: búsqueda libre por texto (payload: {"query"}).</summary>
public sealed class WinGetSearchOperation : WinGetOperation
{
    public WinGetSearchOperation(IWinGetService service)
        : base(service)
    {
    }

    public override string Id => OperationIds.WingetSearch;

    protected override async Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out WinGetSearchPayload payload))
        {
            throw new WinGetException(BridgeErrorCodes.MalformedMessage, "El payload debe ser un objeto JSON.");
        }

        var packages = await Service.SearchAsync(payload.Query, cancellationToken).ConfigureAwait(false);
        return ToCollection(packages);
    }
}
