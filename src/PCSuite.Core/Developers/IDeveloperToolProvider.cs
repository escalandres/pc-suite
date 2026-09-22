using PCSuite.Core.Operations;

namespace PCSuite.Core.Developers;

/// <summary>
/// Proveedor de una herramienta de desarrollador externa (Node, npm, pnpm,
/// Bun, Claude Code...). La Task 004 implementa detección, versión,
/// comprobación/actualización de updates y lanzamiento; las capacidades que
/// una herramienta no soporta devuelven resultados controlados
/// (<c>NOT_SUPPORTED</c>) sin ejecutar nada.
/// </summary>
public interface IDeveloperToolProvider
{
    /// <summary>Identificador estable (p. ej. <c>node</c>, <c>claude-code</c>).</summary>
    string Id { get; }

    /// <summary>Nombre visible (p. ej. <c>Node.js</c>).</summary>
    string Name { get; }

    /// <summary>Detecta la herramienta y reporta su versión y ruta.</summary>
    Task<ToolStatus> GetStatusAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Comprueba contra el registro de origen (npm...) si hay una versión más
    /// reciente. Null si la herramienta no tiene fuente de updates conocida.
    /// </summary>
    Task<UpdateInfo?> CheckUpdateAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Ejecuta el self-update nativo de la herramienta (p. ej.
    /// <c>npm install -g npm@latest</c>). <c>NOT_SUPPORTED</c> si no aplica.
    /// </summary>
    Task<OperationResult> UpdateAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Lanza la herramienta en una ventana interactiva (REPL/TUI).
    /// <c>NOT_SUPPORTED</c> si la herramienta es solo de línea de comandos.
    /// </summary>
    Task<OperationResult> LaunchAsync(CancellationToken cancellationToken);
}
