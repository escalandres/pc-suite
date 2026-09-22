using PCSuite.Application.Developers;
using PCSuite.Contracts;
using PCSuite.Core.Developers;
using PCSuite.Core.Operations;

namespace PCSuite.Infrastructure.Operations;

/// <summary>
/// developer.checkUpdate — comprueba contra la fuente de updates de cada
/// herramienta si hay una versión más reciente. Una comprobación degradada
/// (sin red, sin fuente, no instalada) responde con <c>supported=false</c> en
/// vez de fallar: la UI decide qué mostrar.
/// </summary>
public sealed class DeveloperCheckUpdateOperation : DeveloperToolPayloadOperation
{
    public DeveloperCheckUpdateOperation(DeveloperToolRegistry registry)
        : base(registry)
    {
    }

    public override string Id => OperationIds.DeveloperCheckUpdate;

    protected override async Task<OperationResult> ExecuteForAsync(
        IDeveloperToolProvider provider,
        CancellationToken cancellationToken)
    {
        var status = await provider.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        var update = await provider.CheckUpdateAsync(cancellationToken).ConfigureAwait(false);

        var result = new UpdateCheckResult
        {
            ToolId = provider.Id,
            CurrentVersion = update?.CurrentVersion ?? (status.IsInstalled ? status.Version : null),
            LatestVersion = update?.LatestVersion,
            UpdateAvailable = update?.UpdateAvailable ?? false,
            Supported = update is not null,
        };

        return OperationResult.Ok(BridgeJson.ToJson(result));
    }
}
