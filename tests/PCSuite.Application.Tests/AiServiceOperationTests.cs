using System.Text.Json;
using PCSuite.Contracts;
using PCSuite.Core.AI;
using PCSuite.Core.Operations;
using PCSuite.Infrastructure.AI;
using PCSuite.Infrastructure.Developers;
using PCSuite.Infrastructure.Operations;
using PCSuite.Infrastructure.Processes;

namespace PCSuite.Application.Tests;

/// <summary>Task 038: pruebas de AiService (con process runner mockeado) y AiOperations.</summary>
public sealed class AiServiceOperationTests
{
    [Fact]
    public async Task GetStatus_PythonNotFound_ReturnsNotInstalled()
    {
        var locator = new ExecutableLocator(""); // PATH vacío → no encuentra python
        var runner = FakeProcessRunner.Returning("");
        var svc = new AiService(locator, runner);

        var status = await svc.GetStatusAsync();

        Assert.False(status.Installed);
        Assert.Null(status.PythonPath);
        Assert.NotNull(status.Detail);
    }

    [Fact]
    public async Task Install_PythonNotFound_ReturnsFailed()
    {
        var locator = new ExecutableLocator("");
        var runner = FakeProcessRunner.Returning("");
        var svc = new AiService(locator, runner);

        var outcome = await svc.InstallAsync();

        Assert.False(outcome.Succeeded);
        Assert.Contains("Python", outcome.Detail ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Convert_PythonNotFound_ThrowsAiNotInstalled()
    {
        var locator = new ExecutableLocator("");
        var runner = FakeProcessRunner.Returning("");
        var svc = new AiService(locator, runner);

        var tempFile = Path.Combine(Path.GetTempPath(), "ai-convert-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(tempFile, "hello");
        try
        {
            await Assert.ThrowsAsync<AiException>(() => svc.ConvertAsync(tempFile));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task GetOllamaStatus_NotInstalled_ReturnsFalse()
    {
        var locator = new ExecutableLocator("");
        var runner = FakeProcessRunner.Returning("");
        var svc = new AiService(locator, runner);

        var status = await svc.GetOllamaStatusAsync();

        Assert.False(status.Installed);
        Assert.Null(status.OllamaPath);
    }

    [Fact]
    public async Task ListOllamaModels_NotInstalled_Throws()
    {
        var locator = new ExecutableLocator("");
        var runner = FakeProcessRunner.Returning("");
        var svc = new AiService(locator, runner);

        await Assert.ThrowsAsync<AiException>(() => svc.ListOllamaModelsAsync());
    }

    // --- AiOperations ---

    [Fact]
    public async Task AiDetectOperation_ReturnsStatus()
    {
        var locator = new ExecutableLocator("");
        var runner = FakeProcessRunner.Returning("");
        var svc = new AiService(locator, runner);
        var op = new AiDetectOperation(svc);

        var result = await op.ExecuteAsync(new OperationContext(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
    }

    [Fact]
    public async Task AiConvertOperation_MissingPath_Fails()
    {
        var locator = new ExecutableLocator("");
        var runner = FakeProcessRunner.Returning("");
        var svc = new AiService(locator, runner);
        var op = new AiConvertOperation(svc);

        var result = await op.ExecuteAsync(new OperationContext { Payload = null }, CancellationToken.None);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task AiConvertOperation_MalformedPayload_Fails()
    {
        var locator = new ExecutableLocator("");
        var runner = FakeProcessRunner.Returning("");
        var svc = new AiService(locator, runner);
        var op = new AiConvertOperation(svc);

        var payload = JsonDocument.Parse("42").RootElement;
        var result = await op.ExecuteAsync(new OperationContext { Payload = payload }, CancellationToken.None);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task OllamaDetectOperation_ReturnsStatus()
    {
        var locator = new ExecutableLocator("");
        var runner = FakeProcessRunner.Returning("");
        var svc = new AiService(locator, runner);
        var op = new OllamaDetectOperation(svc);

        var result = await op.ExecuteAsync(new OperationContext(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
    }
}