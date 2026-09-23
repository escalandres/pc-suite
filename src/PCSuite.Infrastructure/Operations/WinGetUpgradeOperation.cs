using System.Text.Json;
using PCSuite.Contracts;
using PCSuite.Core.Operations;
using PCSuite.Core.WinGet;

namespace PCSuite.Infrastructure.Operations;

/// <summary>winget.upgrade: actualiza un paquete concreto (payload: {"packageId"}).</summary>
public sealed class WinGetUpgradeOperation : WinGetOperation
{
    public WinGetUpgradeOperation(IWinGetService service)
        : base(service)
    {
    }

    public override string Id => OperationIds.WingetUpgrade;

    public override bool RequiresElevation => true;

    protected override async Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out WinGetPackagePayload payload))
        {
            throw new WinGetException(BridgeErrorCodes.MalformedMessage, "El payload debe ser un objeto JSON.");
        }

        var outcome = await Service.UpgradeAsync(payload.PackageId, cancellationToken).ConfigureAwait(false);
        return ToMutation(outcome);
    }
}
