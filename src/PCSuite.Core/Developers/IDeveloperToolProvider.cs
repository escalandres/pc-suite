using PCSuite.Core.Operations;

namespace PCSuite.Core.Developers;

/// <summary>
/// Proveedor de una herramienta de desarrollador externa (Node, npm, pnpm,
/// Bun, Claude Code...). La Task 004 implementa detección y versión;
/// <see cref="CheckUpdateAsync"/>, <see cref="UpdateAsync"/> y
/// <see cref="LaunchAsync"/> devuelven resultados controlados hasta que esas
/// capacidades se añadan más adelante (blueprint Task 004).
/// </summary>
public interface IDeveloperToolProvider
{
    /// <summary>Identificador estable (p. ej. <c>node</c>, <c>claude-code</c>).</summary>
    string Id { get; }

    /// <summary>Nombre visible (p. ej. <c>Node.js</c>).</summary>
    string Name { get; }

    /// <summary>Detecta la herramienta y reporta su versión y ruta.</summary>
    Task<ToolStatus> GetStatusAsync(CancellationToken cancellationToken);

    /// <summary>Comprueba si hay una actualización disponible (aún no implementado).</summary>
    Task<UpdateInfo?> CheckUpdateAsync(CancellationToken cancellationToken);

    /// <summary>Actualiza la herramienta (aún no implementado).</summary>
    Task<OperationResult> UpdateAsync(CancellationToken cancellationToken);

    /// <summary>Lanza la herramienta (aún no implementado).</summary>
    Task<OperationResult> LaunchAsync(CancellationToken cancellationToken);
}
