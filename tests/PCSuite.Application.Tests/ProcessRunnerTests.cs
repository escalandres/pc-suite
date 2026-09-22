using System.Collections.Concurrent;
using System.Diagnostics;
using PCSuite.Core.Processes;
using PCSuite.Infrastructure.Processes;

namespace PCSuite.Application.Tests;

/// <summary>
/// Pruebas del ProcessRunner contra el ejecutable mock
/// (tests/PCSuite.MockProcess): sin dependencias de software de sistema.
/// </summary>
public sealed class ProcessRunnerTests
{
    private static readonly ProcessRunner Runner = new();

    [Fact]
    public async Task ExecuteAsync_ReturnsExitCodeAndDuration()
    {
        var result = await Runner.ExecuteAsync(MockProcess.Request("exit", "7"), CancellationToken.None);

        Assert.Equal(7, result.ExitCode);
        Assert.True(result.Duration > TimeSpan.Zero, $"Duración no positiva: {result.Duration}");
    }

    [Fact]
    public async Task ExecuteAsync_Stdout_IsStreamedWhileRunning_AndBuffered()
    {
        // 8 líneas cada 250 ms (~2 s en total). Si la salida se bufferizara
        // entera hasta el final, a mitad de ejecución no habría llegado nada.
        var lines = new ConcurrentQueue<string>();
        var request = MockProcess.Request("out", "8", "250") with
        {
            OnStandardOutput = line => lines.Enqueue(line),
        };

        var execution = Runner.ExecuteAsync(request, CancellationToken.None);

        await Task.Delay(TimeSpan.FromSeconds(1.25));
        Assert.True(lines.Count >= 3, $"Streamming real: se esperaban ≥ 3 líneas en curso, llegaron {lines.Count}.");

        var result = await execution;

        Assert.Equal(
            Enumerable.Range(1, 8).Select(i => $"stdout-line-{i}"),
            lines);
        Assert.Equal(
            Enumerable.Range(1, 8).Select(i => $"stdout-line-{i}"),
            MockProcess.SplitLines(result.StandardOutput));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task ExecuteAsync_Stderr_IsStreamedAndBuffered()
    {
        var lines = new ConcurrentQueue<string>();
        var request = MockProcess.Request("err", "3") with
        {
            OnStandardError = line => lines.Enqueue(line),
        };

        var result = await Runner.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(new[] { "stderr-line-1", "stderr-line-2", "stderr-line-3" }, lines);
        Assert.Equal(new[] { "stderr-line-1", "stderr-line-2", "stderr-line-3" }, MockProcess.SplitLines(result.StandardError));
        Assert.Equal("", result.StandardOutput);
    }

    [Fact]
    public async Task ExecuteAsync_Arguments_ArePassedVerbatim_WithoutShellInterpretation()
    {
        // Metacaracteres de shell y espacios/unicode: deben llegar intactos,
        // lo que demuestra que no hay interpretación intermedia.
        var result = await Runner.ExecuteAsync(
            MockProcess.Request("echo", "con espacio", "ñandú", "-x", "a|b&c<>"),
            CancellationToken.None);

        Assert.Equal(
            new[] { "arg:con espacio", "arg:ñandú", "arg:-x", "arg:a|b&c<>" },
            MockProcess.SplitLines(result.StandardOutput));
    }

    [Fact]
    public async Task ExecuteAsync_WorkingDirectory_IsHonored()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pcsuite-runner-test");
        Directory.CreateDirectory(directory);

        try
        {
            var request = MockProcess.Request("cwd") with { WorkingDirectory = directory };
            var result = await Runner.ExecuteAsync(request, CancellationToken.None);

            var printed = MockProcess.SplitLines(result.StandardOutput).Single();
            Assert.Equal(
                Path.TrimEndingDirectorySeparator(directory),
                Path.TrimEndingDirectorySeparator(printed),
                StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_Timeout_KillsProcess_AndThrowsTypedError()
    {
        var request = MockProcess.Request("sleep", "10000") with
        {
            Timeout = TimeSpan.FromMilliseconds(500),
        };

        var stopwatch = Stopwatch.StartNew();
        var exception = await Assert.ThrowsAsync<ProcessTimeoutException>(
            () => Runner.ExecuteAsync(request, CancellationToken.None));
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"El timeout tardó {stopwatch.Elapsed}.");
        Assert.Equal(TimeSpan.FromMilliseconds(500), exception.Timeout);
        Assert.DoesNotContain("   at ", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_CallerCancellation_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
        var request = MockProcess.Request("sleep", "10000");

        var stopwatch = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Runner.ExecuteAsync(request, cts.Token));
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"La cancelación tardó {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task ExecuteAsync_MissingExecutable_ThrowsControlledWin32Error()
    {
        var request = new ProcessRequest { FileName = @"C:\no\existe\pcsuite-mock.exe" };

        var exception = await Assert.ThrowsAsync<System.ComponentModel.Win32Exception>(
            () => Runner.ExecuteAsync(request, CancellationToken.None));

        Assert.False(string.IsNullOrWhiteSpace(exception.Message));
        Assert.DoesNotContain("   at ", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_RequiresElevation_IsRejected()
    {
        var request = new ProcessRequest { FileName = "pcsuite-mock", RequiresElevation = true };

        var exception = await Assert.ThrowsAsync<NotSupportedException>(
            () => Runner.ExecuteAsync(request, CancellationToken.None));

        Assert.Contains("Task 005", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyFileName_ThrowsArgumentException()
    {
        var request = new ProcessRequest { FileName = "   " };

        await Assert.ThrowsAsync<ArgumentException>(
            () => Runner.ExecuteAsync(request, CancellationToken.None));
    }
}
