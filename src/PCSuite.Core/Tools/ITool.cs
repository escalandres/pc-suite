using System.Text.Json;

namespace PCSuite.Core.Tools;

/// <summary>
/// Una herramienta descubrible por metadatos y ejecutable con un payload
/// tipado. Añadir una tool no requiere modificar módulos relacionados
/// (Rule 6 — Preserve modularity): implementar <see cref="ITool"/> y
/// registrar su módulo en el <c>ToolRegistry</c>.
/// </summary>
public interface ITool
{
    ToolMetadata Metadata { get; }

    Task<ToolResult> ExecuteAsync(
        ToolContext context,
        CancellationToken cancellationToken);
}
