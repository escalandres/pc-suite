using System.Text.Json;
using PCSuite.Contracts;
using PCSuite.Core.Operations;
using PCSuite.Core.Processes;
using PCSuite.Infrastructure.ClamAV;
using PCSuite.Infrastructure.Developers;

namespace PCSuite.Application.Tests;

/// <summary>
/// freshclam DENTRO del proceso elevado (Task 008): ID en la allow-list,
/// payload ignorado con argv VACÍO fijo (sin ruta a un comando arbitrario),
/// carpeta de trabajo = instalación de ClamAV, timeout y resultado estructurado
/// con mensaje controlado (nunca stack traces ni salidas crudas).
/// </summary>
public sealed class ClamAvElevatedOperationTests
{
    private static readonly TimeSpan MutationTimeout = TimeSpan.FromSeconds(900);

    [Fact]
    public void Id_MapsToAllowListedElevatedId()
    {
        var operation = new ClamAvElevatedOperation();

        Assert.Equal(ElevatedOperationIds.ClamavUpdateDefinitions, operation.Id);
        Assert.Contains(operation.Id, ElevatedOperationIds.Allowed);
    }

    [Fact]
    public async Task Success_RebuildsFixedEmptyArgv_WorkingDirectoryAndTimeout()
    {
        using var temp = new TempExecutablePath();
        temp.Add("clamscan.exe");
        var freshclamPath = temp.Add("freshclam.exe");
        var runner = FakeProcessRunner.Returning(string.Empty);
        var operation = new ClamAvElevatedOperation(
            new ExecutableLocator(temp.DirectoryPath), runner, installDirectories: []);

        var result = await operation.ExecuteAsync(new OperationContext(), CancellationToken.None);

        Assert.True(result.Success);
        var data = result.Data!.Value;
        Assert.True(data.GetProperty("succeeded").GetBoolean());
        Assert.Equal(0, data.GetProperty("exitCode").GetInt32());
        Assert.True(data.GetProperty("elevated").GetBoolean());
        Assert.False(data.TryGetProperty("detail", out _)); // sin salida → sin detalle

        var request = Assert.Single(runner.Requests);
        Assert.Equal(freshclamPath, request.FileName);
        Assert.Empty(request.Arguments); // argv vacío fijo: freshclam usa su WorkingDirectory
        Assert.Equal(Path.GetDirectoryName(freshclamPath), request.WorkingDirectory);
        Assert.Equal(MutationTimeout, request.Timeout);
        Assert.Null(request.OutputEncoding); // CRT en codepage OEM (a diferencia de wsl)
    }

