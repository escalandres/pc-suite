using PCSuite.Core.Tools;

namespace PCSuite.Application.Tools;

/// <summary>
/// Registro central de módulos y tools. Es la única fuente de verdad para
/// descubrir tools: la UI las lista/búscas desde aquí vía metadatos, nunca
/// las tiene hardcodeadas.
/// </summary>
public sealed class ToolRegistry
{
    private readonly Dictionary<string, IModule> _modules = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ITool> _tools = new(StringComparer.Ordinal);

    public IReadOnlyCollection<IModule> Modules => _modules.Values;

    public IReadOnlyCollection<ITool> Tools => _tools.Values;

    /// <summary>
    /// Registra un módulo y sus tools. Falla en arranque si hay ids
    /// duplicados (error de configuración, no del usuario).
    /// </summary>
    public void RegisterModule(IModule module)
    {
        ArgumentNullException.ThrowIfNull(module);

        // Validación completa antes de mutar: un fallo no deja el registro a medias.
        if (_modules.ContainsKey(module.Id))
        {
            throw new InvalidOperationException($"Duplicate module id '{module.Id}'.");
        }

        foreach (var tool in module.Tools)
        {
            if (_tools.ContainsKey(tool.Metadata.Id))
            {
                throw new InvalidOperationException($"Duplicate tool id '{tool.Metadata.Id}'.");
            }
        }

        _modules.Add(module.Id, module);
        foreach (var tool in module.Tools)
        {
            _tools.Add(tool.Metadata.Id, tool);
        }
    }

    /// <summary>Devuelve la tool por id o null si no existe.</summary>
    public ITool? GetTool(string? toolId) =>
        !string.IsNullOrEmpty(toolId) && _tools.TryGetValue(toolId, out var tool) ? tool : null;
}
