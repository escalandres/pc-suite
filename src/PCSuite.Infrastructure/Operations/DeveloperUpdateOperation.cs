using PCSuite.Application.Developers;
using PCSuite.Contracts;
using PCSuite.Core.Developers;
using PCSuite.Core.Operations;

namespace PCSuite.Infrastructure.Operations;

/// <summary>
/// developer.update — ejecuta el self-update nativo de la herramienta
/// (p. ej. <c>npm install -g npm@latest</c>). Los códigos controlados del
/// proveedor (<c>NOT_SUPPORTED</c>, <c>ELEVATION_REQUIRED</c>,
/// <c>UPDATE_TIMEOUT</c>...) pasan tal cual a la UI, sin stack traces.
/// </summary>
public sealed class DeveloperUpdateOperation : DeveloperToolPayloadOperation
{
    public DeveloperUpdateOperation(DeveloperToolRegistry registry)
        : base(registry)
    {
    }

    public override string Id => OperationIds.DeveloperUpdate;

    protected override Task<OperationResult> ExecuteForAsync(
        IDeveloperToolProvider provider,
        CancellationToken cancellationToken) =>
        provider.UpdateAsync(cancellationToken);
}
