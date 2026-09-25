using PCSuite.Core.WinGet;

namespace PCSuite.Application.Tests;

/// <summary>
/// <see cref="IWinGetService"/> en memoria: los tests de ClamAV (Task 008) solo
/// usan la instalación; el resto lanza para detectar usos inesperados.
/// </summary>
internal sealed class FakeWinGetService : IWinGetService
{
    /// <summary>Outcome que devuelve <c>InstallAsync</c>.</summary>
    public WinGetMutationOutcome InstallOutcome { get; set; } =
        new("Cisco.ClamAV", true, 0, false, null);

    /// <summary>Si no es null, <c>InstallAsync</c> lanza esta excepción.</summary>
    public WinGetException? InstallException { get; set; }

    /// <summary>Ids recibidos, en orden.</summary>
    public List<string> InstallCalls { get; } = [];

    public Task<WinGetMutationOutcome> InstallAsync(string packageId, CancellationToken cancellationToken = default)
    {
        InstallCalls.Add(packageId);
        return InstallException is null
            ? Task.FromResult(InstallOutcome)
            : Task.FromException<WinGetMutationOutcome>(InstallException);
    }

    public Task<WinGetDetection> DetectAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<WinGetPackage>> SearchAsync(string query, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<WinGetPackage>> ListAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<WinGetPackage>> UpdatesAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<WinGetMutationOutcome> UpgradeAsync(string packageId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<WinGetMutationOutcome> UpgradeAllAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<WinGetMutationOutcome> UninstallAsync(string packageId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
