using System.Text.Json;
using PCSuite.Contracts;
using PCSuite.Core.Operations;
using PCSuite.Core.WinGet;

namespace PCSuite.Infrastructure.Operations;

/// <summary>winget.uninstall: desinstala por id exacto (payload: {"packageId"}).</summary>
public sealed class WinGetUninstallOperation : WinGetOperation
{
    public WinGetUninstallOperation(IWinGetService service)
        : base(service)
    {
    }

    public override string Id => OperationIds.WingetUninstall;

    public override bool RequiresElevation => true;

    protected override async Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out WinGetPackagePayload payload))
        {
            throw new WinGetException(BridgeErrorCodes.MalformedMessage, "El payload debe ser un objeto JSON.");
        }

        var outcome = await Service.UninstallAsync(payload.PackageId, cancellationToken).ConfigureAwait(false);
        return ToMutation(outcome);
    }
}
