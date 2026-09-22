using PCSuite.Infrastructure.Developers;

namespace PCSuite.Application.Tests;

/// <summary>
/// Pruebas del localizador de ejecutables: búsqueda en la PATH, orden de
/// extensiones y robustez ante entradas reales (comillas, vacíos, variables).
/// </summary>
public sealed class ExecutableLocatorTests
{
    [Theory]
    [InlineData(".exe")]
    [InlineData(".com")]
    [InlineData(".cmd")]
    [InlineData(".bat")]
    public void Find_SupportedExecutableExtensions_ReturnsFullPath(string extension)
    {
        using var temp = new TempExecutablePath();
        var expected = temp.Add($"tool{extension}");
        var locator = new ExecutableLocator(temp.DirectoryPath);

        Assert.Equal(expected, locator.Find("tool"));
    }

    [Fact]
    public void Find_ExtensionlessShim_IsIgnored()
    {
        // Como el pnpm de Corepack: script POSIX sin extensión, no ejecutable en Windows.
        using var temp = new TempExecutablePath();
        temp.Add("pnpm");
        var locator = new ExecutableLocator(temp.DirectoryPath);

        Assert.Null(locator.Find("pnpm"));
    }

    [Fact]
    public void Find_ExecutableExtensionWinsOverExtensionlessShim()
    {
        using var temp = new TempExecutablePath();
        temp.Add("pnpm");
        var expected = temp.Add("pnpm.CMD");
        var locator = new ExecutableLocator(temp.DirectoryPath);

        // NTFS no distingue mayúsculas: el casing retornado puede diferir del real.
        Assert.Equal(expected, locator.Find("pnpm"), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Find_PrefersExecutableOverCommandScript_InSameDirectory()
    {
        using var temp = new TempExecutablePath();
        var expected = temp.Add("tool.exe");
        temp.Add("tool.cmd");
        var locator = new ExecutableLocator(temp.DirectoryPath);

        Assert.Equal(expected, locator.Find("tool"));
    }

    [Fact]
    public void Find_MissingExecutable_ReturnsNull()
    {
        using var temp = new TempExecutablePath();
        var locator = new ExecutableLocator(temp.DirectoryPath);

        Assert.Null(locator.Find("no-existe"));
    }

    [Fact]
    public void Find_FirstDirectoryOnPathWins()
    {
        using var first = new TempExecutablePath();
        using var second = new TempExecutablePath();
        var expected = first.Add("bun.exe");
        second.Add("bun.exe");
        var pathVariable = first.DirectoryPath + Path.PathSeparator + second.DirectoryPath;
        var locator = new ExecutableLocator(pathVariable);

        Assert.Equal(expected, locator.Find("bun"));
    }

    [Fact]
    public void Find_HandlesQuotedAndEmptyEntries()
    {
        using var temp = new TempExecutablePath();
        var expected = temp.Add("claude.exe");
        var locator = new ExecutableLocator($"\"{temp.DirectoryPath}\";; ");

        Assert.Equal(expected, locator.Find("claude"));
    }

    [Fact]
    public void Find_ExpandsEnvironmentVariablesInPath()
    {
        using var temp = new TempExecutablePath();
        var expected = temp.Add("node.exe");
        Environment.SetEnvironmentVariable("PCSUITE_TEST_PATH", temp.DirectoryPath);
        try
        {
            var locator = new ExecutableLocator("%PCSUITE_TEST_PATH%");

            Assert.Equal(expected, locator.Find("node"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PCSUITE_TEST_PATH", null);
        }
    }
}
