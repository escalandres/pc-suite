using PCSuite.Application.Operations;
using PCSuite.Core.Plugins;
using PCSuite.Contracts;
using PCSuite.Core.Operations;
using System.Text.Json;

namespace PCSuite.Application.Operations;

/// <summary>
/// Operación puente "plugin.list": devuelve la lista de plugins instalados.
/// </summary>
public sealed class PluginListOperation
{
    private readonly IPluginService _pluginService;

    /// <summary>Constructor que recibe el servicio inyectado.</summary>
    public PluginListOperation(IPluginService pluginService) => _pluginService = pluginService ?? throw new ArgumentNullException(nameof(pluginService));

    /// <inheritdoc />
    public string Id => OperationIds.PluginList;

    /// <inheritdoc />
    public bool RequiresElevation => false;

    /// <inheritdoc />
    public async Task<OperationResult> ExecuteAsync(object? payload, CancellationToken cancellationToken = default)
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