using PCSuite.Core.Processes;

namespace PCSuite.Infrastructure.Developers;

/// <summary>
/// Claude Code: detección (<c>claude --version</c>), comprobación de
/// actualización, autoactualización vía <c>claude update</c> y lanzamiento
/// de su TUI en una ventana de consola (§9: sin emular la terminal).
/// </summary>
public sealed class ClaudeCodeProvider : DeveloperToolProviderBase
{
    public ClaudeCodeProvider(IProcessRunner runner, ExecutableLocator locator)
        : base(runner, locator)
    {
    }

    public override string Id => "claude-code";

    public override string Name => "Claude Code";

    protected override string ExecutableName => "claude";

    // Última versión: el paquete @anthropic-ai/claude-code del registro npm
    // sigue la misma línea de releases (verificado). Sin npm → null.
    protected override ProcessRequest? LatestVersionRequest =>
        LatestVersion("npm", "view", "@anthropic-ai/claude-code", "version");

    // Verificado empíricamente: "Check for updates and install if available".
    protected override ProcessRequest? SelfUpdateRequest =>
        SelfUpdate("claude", "update");

    // §9: la TUI de Claude Code en su propia consola; no se emula un terminal.
    protected override bool SupportsLaunch => true;
}
