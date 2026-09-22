using PCSuite.Core.Processes;

namespace PCSuite.Infrastructure.Developers;

/// <summary>
/// npm: detección (<c>npm --version</c>), comprobación de actualización y
/// autoactualización vía <c>npm install -g npm@latest</c>. Sin interfaz
/// interactiva propia: no es lanzable.
/// </summary>
public sealed class NpmProvider : DeveloperToolProviderBase
{
    public NpmProvider(IProcessRunner runner, ExecutableLocator locator)
        : base(runner, locator)
    {
    }

    public override string Id => "npm";

    public override string Name => "npm";

    protected override string ExecutableName => "npm";

    protected override ProcessRequest? LatestVersionRequest =>
        LatestVersion("npm", "view", "npm", "version");

    // Si npm vive en Program Files, esto falla con EACCES/EACCES de permisos y
    // UpdateAsync lo traduce a ELEVATION_REQUIRED (elevación: Task 005).
    protected override ProcessRequest? SelfUpdateRequest =>
        SelfUpdate("npm", "install", "-g", "npm@latest");
}
