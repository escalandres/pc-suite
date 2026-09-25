using System.Text.Json;
using PCSuite.Contracts;
using PCSuite.Core.Operations;
using PCSuite.Core.Processes;
using PCSuite.Infrastructure.Developers;
using PCSuite.Infrastructure.Wsl;

namespace PCSuite.Application.Tests;

/// <summary>
/// La mutación que corre DENTRO del proceso elevado (Task 007): ID en la
/// allow-list, payload ignorado por completo (wsl.update es de payload cero:
/// argv fijo --update, jamás ejecución arbitraria), timeouts y resultados
/// estructurados con mensaje controlado (nunca stack traces ni salidas crudas).
/// </summary>
public sealed class WslElevatedOperationTests
{
    private static readonly TimeSpan MutationTimeout = TimeSpan.FromSeconds(900);

    // ------------------------------------------------------------------
    // Allow-list (dos barreras sobre ElevatedOperationIds.Allowed)
    // ------------------------------------------------------------------

    [Fact]
    public void Id_MapsToAllowListedElevatedId()
    {
        var operation = new WslElevatedOperation();

        Assert.Equal(ElevatedOperationIds.WslUpdate, operation.Id);
        Assert.Contains(operation.Id, ElevatedOperationIds.Allowed);
    }

    // ------------------------------------------------------------------
    // Ejecución
    // ------------------------------------------------------------------

    [Fact]
    public async Task Update_RebuildsFixedArgv_Utf16Le_With900sTimeout()
    {
        using var temp = new TempExecutablePath();
        var wslPath = temp.Add("wsl.exe");
        var runner = FakeProcessRunner.Returning(string.Empty);
        var operation = new WslElevatedOperation(new ExecutableLocator(temp.DirectoryPath), runner);

        var result = await operation.ExecuteAsync(new OperationContext(), CancellationToken.None);

        Assert.True(result.Success);
        var data = result.Data!.Value;
        Assert.True(data.GetProperty("succeeded").GetBoolean());
        Assert.Equal(0, data.GetProperty("exitCode").GetInt32());
        Assert.True(data.GetProperty("elevated").GetBoolean());
        Assert.False(data.TryGetProperty("detail", out _)); // sin salida → sin detalle

        var request = Assert.Single(runner.Requests);
        Assert.Equal(wslPath, request.FileName);
        Assert.Equal<string>(new[] { "--update" }, request.Arguments);
        Assert.Equal(MutationTimeout, request.Timeout);
        Assert.Equal(1200, request.OutputEncoding!.CodePage);
    }

    [Theory]
    [InlineData("""{"args":["--shutdown","--distro","x"]}""")]
    [InlineData("\"wsl --exec calc\"")]
    public async Task Payload_IsIgnored_FixedUpdateArgvOnly(string payloadJson)
    {
        using var temp = new TempExecutablePath();
        temp.Add("wsl.exe");
        var runner = FakeProcessRunner.Returning(string.Empty);
        var operation = new WslElevatedOperation(new ExecutableLocator(temp.DirectoryPath), runner);

        // Ni se valida ni se parsea (cero excepciones de esquema): el argv es
        // siempre --update.
        var result = await operation.ExecuteAsync(Payload(payloadJson), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal<string>(new[] { "--update" }, Assert.Single(runner.Requests).Arguments);
    }

    [Fact]
    public async Task WithoutWsl_FailsNotInstalled_WithoutAnyProcess()
    {
        using var temp = new TempExecutablePath();
        var runner = FakeProcessRunner.Returning(string.Empty);
        var operation = new WslElevatedOperation(new ExecutableLocator(temp.DirectoryPath), runner);

        var result = await operation.ExecuteAsync(new OperationContext(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(BridgeErrorCodes.NotInstalled, result.ErrorCode);
        Assert.Equal("No se encontró wsl.exe en la PATH.", result.ErrorMessage);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task Timeout_FailsWslTimeout_WithControlledMessage()
    {
        using var temp = new TempExecutablePath();
        temp.Add("wsl.exe");
        var runner = FakeProcessRunner.Throwing(new ProcessTimeoutException(TimeSpan.FromSeconds(10)));
        var operation = new WslElevatedOperation(new ExecutableLocator(temp.DirectoryPath), runner);

        var result = await operation.ExecuteAsync(new OperationContext(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(BridgeErrorCodes.WslTimeout, result.ErrorCode);
        Assert.Equal("La operación wsl superó el tiempo límite y fue terminada.", result.ErrorMessage);
        Assert.DoesNotContain("   at ", result.ErrorMessage);
    }

    [Fact]
    public async Task CliFailure_IsStructuredResult_WithSanitizedDetail()
    {
        using var temp = new TempExecutablePath();
        temp.Add("wsl.exe");
        var cliOutput = "aviso previo\nsegunda" + (char)7 + "con control"; // BEL en la salida cruda
        var runner = FakeProcessRunner.Returning(cliOutput, 1);
        var operation = new WslElevatedOperation(new ExecutableLocator(temp.DirectoryPath), runner);

        var result = await operation.ExecuteAsync(new OperationContext(), CancellationToken.None);

        Assert.True(result.Success);
        var data = result.Data!.Value;
        Assert.False(data.GetProperty("succeeded").GetBoolean());
        Assert.Equal(1, data.GetProperty("exitCode").GetInt32());
        Assert.True(data.GetProperty("elevated").GetBoolean());
        Assert.Equal("segundacon control", data.GetProperty("detail").GetString()); // sin controles
    }

    [Fact]
    public async Task UnexpectedException_FailsOperationFailed_WithoutLeakingInternals()
    {
        using var temp = new TempExecutablePath();
        temp.Add("wsl.exe");
        var runner = FakeProcessRunner.Throwing(new InvalidOperationException("detalle interno secreto"));
        var operation = new WslElevatedOperation(new ExecutableLocator(temp.DirectoryPath), runner);

        var result = await operation.ExecuteAsync(new OperationContext(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(BridgeErrorCodes.OperationFailed, result.ErrorCode);
        Assert.Equal("La operación elevada falló.", result.ErrorMessage);
        Assert.DoesNotContain("detalle interno secreto", result.ErrorMessage);
        Assert.DoesNotContain("   at ", result.ErrorMessage);
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        using var temp = new TempExecutablePath();
        temp.Add("wsl.exe");
        var runner = FakeProcessRunner.Throwing(new OperationCanceledException());
        var operation = new WslElevatedOperation(new ExecutableLocator(temp.DirectoryPath), runner);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => operation.ExecuteAsync(new OperationContext(), CancellationToken.None));
    }

    [Fact]
    public async Task OnNonWindows_FailsNotSupported_BeforeDoingAnything()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var operation = new WslElevatedOperation(
            new ExecutableLocator(Environment.CurrentDirectory),
            FakeProcessRunner.Returning(string.Empty));

        var result = await operation.ExecuteAsync(new OperationContext(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(BridgeErrorCodes.NotSupported, result.ErrorCode);
        Assert.Equal("Solo disponible en Windows.", result.ErrorMessage);
    }

    private static OperationContext Payload(string json) =>
        new() { Payload = JsonSerializer.Deserialize<JsonElement>(json) };
}