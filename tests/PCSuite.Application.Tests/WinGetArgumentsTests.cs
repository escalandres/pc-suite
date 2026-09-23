using PCSuite.Contracts;
using PCSuite.Core.WinGet;
using PCSuite.Infrastructure.WinGet;

namespace PCSuite.Application.Tests;

/// <summary>
/// Argv FIJOS por operación y validación de entrada (§10.1): el frontend
/// nunca envía una línea de comando, solo datos que .NET valida y convierte
/// en arreglos fijos de argumentos, sin shell.
/// </summary>
public sealed class WinGetArgumentsTests
{
    [Fact]
    public void Version_IsJustTheFlag() =>
        Assert.Equal<string>(new[] { "--version" }, WinGetArguments.Version());

    [Fact]
    public void Search_BuildsFixedArgv_WithCount25()
    {
        Assert.Equal<string>(
            new[]
            {
                "search", "visual studio code", "--source", "winget", "--count", "25",
                "--disable-interactivity", "--accept-source-agreements",
            },
            WinGetArguments.Search("visual studio code"));
    }

    [Fact]
    public void Search_TrimsQuery_AndNeverUsesExact()
    {
        var args = WinGetArguments.Search("  git  ");

        Assert.Equal("git", args[1]);
        Assert.DoesNotContain("--exact", args); // búsqueda libre
    }

    [Fact]
    public void List_BuildsFixedArgv() =>
        Assert.Equal<string>(
            new[]
            {
                "list", "--source", "winget", "--disable-interactivity", "--accept-source-agreements",
            },
            WinGetArguments.List());

    [Fact]
    public void Updates_UsesUpgradeCommand_WithoutAll() =>
        Assert.Equal<string>(
            new[]
            {
                "upgrade", "--source", "winget", "--disable-interactivity", "--accept-source-agreements",
            },
            WinGetArguments.Updates());

    [Fact]
    public void Install_BuildsFixedArgv_ExactSilentAgreements()
    {
        Assert.Equal<string>(
            new[]
            {
                "install", "--id", "7zip.7zip", "--exact", "--source", "winget", "--silent",
                "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity",
            },
            WinGetArguments.Install("7zip.7zip"));
    }

    [Fact]
    public void Upgrade_BuildsFixedArgv_ExactSilentAgreements()
    {
        Assert.Equal<string>(
            new[]
            {
                "upgrade", "--id", "Git.Git", "--exact", "--source", "winget", "--silent",
                "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity",
            },
            WinGetArguments.Upgrade("Git.Git"));
    }

    [Fact]
    public void UpgradeAll_BuildsFixedArgv_WithoutId()
    {
        Assert.Equal<string>(
            new[]
            {
                "upgrade", "--all", "--source", "winget", "--silent",
                "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity",
            },
            WinGetArguments.UpgradeAll());
    }

    [Fact]
    public void Uninstall_OmitsPackageAgreementsFlag()
    {
        var args = WinGetArguments.Uninstall("7zip.7zip");

        Assert.Equal<string>(
            new[]
            {
                "uninstall", "--id", "7zip.7zip", "--exact", "--source", "winget", "--silent",
                "--disable-interactivity", "--accept-source-agreements",
            },
            args);

        // El flag no existe en uninstall: pasarlo sería un error del CLI.
        Assert.DoesNotContain("--accept-package-agreements", args);
    }

    [Fact]
    public void Install_InvalidId_ThrowsBeforeBuildingAnyArgv() =>
        Assert.Throws<WinGetException>(() => WinGetArguments.Install("id malo"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("-Partido")]
    [InlineData("tiene espacios")]
    [InlineData("inyeccion;rm")]
    [InlineData("con\0caracter de control")]
    public void ValidatePackageId_Invalid_ThrowsMalformed(string? packageId)
    {
        var exception = Assert.Throws<WinGetException>(
            () => WinGetArguments.ValidatePackageId(packageId));

        Assert.Equal(BridgeErrorCodes.MalformedMessage, exception.ErrorCode);
        Assert.DoesNotContain("   at ", exception.ErrorMessage);
    }

    [Fact]
    public void ValidatePackageId_TooLong_ThrowsMalformed()
    {
        var exception = Assert.Throws<WinGetException>(
            () => WinGetArguments.ValidatePackageId(new string('a', 129)));

        Assert.Equal(BridgeErrorCodes.MalformedMessage, exception.ErrorCode);
    }

    [Theory]
    [InlineData("7zip.7zip")]
    [InlineData("Microsoft.VisualStudioCode")]
    [InlineData("pkg_1+x-y.z")]
    public void ValidatePackageId_Valid_ReturnsSameId(string packageId) =>
        Assert.Equal(packageId, WinGetArguments.ValidatePackageId(packageId));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-flag")]
    [InlineData("-otra bandera")]
    [InlineData("con\ttabulacion")]
    public void ValidateQuery_Invalid_ThrowsMalformed(string? query)
    {
        var exception = Assert.Throws<WinGetException>(
            () => WinGetArguments.ValidateQuery(query));

        Assert.Equal(BridgeErrorCodes.MalformedMessage, exception.ErrorCode);
    }

    [Fact]
    public void ValidateQuery_TooLong_ThrowsMalformed()
    {
        var exception = Assert.Throws<WinGetException>(
            () => WinGetArguments.ValidateQuery(new string('x', 129)));

        Assert.Equal(BridgeErrorCodes.MalformedMessage, exception.ErrorCode);
    }

    [Fact]
    public void ValidateQuery_Trims() =>
        Assert.Equal("visual studio", WinGetArguments.ValidateQuery("  visual studio  "));
}
