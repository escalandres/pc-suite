using System.Text.Json;
using PCSuite.Contracts;
using PCSuite.Core.Operations;
using PCSuite.Core.WinGet;

namespace PCSuite.Infrastructure.Operations;

/// <summary>winget.updates: paquetes con actualización disponible (sin payload).</summary>
public sealed class WinGetUpdatesOperation : WinGetOperation
{
    public WinGetUpdatesOperation(IWinGetService service)
        : base(service)
    {
    }

    public override string Id => OperationIds.WingetUpdates;

    protected override async Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken)
    {
        var packages = await Service.UpdatesAsync(cancellationToken).ConfigureAwait(false);
        return ToCollection(packages);
    }
}
