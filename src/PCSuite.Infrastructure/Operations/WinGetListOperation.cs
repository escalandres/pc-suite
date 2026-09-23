using System.Text.Json;
using PCSuite.Contracts;
using PCSuite.Core.Operations;
using PCSuite.Core.WinGet;

namespace PCSuite.Infrastructure.Operations;

/// <summary>winget.list: paquetes instalados de origen winget (sin payload).</summary>
public sealed class WinGetListOperation : WinGetOperation
{
    public WinGetListOperation(IWinGetService service)
        : base(service)
    {
    }

    public override string Id => OperationIds.WingetList;

    protected override async Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken)
    {
        var packages = await Service.ListAsync(cancellationToken).ConfigureAwait(false);
        return ToCollection(packages);
    }
}
