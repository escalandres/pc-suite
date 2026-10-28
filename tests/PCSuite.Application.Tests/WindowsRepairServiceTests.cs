using PCSuite.Contracts;
using PCSuite.Core.Processes;
using PCSuite.Core.Windows;
using PCSuite.Infrastructure.Windows;

namespace PCSuite.Application.Tests;

/// <summary>
/// WindowsRepairService (Task 036): la petición de proceso es SIEMPRE
/// elevada, sin tope de reloj (lección 024) y con el argv FIJO del catálogo;
/// ids desconocidos se rechazan SIN tocar el runner (cero UAC); los exit
/// codes se traducen a mensajes controlados con puntero al log oficial.
/// </summary>
public sealed class WindowsRepairServiceTests
{
    [Fact]
    public async Task Run_UnknownId_ThrowsControlledWithoutTouchingTheRunner()
    {
        var runner = FakeProcessRunner.Returning(string.Empty, exitCode: 0);
        var service = new WindowsRepairService(runner);

        var exception = await Assert.ThrowsAsync<WindowsRepairException>(
            () => service.RunAsync("comando-inventado"));

        Assert.Equal(BridgeErrorCodes.WindowsFailed, exception.ErrorCode);
        Assert.Empty(runner.Requests); // jamás llega a UAC ni a proceso
    }

    [Fact]
    public async Task Run_BuildsTheFixedElevatedRequest()
    {
        var runner = FakeProcessRunner.Returning(string.Empty, exitCode: 0);
        var service = new WindowsRepairService(runner);

        await service.RunAsync(WindowsCommandCatalog.SfcScannow);

        var request = Assert.Single(runner.Requests);
        Assert.EndsWith("sfc.exe", request.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { "/scannow" }, request.Arguments);
        Assert.True(request.RequiresElevation);
        Assert.Null(request.Timeout); // 024: sfc/DISM pueden tardar horas
    }

    [Fact]
    public async Task Run_DismRestoreHealth_UsesTheFixedDismArguments()
    {
        var runner = FakeProcessRunner.Returning(string.Empty, exitCode: 0);
        var service = new WindowsRepairService(runner);

        await service.RunAsync(WindowsCommandCatalog.DismRestoreHealth);

        var request = Assert.Single(runner.Requests);
        Assert.EndsWith("Dism.exe", request.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            new[] { "/Online", "/Cleanup-Image", "/RestoreHealth" },
            request.Arguments);
    }

    [Theory]
    [InlineData(0, "no encontró violaciones")]
    [InlineData(1, "reparó archivos dañados")]
    [InlineData(2, "no pudo reparar")]
    public async Task Run_SfcScannow_TranslatesOfficialExitCodes(int exitCode, string expectedFragment)
    {
        var service = new WindowsRepairService(FakeProcessRunner.Returning(string.Empty, exitCode));

        var outcome = await service.RunAsync(WindowsCommandCatalog.SfcScannow);

        Assert.Equal(exitCode == 0, outcome.Succeeded);
        Assert.Equal(exitCode, outcome.ExitCode);
        Assert.Contains(expectedFragment, outcome.Message);
        Assert.Contains("CBS.log", outcome.LogHint);
    }

    [Fact]
    public async Task Run_DismRestoreHealth_FailurePointsToTheLog()
    {
        var service = new WindowsRepairService(FakeProcessRunner.Returning(string.Empty, exitCode: 87));

        var outcome = await service.RunAsync(WindowsCommandCatalog.DismRestoreHealth);

        Assert.False(outcome.Succeeded);
        Assert.Contains("dism.log", outcome.LogHint);
        Assert.Contains("87", outcome.Message);
    }

    [Fact]
    public async Task Run_ElevationDeclined_ThrowsControlled()
    {
        var service = new WindowsRepairService(
            FakeProcessRunner.Throwing(new ElevationDeclinedException()));

        var exception = await Assert.ThrowsAsync<WindowsRepairException>(
            () => service.RunAsync(WindowsCommandCatalog.DismCheckHealth));

        Assert.Equal(BridgeErrorCodes.ElevationCancelled, exception.ErrorCode);
        Assert.Contains("canceló el control de cuentas", exception.Message);
    }

    [Fact]
    public async Task Run_UnexpectedProcessFailure_ThrowsControlled()
    {
        var service = new WindowsRepairService(
            FakeProcessRunner.Throwing(new InvalidOperationException("boom")));

        var exception = await Assert.ThrowsAsync<WindowsRepairException>(
            () => service.RunAsync(WindowsCommandCatalog.DismCheckHealth));

        Assert.Equal(BridgeErrorCodes.WindowsFailed, exception.ErrorCode);
        Assert.DoesNotContain("boom", exception.Message); // sin detalles internos
    }

    [Fact]
    public async Task Run_Cancellation_PropagatesForTheJobToMark()
    {
        var service = new WindowsRepairService(
            FakeProcessRunner.Throwing(new OperationCanceledException()));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.RunAsync(WindowsCommandCatalog.DismCheckHealth));
    }

    [Fact]
    public void List_ReturnsTheCatalogAsIs()
    {
        var service = new WindowsRepairService(FakeProcessRunner.Returning(string.Empty));

        var commands = service.List();

        Assert.Equal(WindowsCommandCatalog.All, commands);
    }
}
