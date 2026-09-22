using System.Diagnostics;
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
    public async Task UpdateAsync_NotInstalled_FailsControlledWithoutExecuting(string id, string name)
    {
        var runner = FakeProcessRunner.Throwing(new InvalidOperationException("No debe ejecutarse."));
        var provider = Create(id, runner, new ExecutableLocator(string.Empty));

        var result = await provider.UpdateAsync(CancellationToken.None);

        Assert.False(result.Success);

        // Solo Node.js carece de fuente de autoactualización (NOT_SUPPORTED);
        // el resto llegan a comprobar la instalación (NOT_INSTALLED).
        Assert.Equal(id == "node" ? "NOT_SUPPORTED" : "NOT_INSTALLED", result.ErrorCode);
        Assert.Contains(name, result.ErrorMessage);
        Assert.DoesNotContain("   at ", result.ErrorMessage);
        Assert.Empty(runner.Requests);
    }

    [Theory]
    [MemberData(nameof(ProviderIdNames))]
    public async Task LaunchAsync_NotInstalledOrNotSupported_FailsControlledWithoutExecuting(string id, string name)
    {
        var runner = FakeProcessRunner.Throwing(new InvalidOperationException("No debe ejecutarse."));
        var provider = Create(id, runner, new ExecutableLocator(string.Empty));

        var result = await provider.LaunchAsync(CancellationToken.None);

        Assert.False(result.Success);

        // node y claude-code son interactivos (llegan a buscar la instalación);
        // npm/pnpm/bun no tienen interfaz interactiva propia.
        var supportsLaunch = id is "node" or "claude-code";
        Assert.Equal(supportsLaunch ? "NOT_INSTALLED" : "NOT_SUPPORTED", result.ErrorCode);
        Assert.Contains(name, result.ErrorMessage);
        Assert.DoesNotContain("   at ", result.ErrorMessage);
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

    [Fact]
    public async Task CheckUpdateAsync_NewerVersionAvailable_ReportsUpdate_AndQueriesRegistry()
    {
        using var temp = new TempExecutablePath();
        var nodePath = temp.Add("node.exe");
        var npmPath = temp.Add("npm.cmd");
        var runner = new FakeProcessRunner((request, _) => Task.FromResult(new ProcessResult
        {
            ExitCode = 0,
            StandardOutput = request.Arguments.Contains("view") ? "26.9.0\n" : "v26.4.0\n",
        }));
        var provider = new NodeProvider(runner, new ExecutableLocator(temp.DirectoryPath));

        var info = await provider.CheckUpdateAsync(CancellationToken.None);

        Assert.NotNull(info);
        Assert.Equal("26.4.0", info!.CurrentVersion);
        Assert.Equal("26.9.0", info.LatestVersion);
        Assert.True(info.UpdateAvailable);

        // 1) --version del nodo, 2) consulta a la fuente resuelta en la PATH.
        Assert.Equal(2, runner.Requests.Count);
        Assert.Equal(nodePath, runner.Requests[0].FileName, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(new[] { "--version" }, runner.Requests[0].Arguments);

        var registry = runner.Requests[1];
        Assert.Equal(npmPath, registry.FileName, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(new[] { "view", "node", "version" }, registry.Arguments);
        Assert.True(registry.Timeout.HasValue, "La consulta a la fuente debe tener timeout.");
        Assert.False(registry.Interactive);
    }

    [Fact]
    public async Task CheckUpdateAsync_AlreadyLatest_ReportsNoUpdate()
    {
        using var temp = new TempExecutablePath();
        temp.Add("npm.cmd");
        var runner = FakeProcessRunner.Returning("11.7.0\n");
        var provider = new NpmProvider(runner, new ExecutableLocator(temp.DirectoryPath));

        var info = await provider.CheckUpdateAsync(CancellationToken.None);

        Assert.NotNull(info);
        Assert.Equal("11.7.0", info!.CurrentVersion);
        Assert.Equal("11.7.0", info.LatestVersion);
        Assert.False(info.UpdateAvailable);
    }

    [Fact]
    public async Task CheckUpdateAsync_SourceFailure_ReturnsNull()
    {
        using var temp = new TempExecutablePath();
        temp.Add("claude.exe");
        temp.Add("npm.cmd");
        var calls = 0;
        var runner = new FakeProcessRunner((_, _) => ++calls == 1
            ? Task.FromResult(new ProcessResult { ExitCode = 0, StandardOutput = "2.1.181 (Claude Code)\n" })
            : Task.FromException<ProcessResult>(new ProcessTimeoutException(TimeSpan.FromSeconds(60))));
        var provider = new ClaudeCodeProvider(runner, new ExecutableLocator(temp.DirectoryPath));

        var info = await provider.CheckUpdateAsync(CancellationToken.None);

        Assert.Null(info);
        Assert.Equal(2, runner.Requests.Count);
    }

    [Fact]
    public async Task CheckUpdateAsync_SourceToolMissing_ReturnsNull_WithoutQuerying()
    {
        // Bun consulta el registro npm vía npm: sin npm, degrada a null.
        using var temp = new TempExecutablePath();
        temp.Add("bun.CMD");
        var runner = FakeProcessRunner.Returning("1.4.0\n");
        var provider = new BunProvider(runner, new ExecutableLocator(temp.DirectoryPath));

        var info = await provider.CheckUpdateAsync(CancellationToken.None);

        Assert.Null(info);
        var request = Assert.Single(runner.Requests);
        Assert.Equal(new[] { "--version" }, request.Arguments);
    }

    [Fact]
    public async Task UpdateAsync_SelfUpdater_Succeeds_AndReportsNewVersion()
    {
        using var temp = new TempExecutablePath();
        var claudePath = temp.Add("claude.exe");
        var versionQueries = 0;
        var runner = new FakeProcessRunner((request, _) =>
        {
            if (request.Arguments.Contains("update"))
            {
                return Task.FromResult(new ProcessResult { ExitCode = 0 });
            }

            return Task.FromResult(new ProcessResult
            {
                ExitCode = 0,
                StandardOutput = ++versionQueries == 1
                    ? "2.1.181 (Claude Code)\n"
                    : "2.1.280 (Claude Code)\n",
            });
        });
        var provider = new ClaudeCodeProvider(runner, new ExecutableLocator(temp.DirectoryPath));

        var result = await provider.UpdateAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.ErrorCode);
        Assert.NotNull(result.Data);
        var json = result.Data!.Value.GetRawText();
        Assert.Contains("2.1.181", json);
        Assert.Contains("2.1.280", json);

        // Estado → autoactualización → estado (verificación posterior).
        Assert.Equal(3, runner.Requests.Count);
        var update = runner.Requests[1];
        Assert.Equal(claudePath, update.FileName, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(new[] { "update" }, update.Arguments);
        Assert.Equal(TimeSpan.FromMinutes(5), update.Timeout);
        Assert.False(update.Interactive);
    }

    [Fact]
    public async Task UpdateAsync_PermissionDenied_ReportsElevationRequired_WithoutStack()
    {
        using var temp = new TempExecutablePath();
        temp.Add("npm.cmd");
        var runner = new FakeProcessRunner((request, _) => Task.FromResult(
            request.Arguments.Contains("install")
                ? new ProcessResult
                {
                    ExitCode = 1,
                    StandardError = "npm ERR! code EACCES\nnpm ERR!     at async Object.rename (x:1:2)\n",
                }
                : new ProcessResult { ExitCode = 0, StandardOutput = "11.7.0\n" }));
        var provider = new NpmProvider(runner, new ExecutableLocator(temp.DirectoryPath));

        var result = await provider.UpdateAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("ELEVATION_REQUIRED", result.ErrorCode);
        Assert.Contains("npm", result.ErrorMessage);
        Assert.Contains("EACCES", result.ErrorMessage);
        Assert.DoesNotContain("at async", result.ErrorMessage);
        Assert.DoesNotContain("   at ", result.ErrorMessage);

        Assert.Equal(2, runner.Requests.Count);
        Assert.Equal(new[] { "install", "-g", "npm@latest" }, runner.Requests[1].Arguments);
    }

    [Fact]
    public async Task UpdateAsync_Failure_ReportsControlledDetail_WithoutStack()
    {
        using var temp = new TempExecutablePath();
        temp.Add("bun.CMD");
        var runner = new FakeProcessRunner((request, _) => Task.FromResult(
            request.Arguments.Contains("upgrade")
                ? new ProcessResult
                {
                    ExitCode = 1,
                    StandardError = "error: something exploded\n   at Object.foo (bun:1:1)\n",
                }
                : new ProcessResult { ExitCode = 0, StandardOutput = "1.4.0\n" }));
        var provider = new BunProvider(runner, new ExecutableLocator(temp.DirectoryPath));

        var result = await provider.UpdateAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("UPDATE_FAILED", result.ErrorCode);
        Assert.Contains("something exploded", result.ErrorMessage);
        Assert.DoesNotContain("Object.foo", result.ErrorMessage);
        Assert.DoesNotContain("   at ", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateAsync_Timeout_ReportsControlledTimeoutError()
    {
        using var temp = new TempExecutablePath();
        temp.Add("pnpm.CMD");
        var runner = FakeProcessRunner.Throwing(new ProcessTimeoutException(TimeSpan.FromMinutes(5)));
        var provider = new PnpmProvider(runner, new ExecutableLocator(temp.DirectoryPath));

        var result = await provider.UpdateAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("UPDATE_TIMEOUT", result.ErrorCode);
        Assert.Contains("tiempo límite", result.ErrorMessage);
        Assert.DoesNotContain("   at ", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateAsync_RunnerFailure_ReportsGenericControlledError()
    {
        using var temp = new TempExecutablePath();
        temp.Add("bun.CMD");
        var runner = FakeProcessRunner.Throwing(new IOException("La unidad de red cayó."));
        var provider = new BunProvider(runner, new ExecutableLocator(temp.DirectoryPath));

        var result = await provider.UpdateAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("UPDATE_FAILED", result.ErrorCode);
        Assert.Contains("Bun", result.ErrorMessage);

        // El detalle de la excepción nunca llega a la UI.
        Assert.DoesNotContain("La unidad de red", result.ErrorMessage);
        Assert.DoesNotContain("   at ", result.ErrorMessage);
    }

    public static TheoryData<string, string, string> LaunchableProviders => new()
    {
        { "node", "Node.js", "node.exe" },
        { "claude-code", "Claude Code", "claude.exe" },
    };

    [Theory]
    [MemberData(nameof(LaunchableProviders))]
    public async Task LaunchAsync_InteractiveTool_StartsWithoutWaitingForClose(
        string id,
        string name,
        string executableFile)
    {
        using var temp = new TempExecutablePath();
        var path = temp.Add(executableFile);
        var never = new TaskCompletionSource<ProcessResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new FakeProcessRunner((request, _) => request.Interactive
            ? never.Task
            : Task.FromResult(new ProcessResult { ExitCode = 0, StandardOutput = "v1.0.0\n" }));
        var provider = Create(id, runner, new ExecutableLocator(temp.DirectoryPath));

        var stopwatch = Stopwatch.StartNew();
        var result = await provider.LaunchAsync(CancellationToken.None);
        stopwatch.Stop();

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(10),
            $"LaunchAsync no debe esperar al cierre de la herramienta: {stopwatch.Elapsed}.");

        // 1) estado (--version), 2) lanzamiento interactivo.
        Assert.Equal(2, runner.Requests.Count);
        var launch = runner.Requests[1];
        Assert.Equal(path, launch.FileName, StringComparer.OrdinalIgnoreCase);
        Assert.Empty(launch.Arguments);
        Assert.True(launch.Interactive);
        Assert.Null(launch.Timeout);
        Assert.Null(launch.OnStandardOutput);
        Assert.Null(launch.OnStandardError);
        Assert.False(launch.RequiresElevation);
        Assert.Contains(name, provider.Name);
    }

    [Theory]
    [MemberData(nameof(LaunchableProviders))]
    public async Task LaunchAsync_StartupFailure_ReportsControlledError(
        string id,
        string name,
        string executableFile)
    {
        using var temp = new TempExecutablePath();
        temp.Add(executableFile);
        var runner = FakeProcessRunner.Throwing(new InvalidOperationException("boom"));
        var provider = Create(id, runner, new ExecutableLocator(temp.DirectoryPath));

        var result = await provider.LaunchAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("LAUNCH_FAILED", result.ErrorCode);
        Assert.Contains(name, result.ErrorMessage);
        Assert.DoesNotContain("boom", result.ErrorMessage);
        Assert.DoesNotContain("   at ", result.ErrorMessage);
    }

    [Fact]
    public async Task LaunchAsync_ToolExitsImmediatelyWithError_ReportsControlledError()
    {
        using var temp = new TempExecutablePath();
        temp.Add("node.exe");
        var runner = FakeProcessRunner.Returning(string.Empty, exitCode: 64);
        var provider = new NodeProvider(runner, new ExecutableLocator(temp.DirectoryPath));

        var result = await provider.LaunchAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("LAUNCH_FAILED", result.ErrorCode);
        Assert.Contains("64", result.ErrorMessage);
        Assert.DoesNotContain("   at ", result.ErrorMessage);
    }

    [Fact]
    public async Task ProviderDefaults_WithoutSourceOrLaunch_StayControlled()
    {
        using var temp = new TempExecutablePath();
        temp.Add("bare.exe");
        var runner = FakeProcessRunner.Returning("1.0\n");
        var provider = new BareProvider(runner, new ExecutableLocator(temp.DirectoryPath));

        Assert.Null(await provider.CheckUpdateAsync(CancellationToken.None));

        var update = await provider.UpdateAsync(CancellationToken.None);
        Assert.False(update.Success);
        Assert.Equal("NOT_SUPPORTED", update.ErrorCode);
        Assert.Contains("Bare", update.ErrorMessage);

        var launch = await provider.LaunchAsync(CancellationToken.None);
        Assert.False(launch.Success);
        Assert.Equal("NOT_SUPPORTED", launch.ErrorCode);
        Assert.Contains("Bare", launch.ErrorMessage);
    }

    /// <summary>Proveedor mínimo para ejercitar los valores por defecto de la base.</summary>
    private sealed class BareProvider : DeveloperToolProviderBase
    {
        public BareProvider(IProcessRunner runner, ExecutableLocator locator)
            : base(runner, locator)
        {
        }

        public override string Id => "bare";

        public override string Name => "Bare";

        protected override string ExecutableName => "bare";
    }
}
