using System.Text.Json;
using PCSuite.Contracts;
using PCSuite.Core.Files;
using PCSuite.Core.Operations;

namespace PCSuite.Infrastructure.Operations;

public sealed class FilesStegoVerifyOperation : FileSecurityOperation
{
    public FilesStegoVerifyOperation(IFileSecurityService service)
        : base(service)
    {
    }

    public override string Id => OperationIds.FilesStegoVerify;

    protected override async Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out FilesStegoVerifyPayload payload))
        {
            throw MalformedPayload();
        }

        var result = await Service.StegoVerifyAsync(
            new StegoVerifyRequest(payload.ImagePath ?? string.Empty),
            cancellationToken).ConfigureAwait(false);

        return BridgeJson.ToJson(result);
    }
}
