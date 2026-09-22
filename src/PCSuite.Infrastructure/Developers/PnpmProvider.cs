using PCSuite.Core.Processes;

namespace PCSuite.Infrastructure.Developers;

/// <summary>
/// pnpm: detección (<c>pnpm --version</c>), comprobación de actualización y
/// autoactualización vía <c>pnpm self-update</c>. Sin interfaz interactiva
/// propia: no es lanzable.
/// </summary>
public sealed class PnpmProvider : DeveloperToolProviderBase
{
    public PnpmProvider(IProcessRunner runner, ExecutableLocator locator)
        : base(runner, locator)
    {
    }

    public override string Id => "pnpm";

    public override string Name => "pnpm";

    protected override string ExecutableName => "pnpm";

    // pnpm incluye comandos compatibles con npm (verificado: "pnpm view").
    protected override ProcessRequest? LatestVersionRequest =>
        LatestVersion("pnpm", "view", "pnpm", "version");

    // Verificado empíricamente: "Updates pnpm to the latest version".
    protected override ProcessRequest? SelfUpdateRequest =>
        SelfUpdate("pnpm", "self-update");
}
