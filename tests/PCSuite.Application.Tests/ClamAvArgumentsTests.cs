using PCSuite.Infrastructure.ClamAV;

namespace PCSuite.Application.Tests;

/// <summary>argv fijos de ClamAV (§10.1/§53): banderas siempre delante, sin shell.</summary>
public sealed class ClamAvArgumentsTests
{
    [Fact]
    public void VersionAndFreshclam_AreFixedArgv()
    {
        Assert.Equal<string>(["--version"], ClamAvArguments.Version);
        Assert.Empty(ClamAvArguments.Freshclam); // freshclam usa WorkingDirectory, sin argumentos
    }

    [Fact]
    public void QuickScan_FlagsFirst_RootedTargets_NoDuplicates()
    {
        var args = ClamAvArguments.QuickScan();

        Assert.Equal("--recursive", args[0]);
        Assert.Equal("--verbose", args[1]);
        // Mínimo: escritorio + temp (Descargas se suma si la Known Folder resuelve).
        Assert.True(args.Length >= 4);
        Assert.Contains(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), args);
        Assert.Contains(Path.GetTempPath(), args);
        Assert.All(args[2..], target => Assert.True(Path.IsPathRooted(target)));
        Assert.Equal(args.Length, args.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void FullScan_AppendsGivenDriveRoots_AfterFlags()
    {
        var args = ClamAvArguments.FullScan(["C:\\", "D:\\"]);

        Assert.Equal<string>(["--recursive", "C:\\", "D:\\"], args);
    }

    [Fact]
    public void CustomScan_KeepsPathAsSingleLastElement()
    {
        // Un SOLO elemento de argv: con metacaracteres de shell incluidos no hay
        // shell que lo parta ni opciones que inyectar (la ruta ya viene validada).
        var args = ClamAvArguments.CustomScan(@"C:\safe\file & calc.exe");

        Assert.Equal(3, args.Length);
        Assert.Equal("--recursive", args[0]);
        Assert.Equal("--verbose", args[1]);
        Assert.Equal(@"C:\safe\file & calc.exe", args[2]);
    }

    [Fact]
    public void WithScanLog_AppendsLogAsLastElement()
    {
        // 2026-10-18: los escaneos van elevados (runas) — sin pipes, el
        // reporte vive en el --log que el padre taila en vivo.
        var args = ClamAvArguments.WithScanLog(
            ["--recursive", @"C:\target"],
            @"C:\Users\ana\AppData\Local\Temp\scan.log");

        Assert.Equal(3, args.Length);
        Assert.Equal("--recursive", args[0]);
        Assert.Equal(@"C:\target", args[1]); // los objetivos NO se tocan
        Assert.Equal(@"--log=C:\Users\ana\AppData\Local\Temp\scan.log", args[^1]);
    }
}
