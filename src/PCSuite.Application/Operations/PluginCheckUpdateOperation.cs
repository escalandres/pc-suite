using PCSuite.Application.Operations;
using PCSuite.Core.Plugins;
using PCSuite.Contracts;
using PCSuite.Core.Operations;
using System.Text.Json;

namespace PCSuite.Application.Operations;

public sealed class PluginCheckUpdateOperation : IOperation
{
    private readonly IPluginService _pluginService;

    public PluginCheckUpdateOperation(IPluginService pluginService) => _pluginService = pluginService ?? throw new ArgumentNullException(nameof(pluginService));

    public string Id => OperationIds.PluginCheckUpdate;

    public bool RequiresElevation => false;

    public async Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken cancellationToken = default)
    {
        PluginCheckUpdatePayload? payload = null;
        if (context.Payload.HasValue)
        {
            try { payload = context.Payload.Value.Deserialize<PluginCheckUpdatePayload>(BridgeJson.Options); }
            catch { payload = null; }
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.PluginId))
        {
            return OperationResult.Fail("error", "El parámetro 'pluginId' es obligatorio.");
        }

        var result = await Task.FromResult(_pluginService.CheckUpdate(payload.PluginId));
        var json = JsonSerializer.Serialize(result, BridgeJson.Options);
        return OperationResult.Ok(JsonDocument.Parse(json).RootElement);
    }
}