    [Theory]
    [InlineData("""{"args":["--arg","calc"]}""")]
    [InlineData("\"freshclam --arg calc\"")]
    public async Task Payload_IsIgnored_FixedEmptyArgvOnly(string payloadJson)
    {
        using var temp = new TempExecutablePath();
        temp.Add("clamscan.exe");
        temp.Add("freshclam.exe");
        var runner = FakeProcessRunner.Returning(string.Empty);
        var operation = new ClamAvElevatedOperation(
            new ExecutableLocator(temp.DirectoryPath), runner, installDirectories: []);

        // Ni se valida ni se parsea (cero excepciones de esquema): el argv es
        // siempre vacío, así que no existe entrada hacia un comando arbitrario.
        var result = await operation.ExecuteAsync(Payload(payloadJson), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Empty(Assert.Single(runner.Requests).Arguments);
    }

    [Fact]
    public async Task WithoutClamAv_FailsNotInstalled_WithoutAnyProcess()
    {
        using var temp = new TempExecutablePath();
        var runner = FakeProcessRunner.Returning(string.Empty);
        var operation = new ClamAvElevatedOperation(
            new ExecutableLocator(temp.DirectoryPath), runner, installDirectories: []);

        var result = await operation.ExecuteAsync(new OperationContext(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(BridgeErrorCodes.NotInstalled, result.ErrorCode);
        Assert.Equal("ClamAV no está instalado.", result.ErrorMessage);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task WithoutFreshclam_FailsNotInstalled_WithoutAnyProcess()
    {
        using var temp = new TempExecutablePath();
        temp.Add("clamscan.exe"); // sin freshclam.exe junto
        var runner = FakeProcessRunner.Returning(string.Empty);
        var operation = new ClamAvElevatedOperation(
            new ExecutableLocator(temp.DirectoryPath), runner, installDirectories: []);

        var result = await operation.ExecuteAsync(new OperationContext(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(BridgeErrorCodes.NotInstalled, result.ErrorCode);
        Assert.Equal("No se encontró freshclam.exe junto a clamscan.exe.", result.ErrorMessage);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task Timeout_FailsWithClamAvTimeout_ControlledMessage()
    {
        using var temp = new TempExecutablePath();
        temp.Add("clamscan.exe");
        temp.Add("freshclam.exe");
        var runner = FakeProcessRunner.Throwing(new ProcessTimeoutException(TimeSpan.FromSeconds(10)));
        var operation = new ClamAvElevatedOperation(
            new ExecutableLocator(temp.DirectoryPath), runner, installDirectories: []);

        var result = await operation.ExecuteAsync(new OperationContext(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(BridgeErrorCodes.ClamAvTimeout, result.ErrorCode);
        Assert.Equal(
            "La actualización de definiciones superó el tiempo límite y fue terminada.",
            result.ErrorMessage);
    }

    [Fact]
    public async Task CliFailure_ReturnsStructuredResult_SanitizingRawOutput()
    {
        using var temp = new TempExecutablePath();
        temp.Add("clamscan.exe");
        temp.Add("freshclam.exe");
        // Salida con un carácter de control en medio: el detalle lo sanea.
        var runner = FakeProcessRunner.Returning(
            "aviso previo\nsegunda" + (char)7 + "con control", exitCode: 1);
        var operation = new ClamAvElevatedOperation(
            new ExecutableLocator(temp.DirectoryPath), runner, installDirectories: []);

        var result = await operation.ExecuteAsync(new OperationContext(), CancellationToken.None);

        Assert.True(result.Success); // fallo controlado NO es error del puente
        var data = result.Data!.Value;
        Assert.False(data.GetProperty("succeeded").GetBoolean());
        Assert.Equal(1, data.GetProperty("exitCode").GetInt32());
        Assert.True(data.GetProperty("elevated").GetBoolean());
        Assert.Equal("segundacon control", data.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task UnexpectedException_FailsOperationFailed_WithoutLeakingDetails()
    {
        using var temp = new TempExecutablePath();
        temp.Add("clamscan.exe");
        temp.Add("freshclam.exe");
        var runner = FakeProcessRunner.Throwing(new InvalidOperationException("secreto interno"));
        var operation = new ClamAvElevatedOperation(
            new ExecutableLocator(temp.DirectoryPath), runner, installDirectories: []);

        var result = await operation.ExecuteAsync(new OperationContext(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(BridgeErrorCodes.OperationFailed, result.ErrorCode);
        Assert.Equal("La operación elevada falló.", result.ErrorMessage);
        Assert.DoesNotContain("secreto interno", result.ErrorMessage);
        Assert.DoesNotContain("   at ", result.ErrorMessage);
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        using var temp = new TempExecutablePath();
        temp.Add("clamscan.exe");
        temp.Add("freshclam.exe");
        var runner = FakeProcessRunner.Throwing(new OperationCanceledException());
        var operation = new ClamAvElevatedOperation(
            new ExecutableLocator(temp.DirectoryPath), runner, installDirectories: []);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => operation.ExecuteAsync(new OperationContext(), CancellationToken.None));
    }

    [Fact]
    public async Task NonWindows_FailsNotSupported()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // misma convención que los tests elevados de wsl
        }

        var operation = new ClamAvElevatedOperation(
            installDirectories: []);

        var result = await operation.ExecuteAsync(new OperationContext(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(BridgeErrorCodes.NotSupported, result.ErrorCode);
    }

    private static OperationContext Payload(string json) =>
        new() { Payload = JsonSerializer.Deserialize<JsonElement>(json) };
}
