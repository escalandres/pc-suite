using System.Text.Json;
using PCSuite.Contracts;
using PCSuite.Core.Files;
using PCSuite.Core.Operations;

namespace PCSuite.Infrastructure.Operations;

public sealed class FilesStegoEmbedOperation : FileSecurityOperation
{
    public FilesStegoEmbedOperation(IFileSecurityService service)
        : base(service)
    {
    }

    public override string Id => OperationIds.FilesStegoEmbed;

    protected override async Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out FilesStegoEmbedPayload payload))
        {
            throw MalformedPayload();
        }

        var result = await Service.StegoEmbedAsync(
            new StegoEmbedRequest(
                payload.ImagePath ?? string.Empty,
                payload.TextPayload,
                payload.FilePayloadPath,
                payload.Password),
            cancellationToken).ConfigureAwait(false);

        return BridgeJson.ToJson(result);
    }
}

public sealed class FilesStegoExtractOperation : FileSecurityOperation
{
    public FilesStegoExtractOperation(IFileSecurityService service)
        : base(service)
    {
    }

    public override string Id => OperationIds.FilesStegoExtract;

    protected override async Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out FilesStegoExtractPayload payload))
        {
            throw MalformedPayload();
        }

        var result = await Service.StegoExtractAsync(
            new StegoExtractRequest(
                payload.ImagePath ?? string.Empty,
                payload.Password),
            cancellationToken).ConfigureAwait(false);

        return BridgeJson.ToJson(result);
    }
}
