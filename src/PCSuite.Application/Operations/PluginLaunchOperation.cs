using PCSuite.Application.Operations;
using PCSuite.Core.Plugins;
using PCSuite.Contracts;
using PCSuite.Core.Operations;
using System.Text.Json;

namespace PCSuite.Application.Operations;

/// <summary>
/// Operación puente "plugin.launch": lanza (ejecuta) un plugin identificado por su id.
/// </summary>
public sealed class PluginLaunchOperation
{
    private readonly IPluginService _pluginService;

    /// <summary>Constructor que recibe el servicio inyectado.</summary>
    public PluginLaunchOperation(IPluginService pluginService) => _pluginService = pluginService ?? throw new ArgumentNullException(nameof(pluginService));

    /// <inheritdoc />
    public string Id => OperationIds.PluginLaunch;

    /// <inheritdoc />
    public bool RequiresElevation => false;

    /// <inheritdoc />
    public async Task<OperationResult> ExecuteAsync(PluginLaunchPayload payload, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(payload?.PluginId))
        {
            return OperationResult.Fail("error", "El parámetro 'pluginId' es obligatorio.");
        }

        var result = await Task.FromResult(_pluginService.Launch(payload.PluginId));
        var json = JsonSerializer.Serialize(result, BridgeJson.Options);
        return OperationResult.Ok(JsonDocument.Parse(json).RootElement);
    }
}