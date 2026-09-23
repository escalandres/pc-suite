using System.Text.Json;
using PCSuite.Contracts;
using PCSuite.Core.Operations;
using PCSuite.Core.WinGet;

namespace PCSuite.Infrastructure.Operations;

/// <summary>winget.detect: detección de winget + privilegio actual (Stage 7).</summary>
public sealed class WinGetDetectOperation : WinGetOperation
{
    public WinGetDetectOperation(IWinGetService service)
        : base(service)
    {
    }

    public override string Id => OperationIds.WingetDetect;

    protected override async Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken)
    {
        var detection = await Service.DetectAsync(cancellationToken).ConfigureAwait(false);
        return BridgeJson.ToJson(new WinGetDetectResult
        {
            Installed = detection.Installed,
            Version = detection.Version,
            Path = detection.Path,
            IsAdministrator = detection.IsAdministrator,
        });
    }
}
