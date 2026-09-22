using PCSuite.Core.Processes;

namespace PCSuite.Infrastructure.Developers;

/// <summary>
/// Bun: detección (<c>bun --version</c>), comprobación de actualización y
/// autoactualización vía <c>bun upgrade</c>. Sin interfaz interactiva propia:
/// no es lanzable.
/// </summary>
public sealed class BunProvider : DeveloperToolProviderBase
{
    public BunProvider(IProcessRunner runner, ExecutableLocator locator)
        : base(runner, locator)
    {
    }

    public override string Id => "bun";

    public override string Name => "Bun";

    protected override string ExecutableName => "bun";

    // Bun no expone consulta de última versión: se usa el registro npm, que
    // publica el binario oficial (verificado). Sin npm → null.
    protected override ProcessRequest? LatestVersionRequest =>
        LatestVersion("npm", "view", "bun", "version");

    // Verificado empíricamente: "bun upgrade" (sin flags de comprobación).
    protected override ProcessRequest? SelfUpdateRequest =>
        SelfUpdate("bun", "upgrade");
}
