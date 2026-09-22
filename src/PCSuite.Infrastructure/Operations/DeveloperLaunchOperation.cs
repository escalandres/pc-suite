using PCSuite.Application.Developers;
using PCSuite.Contracts;
using PCSuite.Core.Developers;
using PCSuite.Core.Operations;

namespace PCSuite.Infrastructure.Operations;

/// <summary>
/// developer.launch — lanza la interfaz interactiva (REPL/TUI) de la
/// herramienta en su propia ventana de consola. Los códigos controlados del
/// proveedor (<c>NOT_SUPPORTED</c>, <c>NOT_INSTALLED</c>,
/// <c>LAUNCH_FAILED</c>...) pasan tal cual a la UI, sin stack traces.
/// </summary>
public sealed class DeveloperLaunchOperation : DeveloperToolPayloadOperation
{
    public DeveloperLaunchOperation(DeveloperToolRegistry registry)
        : base(registry)
    {
    }

    public override string Id => OperationIds.DeveloperLaunch;

    protected override Task<OperationResult> ExecuteForAsync(
        IDeveloperToolProvider provider,
        CancellationToken cancellationToken) =>
        provider.LaunchAsync(cancellationToken);
}
