using PCSuite.Core.Developers;
using PCSuite.Core.Processes;
using PCSuite.Infrastructure.Developers;

namespace PCSuite.Application.Tests;

/// <summary>
/// Pruebas de los proveedores de herramientas de desarrollador: detección,
/// versión y ruta, con la PATH apuntando a una carpeta temporal y un runner
/// falso — sin depender del software instalado en la máquina.
/// </summary>
public sealed class DeveloperToolProviderTests
{
    public static TheoryData<string> ProviderIds => new()
    {
        { "node" },
        { "npm" },
        { "pnpm" },
        { "bun" },
        { "claude-code" },
    };

    public static TheoryData<string, string> ProviderIdNames => new()
    {
        { "node", "Node.js" },
        { "npm", "npm" },
        { "pnpm", "pnpm" },
        { "bun", "Bun" },
        { "claude-code", "Claude Code" },
    };

    public static TheoryData<string, string, string> Providers => new()
    {
        { "node", "Node.js", "node.exe" },
        { "npm", "npm", "npm.cmd" },
        { "pnpm", "pnpm", "pnpm.CMD" },
        { "bun", "Bun", "bun.CMD" },
        { "claude-code", "Claude Code", "claude.exe" },
    };

    private static IDeveloperToolProvider Create(
        string id,
        IProcessRunner runner,
        ExecutableLocator locator) => id switch
    {
        "node" => new NodeProvider(runner, locator),
        "npm" => new NpmProvider(runner, locator),
        "pnpm" => new PnpmProvider(runner, locator),
        "bun" => new BunProvider(runner, locator),
        "claude-code" => new ClaudeCodeProvider(runner, locator),
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Proveedor desconocido."),
    };

