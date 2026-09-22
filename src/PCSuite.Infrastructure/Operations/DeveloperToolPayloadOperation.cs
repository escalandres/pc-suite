using PCSuite.Application.Developers;
using PCSuite.Contracts;
using PCSuite.Core.Developers;
using PCSuite.Core.Operations;

namespace PCSuite.Infrastructure.Operations;

/// <summary>
/// Base de las operaciones <c>developer.*</c> con payload: valida el JSON,
/// resuelve el proveedor contra el registro (solo ids registrados; jamás un
/// comando arbitrario) y delega la acción concreta.
/// </summary>
public abstract class DeveloperToolPayloadOperation : IOperation
{
    private readonly DeveloperToolRegistry _registry;

    protected DeveloperToolPayloadOperation(DeveloperToolRegistry registry)
    {
        _registry = registry;
    }

    public abstract string Id { get; }

    public bool RequiresElevation => false;

    public async Task<OperationResult> ExecuteAsync(
        OperationContext context,
        CancellationToken cancellationToken)
    {
        if (!ToolMapper.TryParsePayload<DeveloperToolPayload>(context.Payload, out var payload))
        {
            return OperationResult.Fail(BridgeErrorCodes.MalformedMessage, "El payload debe ser un objeto JSON.");
        }

        if (string.IsNullOrWhiteSpace(payload.ToolId))
        {
            return OperationResult.Fail(
                BridgeErrorCodes.MalformedMessage,
                "El campo 'toolId' es obligatorio.");
        }

        var provider = _registry.Get(payload.ToolId);
        if (provider is null)
        {
            return OperationResult.Fail(
                BridgeErrorCodes.OperationNotFound,
                $"Unknown developer tool '{payload.ToolId}'.");
        }

        return await ExecuteForAsync(provider, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Ejecuta la acción concreta sobre el proveedor resuelto.</summary>
    protected abstract Task<OperationResult> ExecuteForAsync(
        IDeveloperToolProvider provider,
        CancellationToken cancellationToken);
}
