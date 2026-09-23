using System.Text.Json;
using PCSuite.Core.Operations;
using PCSuite.Core.WinGet;

namespace PCSuite.Infrastructure.Operations;

/// <summary>winget.upgradeAll: actualiza todo lo actualizable (sin payload).</summary>
public sealed class WinGetUpgradeAllOperation : WinGetOperation
{
    public WinGetUpgradeAllOperation(IWinGetService service)
        : base(service)
    {
    }

    public override string Id => PCSuite.Contracts.OperationIds.WingetUpgradeAll;

    public override bool RequiresElevation => true;

    protected override async Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken)
    {
        var outcome = await Service.UpgradeAllAsync(cancellationToken).ConfigureAwait(false);
        return ToMutation(outcome);
    }
}
