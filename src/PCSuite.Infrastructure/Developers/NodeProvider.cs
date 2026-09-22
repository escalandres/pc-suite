using PCSuite.Core.Processes;

namespace PCSuite.Infrastructure.Developers;

/// <summary>
/// Node.js: detección (<c>node --version</c>), comprobación de actualización,
/// autoactualización (no soportada: llega con WinGet) y lanzamiento del REPL.
/// </summary>
public sealed class NodeProvider : DeveloperToolProviderBase
{
    public NodeProvider(IProcessRunner runner, ExecutableLocator locator)
        : base(runner, locator)
    {
    }

    public override string Id => "node";

    public override string Name => "Node.js";

    protected override string ExecutableName => "node";

    // Última versión: el paquete "node" del registro npm replica los releases
    // oficiales (verificado empíricamente en la Task 004). Sin npm → null.
    protected override ProcessRequest? LatestVersionRequest =>
        LatestVersion("npm", "view", "node", "version");

    protected override string UpdateNotSupportedMessage =>
        "Node.js no tiene autoactualización propia: la actualización llegará con WinGet (Task 006).";

    // Interactiva: el REPL de Node en su propia ventana de consola.
    protected override bool SupportsLaunch => true;
}
