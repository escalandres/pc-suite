using PCSuite.Application.Operations;
using PCSuite.Core.Plugins;
using PCSuite.Contracts;
using PCSuite.Core.Operations;
using System.Text.Json;

namespace PCSuite.Application.Operations;

public sealed class PluginListOperation : IOperation
{
    private readonly IPluginService _pluginService;

    public PluginListOperation(IPluginService pluginService) => _pluginService = pluginService ?? throw new ArgumentNullException(nameof(pluginService));

    public string Id => OperationIds.PluginList;

    public bool RequiresElevation => false;

    public async Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken cancellationToken = default)
    {
        var plugins = await Task.FromResult(_pluginService.ListInstalledPlugins());
        var result = new PluginCollectionResult
        {
            Plugins = plugins.ToList(),
            ScannedAt = DateTimeOffset.UtcNow
        };
        var json = JsonSerializer.Serialize(result, BridgeJson.Options);
        return OperationResult.Ok(JsonDocument.Parse(json).RootElement);
    }
}
