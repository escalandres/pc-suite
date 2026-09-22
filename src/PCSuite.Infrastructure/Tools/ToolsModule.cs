using PCSuite.Core.Tools;

namespace PCSuite.Infrastructure.Tools;

/// <summary>Módulo con las tools de utilería general de PCSuite.</summary>
public sealed class ToolsModule : IModule
{
    public const string IdValue = "tools";

    public string Id => IdValue;

    public string Name => "Herramientas";

    public IReadOnlyCollection<ITool> Tools { get; } =
    [
        new JsonFormatterTool(),
        new UuidGeneratorTool(),
        new Base64Tool(),
        new HashTool(),
        new PasswordGeneratorTool(),
    ];
}
