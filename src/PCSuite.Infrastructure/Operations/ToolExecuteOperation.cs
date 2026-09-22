using PCSuite.Application.Tools;
using PCSuite.Contracts;
using PCSuite.Core.Operations;
using PCSuite.Core.Tools;

namespace PCSuite.Infrastructure.Operations;

/// <summary>
/// tool.execute — ejecuta una tool del registry por su id. Solo entran en
/// la allow-list las tools registradas; jamás se acepta un comando arbitrario.
/// </summary>
public sealed class ToolExecuteOperation : IOperation
{
    private readonly ToolRegistry _registry;

    public ToolExecuteOperation(ToolRegistry registry)
    {
        _registry = registry;
    }

    public string Id => OperationIds.ToolExecute;

    public bool RequiresElevation => false;

    public async Task<OperationResult> ExecuteAsync(
        OperationContext context,
        CancellationToken cancellationToken)
    {
        if (!ToolMapper.TryParsePayload<ToolExecutePayload>(context.Payload, out var payload))
        {
            return OperationResult.Fail(BridgeErrorCodes.MalformedMessage, "El payload debe ser un objeto JSON.");
        }

        if (string.IsNullOrWhiteSpace(payload.ToolId))
        {
            return OperationResult.Fail(BridgeErrorCodes.MalformedMessage, "El campo 'toolId' es obligatorio.");
        }

        var tool = _registry.GetTool(payload.ToolId);
        if (tool is null)
        {
            return OperationResult.Fail(
                BridgeErrorCodes.OperationNotFound,
                $"Unknown tool '{payload.ToolId}'.");
        }

        var result = await tool.ExecuteAsync(
            new ToolContext { Input = payload.Input },
            cancellationToken);

        if (!result.Success)
        {
            return OperationResult.Fail(
                BridgeErrorCodes.OperationFailed,
                result.ErrorMessage ?? "The tool failed.");
        }

        var output = new ToolExecuteResult { ToolId = tool.Metadata.Id, Output = result.Output };
        return OperationResult.Ok(BridgeJson.ToJson(output));
    }
}