    [Theory]
    [MemberData(nameof(ProviderIds))]
    public async Task GetStatus_NotInstalled_ReturnsNotInstalled_WithoutExecutingAnything(string id)
    {
        using var temp = new TempExecutablePath();
        var runner = FakeProcessRunner.Throwing(new InvalidOperationException("No debe ejecutarse."));
        var provider = Create(id, runner, new ExecutableLocator(temp.DirectoryPath));

        var status = await provider.GetStatusAsync(CancellationToken.None);

        Assert.False(status.IsInstalled);
        Assert.Null(status.Version);
        Assert.Null(status.Path);
        Assert.Empty(runner.Requests);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task GetStatus_Installed_ReportsVersionAndPath_ViaVersionFlag(
        string id,
        string name,
        string executableFile)
    {
        using var temp = new TempExecutablePath();
        var expectedPath = temp.Add(executableFile);
        var runner = FakeProcessRunner.Returning("v1.2.3\n");
        var provider = Create(id, runner, new ExecutableLocator(temp.DirectoryPath));

        var status = await provider.GetStatusAsync(CancellationToken.None);

        Assert.Equal(id, provider.Id);
        Assert.Equal(name, provider.Name);
        Assert.True(status.IsInstalled);
        Assert.Equal("1.2.3", status.Version);

        // NTFS no distingue mayúsculas: el casing retornado puede diferir del real.
        Assert.Equal(expectedPath, status.Path, StringComparer.OrdinalIgnoreCase);

        var request = Assert.Single(runner.Requests);
        Assert.Equal(expectedPath, request.FileName, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(new[] { "--version" }, request.Arguments);
        Assert.True(request.Timeout.HasValue, "La consulta de versión debe tener timeout.");
    }

    [Theory]
    [MemberData(nameof(ProviderIds))]
    public async Task CheckUpdateAsync_ReturnsNull_WithoutExecutingAnything(string id)
    {
        var runner = FakeProcessRunner.Throwing(new InvalidOperationException("No debe ejecutarse."));
        var provider = Create(id, runner, new ExecutableLocator(string.Empty));

        var update = await provider.CheckUpdateAsync(CancellationToken.None);

        Assert.Null(update);
        Assert.Empty(runner.Requests);
    }

    [Theory]
    [MemberData(nameof(ProviderIdNames))]
    public async Task UpdateAndLaunchAsync_ReturnControlledNotSupportedFailures(string id, string name)
    {
        var runner = FakeProcessRunner.Throwing(new InvalidOperationException("No debe ejecutarse."));
        var provider = Create(id, runner, new ExecutableLocator(string.Empty));

        foreach (var result in new[]
        {
            await provider.UpdateAsync(CancellationToken.None),
            await provider.LaunchAsync(CancellationToken.None),
        })
        {
            Assert.False(result.Success);
            Assert.Equal("NOT_SUPPORTED", result.ErrorCode);
            Assert.Contains(name, result.ErrorMessage);
            Assert.DoesNotContain("   at ", result.ErrorMessage);
        }

        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task GetStatus_Timeout_ReturnsInstalledWithoutVersion()
    {
        using var temp = new TempExecutablePath();
        temp.Add("node.exe");
        var runner = FakeProcessRunner.Throwing(new ProcessTimeoutException(TimeSpan.FromSeconds(10)));
        var provider = new NodeProvider(runner, new ExecutableLocator(temp.DirectoryPath));

        var status = await provider.GetStatusAsync(CancellationToken.None);

        Assert.True(status.IsInstalled);
        Assert.Null(status.Version);
        Assert.NotNull(status.Path);
    }

    [Fact]
    public async Task GetStatus_NonZeroExit_ReturnsInstalledWithoutVersion()
    {
        using var temp = new TempExecutablePath();
        temp.Add("node.exe");
        var runner = FakeProcessRunner.Returning("algo fue mal", exitCode: 1);
        var provider = new NodeProvider(runner, new ExecutableLocator(temp.DirectoryPath));

        var status = await provider.GetStatusAsync(CancellationToken.None);

        Assert.True(status.IsInstalled);
        Assert.Null(status.Version);
    }

    [Fact]
    public async Task GetStatus_VersionWithoutVPrefix_ReturnsAsIs()
    {
        using var temp = new TempExecutablePath();
        temp.Add("npm.cmd");
        var runner = FakeProcessRunner.Returning("11.7.0\n");
        var provider = new NpmProvider(runner, new ExecutableLocator(temp.DirectoryPath));

        var status = await provider.GetStatusAsync(CancellationToken.None);

        Assert.Equal("11.7.0", status.Version);
    }

    [Fact]
    public async Task GetStatus_BlankOutput_ReturnsNullVersion()
    {
        using var temp = new TempExecutablePath();
        temp.Add("node.exe");
        var runner = FakeProcessRunner.Returning("  \n");
        var provider = new NodeProvider(runner, new ExecutableLocator(temp.DirectoryPath));

        var status = await provider.GetStatusAsync(CancellationToken.None);

        Assert.True(status.IsInstalled);
        Assert.Null(status.Version);
    }

    [Fact]
    public async Task GetStatus_OversizedOutput_ReturnsNullVersion()
    {
        using var temp = new TempExecutablePath();
        temp.Add("node.exe");
        var runner = FakeProcessRunner.Returning(new string('9', 65));
        var provider = new NodeProvider(runner, new ExecutableLocator(temp.DirectoryPath));

        var status = await provider.GetStatusAsync(CancellationToken.None);

        Assert.True(status.IsInstalled);
        Assert.Null(status.Version);
    }

    [Fact]
    public async Task GetStatus_RunnerFailure_ReturnsInstalledWithoutVersion()
    {
        using var temp = new TempExecutablePath();
        temp.Add("node.exe");
        var runner = FakeProcessRunner.Throwing(new IOException("El archivo desapareció."));
        var provider = new NodeProvider(runner, new ExecutableLocator(temp.DirectoryPath));

        var status = await provider.GetStatusAsync(CancellationToken.None);

        Assert.True(status.IsInstalled);
        Assert.Null(status.Version);
    }

    [Fact]
    public async Task GetStatus_VersionWithSuffixText_ReturnsFirstToken()
    {
        // Como claude --version: "2.1.181 (Claude Code)".
        using var temp = new TempExecutablePath();
        temp.Add("claude.exe");
        var runner = FakeProcessRunner.Returning("2.1.181 (Claude Code)\n");
        var provider = new ClaudeCodeProvider(runner, new ExecutableLocator(temp.DirectoryPath));

        var status = await provider.GetStatusAsync(CancellationToken.None);

        Assert.Equal("2.1.181", status.Version);
    }

    [Fact]
    public async Task GetStatus_CallerCancelled_PropagatesCancellation()
    {
        using var temp = new TempExecutablePath();
        temp.Add("node.exe");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var runner = new FakeProcessRunner((_, ct) => Task.FromCanceled<ProcessResult>(ct));
        var provider = new NodeProvider(runner, new ExecutableLocator(temp.DirectoryPath));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GetStatusAsync(cts.Token));
    }
}
