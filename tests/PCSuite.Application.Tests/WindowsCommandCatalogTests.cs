using PCSuite.Core.Windows;
using PCSuite.Infrastructure.Windows;

namespace PCSuite.Application.Tests;

/// <summary>
/// El catálogo de comandos (Task 036) es la allow-list del módulo: ids
/// únicos, binarios reales de System32, argv fijos no vacíos y datos de
/// UI completos. Un comando futuro que rompa cualquiera de estas reglas
/// revienta aquí, no en la cara del usuario.
/// </summary>
public sealed class WindowsCommandCatalogTests
{
    [Fact]
    public void All_IdsAreUnique()
    {
        var ids = WindowsCommandCatalog.All.Select(command => command.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void All_LiveUnderSystem32()
    {
        foreach (var command in WindowsCommandCatalog.All)
        {
            Assert.StartsWith(
                Environment.SystemDirectory,
                command.FileName,
                StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(command.FileName), $"No existe: {command.FileName}");
        }
    }

    [Fact]
    public void All_ArgumentsAreFixedAndNonEmpty()
    {
        foreach (var command in WindowsCommandCatalog.All)
        {
            Assert.NotEmpty(command.Arguments);
            Assert.All(command.Arguments, argument => Assert.False(string.IsNullOrWhiteSpace(argument)));
        }
    }

    [Fact]
    public void All_HaveCatalogMetadataForTheUi()
    {
        foreach (var command in WindowsCommandCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(command.Name));
            Assert.False(string.IsNullOrWhiteSpace(command.Description));
            Assert.False(string.IsNullOrWhiteSpace(command.Category));
            Assert.False(string.IsNullOrWhiteSpace(command.DisplayCommand));
            Assert.False(string.IsNullOrWhiteSpace(command.DurationHint));
            Assert.False(string.IsNullOrWhiteSpace(command.LogPath));
        }
    }

    [Fact]
    public void Find_UnknownId_ReturnsNull()
    {
        Assert.Null(WindowsCommandCatalog.Find("no-existe"));
        Assert.Null(WindowsCommandCatalog.Find(string.Empty));
        Assert.Null(WindowsCommandCatalog.Find("sfc-scannow; del /f"));
    }

    [Fact]
    public void All_ContainsTheFiveSeedCommands()
    {
        var ids = WindowsCommandCatalog.All.Select(command => command.Id).ToHashSet();
        Assert.Superset(
            new HashSet<string>
            {
                WindowsCommandCatalog.SfcScannow,
                WindowsCommandCatalog.SfcVerifyOnly,
                WindowsCommandCatalog.DismCheckHealth,
                WindowsCommandCatalog.DismScanHealth,
                WindowsCommandCatalog.DismRestoreHealth,
            },
            ids);
    }
}
