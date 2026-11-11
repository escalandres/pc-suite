using System.Text.Json;
using PCSuite.Contracts;
using PCSuite.Core.Files;
using PCSuite.Core.Operations;
using PCSuite.Infrastructure.Files;

namespace PCSuite.Infrastructure.Operations;

public sealed class FilesFido2EncryptOperation : FileSecurityOperation
{
    public FilesFido2EncryptOperation(IFileSecurityService service) : base(service) { }
    public override string Id => OperationIds.FilesFido2Encrypt;

    protected override async Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out FilesEncryptPayload payload))
            throw MalformedPayload();

        var result = await Service.EncryptWithKeyAsync(
            new FileEncryptWithKeyRequest(
                payload.InputPath ?? string.Empty,
                payload.Password ?? string.Empty,
                payload.OutputPath,
                payload.DeleteOriginal),
            cancellationToken).ConfigureAwait(false);

        return BridgeJson.ToJson(new FileEncryptResult(result.OutputPath, result.Sha256, result.Kind));
    }
}

public sealed class FilesFido2DecryptOperation : FileSecurityOperation
{
    public FilesFido2DecryptOperation(IFileSecurityService service) : base(service) { }
    public override string Id => OperationIds.FilesFido2Decrypt;

    protected override async Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out FilesDecryptPayload payload))
            throw MalformedPayload();

        var result = await Service.DecryptWithKeyAsync(
            new FileDecryptWithKeyRequest(
                payload.InputPath ?? string.Empty,
                payload.Password ?? string.Empty,
                payload.OutputPath),
            cancellationToken).ConfigureAwait(false);

        return BridgeJson.ToJson(new FileDecryptResult(result.OutputPath, result.Sha256, result.Kind));
    }
}