using System.Text.Json;
using PCSuite.Contracts;
using PCSuite.Core.Operations;
using PCSuite.Core.Processes;
using PCSuite.Infrastructure.Developers;
using PCSuite.Infrastructure.WinGet;

namespace PCSuite.Application.Tests;

/// <summary>
/// La mutación que corre DENTRO del proceso elevado (Task 005 + 006): IDs en
/// la allow-list, REVALIDACIÓN del packageId (segunda barrera: cero procesos
/// con entrada inválida), argv reconstruidos fijos, timeouts y resultados
/// estructurados con mensaje controlado (nunca stack traces ni salidas crudas).
/// </summary>
public sealed class WinGetElevatedOperationTests
{
    private static readonly TimeSpan MutationTimeout = TimeSpan.FromSeconds(900);

    // ------------------------------------------------------------------
    // Allow-list (dos barreras sobre ElevatedOperationIds.Allowed)
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(WinGetMutationKind.Install, ElevatedOperationIds.WingetInstall)]
    [InlineData(WinGetMutationKind.Upgrade, ElevatedOperationIds.WingetUpgrade)]
    [InlineData(WinGetMutationKind.UpgradeAll, ElevatedOperationIds.WingetUpgradeAll)]
    [InlineData(WinGetMutationKind.Uninstall, ElevatedOperationIds.WingetUninstall)]
    public void Id_MapsToAllowListedElevatedId(WinGetMutationKind kind, string expectedId)
    {
        using var temp = new TempExecutablePath();
        var operation = new WinGetElevatedOperation(
            kind,
            new ExecutableLocator(temp.DirectoryPath),
            FakeProcessRunner.Returning(string.Empty));

        Assert.Equal(expectedId, operation.Id);
        Assert.Contains(operation.Id, ElevatedOperationIds.Allowed);
    }

    // ------------------------------------------------------------------
    // argv fijos + resultado estructurado
    // ------------------------------------------------------------------

    [Fact]
    public async Task Install_RebuildsFixedArgv_Utf8_With900sTimeout()
    {
        using var temp = new TempExecutablePath();
        var wingetPath = temp.Add("winget.exe");
        var runner = FakeProcessRunner.Returning(string.Empty);
        var operation = new WinGetElevatedOperation(
            WinGetMutationKind.Install,
            new ExecutableLocator(temp.DirectoryPath),
            runner);

        var result = await operation.ExecuteAsync(
            Payload("""{"packageId":"7zip.7zip"}"""),
            CancellationToken.None);

        Assert.True(result.Success);
        var dto = result.Data!.Value;
        Assert.Equal("7zip.7zip", dto.GetProperty("packageId").GetString());
        Assert.True(dto.GetProperty("succeeded").GetBoolean());
        Assert.Equal(0, dto.GetProperty("exitCode").GetInt32());
        Assert.True(dto.GetProperty("elevated").GetBoolean());
        Assert.False(dto.TryGetProperty("detail", out _)); // sin salida → sin detalle

        var request = Assert.Single(runner.Requests);
        Assert.Equal(wingetPath, request.FileName);
        Assert.Equal<string>(
            new[]
            {
                "install", "--id", "7zip.7zip", "--exact", "--source", "winget", "--silent",
                "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity",
            },
            request.Arguments);
        Assert.Equal(MutationTimeout, request.Timeout);
        Assert.Equal("utf-8", request.OutputEncoding!.WebName);
    }

    [Theory]
    [InlineData(WinGetMutationKind.Upgrade)]
    [InlineData(WinGetMutationKind.Uninstall)]
    public async Task KindArgv_UsesVerbExactIdAndFixedFlags(WinGetMutationKind kind)
    {
        using var temp = new TempExecutablePath();
        temp.Add("winget.exe");
        var runner = FakeProcessRunner.Returning(string.Empty);
        var operation = new WinGetElevatedOperation(
            kind,
            new ExecutableLocator(temp.DirectoryPath),
            runner);

        var result = await operation.ExecuteAsync(
            Payload("""{"packageId":"Git.Git"}"""),
            CancellationToken.None);

        Assert.True(result.Success);
        string[] expected = kind == WinGetMutationKind.Upgrade
            ? new[]
            {
                "upgrade", "--id", "Git.Git", "--exact", "--source", "winget", "--silent",
                "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity",
            }
            : new[]
            {
                "uninstall", "--id", "Git.Git", "--exact", "--source", "winget", "--silent",
                "--disable-interactivity", "--accept-source-agreements",
            };

        Assert.Equal<string>(expected, Assert.Single(runner.Requests).Arguments);
    }

    [Fact]
    public async Task UpgradeAll_NeedsNoPayload_BuildsAllArgv_AndOmitsPackageId()
    {
        using var temp = new TempExecutablePath();
        temp.Add("winget.exe");
        var runner = FakeProcessRunner.Returning(string.Empty);
        var operation = new WinGetElevatedOperation(
            WinGetMutationKind.UpgradeAll,
            new ExecutableLocator(temp.DirectoryPath),
            runner);

        var result = await operation.ExecuteAsync(new OperationContext(), CancellationToken.None);

        Assert.True(result.Success);
        var dto = result.Data!.Value;
        Assert.False(dto.TryGetProperty("packageId", out _)); // upgradeAll no lleva id
        Assert.True(dto.GetProperty("elevated").GetBoolean());
        Assert.Equal<string>(
            new[]
            {
                "upgrade", "--all", "--source", "winget", "--silent",
                "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity",
            },
            Assert.Single(runner.Requests).Arguments);
    }

    // ------------------------------------------------------------------
    // Seguridad: revalidación (segunda barrera) y ausencia de shell
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("id malicioso; rm -rf")]
    [InlineData("-borrar")]
    [InlineData("id con espacios")]
    [InlineData("")]
    public async Task InvalidPackageId_Revalidated_FailsMalformed_WithoutAnyProcess(
        string packageId)
    {
        using var temp = new TempExecutablePath();
        temp.Add("winget.exe");
        var runner = FakeProcessRunner.Returning(string.Empty);
        var operation = new WinGetElevatedOperation(
            WinGetMutationKind.Install,
            new ExecutableLocator(temp.DirectoryPath),
            runner);

        var result = await operation.ExecuteAsync(
            Payload($$"""{"packageId":"{{packageId}}"}"""),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(BridgeErrorCodes.MalformedMessage, result.ErrorCode);
        Assert.Empty(runner.Requests); // nunca se crea un proceso con entrada inválida
    }

    [Fact]
    public async Task NonObjectPayload_FailsMalformed_WithoutAnyProcess()
    {
        using var temp = new TempExecutablePath();
        temp.Add("winget.exe");
        var runner = FakeProcessRunner.Returning(string.Empty);
        var operation = new WinGetElevatedOperation(
            WinGetMutationKind.Install,
            new ExecutableLocator(temp.DirectoryPath),
            runner);

        var result = await operation.ExecuteAsync(
            new OperationContext
            {
                Payload = JsonSerializer.SerializeToElement("winget install --id x"),
            },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(BridgeErrorCodes.MalformedMessage, result.ErrorCode);
        Assert.Equal("El payload debe ser un objeto JSON.", result.ErrorMessage);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task MissingPackageId_FailsMalformed_WithoutAnyProcess()
    {
        using var temp = new TempExecutablePath();
        temp.Add("winget.exe");
        var runner = FakeProcessRunner.Returning(string.Empty);
        var operation = new WinGetElevatedOperation(
            WinGetMutationKind.Install,
            new ExecutableLocator(temp.DirectoryPath),
            runner);

        var result = await operation.ExecuteAsync(new OperationContext(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(BridgeErrorCodes.MalformedMessage, result.ErrorCode);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task WithoutWinget_FailsNotInstalled_WithoutAnyProcess()
    {
        using var empty = new TempExecutablePath(); // sin winget.exe
        var runner = FakeProcessRunner.Returning(string.Empty);
        var operation = new WinGetElevatedOperation(
            WinGetMutationKind.Install,
            new ExecutableLocator(empty.DirectoryPath),
            runner);

        var result = await operation.ExecuteAsync(
            Payload("""{"packageId":"7zip.7zip"}"""),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(BridgeErrorCodes.NotInstalled, result.ErrorCode);
        Assert.Equal("No se encontró winget en la PATH.", result.ErrorMessage);
        Assert.Empty(runner.Requests);
    }

    // ------------------------------------------------------------------
    // Fallos controlados: siempre código + mensaje aptos para la UI
    // ------------------------------------------------------------------

    [Fact]
    public async Task Timeout_FailsWingetTimeout_WithControlledMessage()
    {
        using var temp = new TempExecutablePath();
        temp.Add("winget.exe");
        var runner = FakeProcessRunner.Throwing(
            new ProcessTimeoutException(TimeSpan.FromSeconds(5)));
        var operation = new WinGetElevatedOperation(
            WinGetMutationKind.Install,
            new ExecutableLocator(temp.DirectoryPath),
            runner);

        var result = await operation.ExecuteAsync(
            Payload("""{"packageId":"7zip.7zip"}"""),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(BridgeErrorCodes.WingetTimeout, result.ErrorCode);
        Assert.Equal(
            "La operación winget superó el tiempo límite y fue terminada.",
            result.ErrorMessage);
        Assert.DoesNotContain("   at ", result.ErrorMessage);
    }

    [Fact]
    public async Task CliFailure_IsStructuredResult_WithSanitizedDetail()
    {
        using var temp = new TempExecutablePath();
        temp.Add("winget.exe");
        var runner = FakeProcessRunner.Returning(
            "aviso previo\nsegunda\u0007con control",
            exitCode: 1);
        var operation = new WinGetElevatedOperation(
            WinGetMutationKind.Install,
            new ExecutableLocator(temp.DirectoryPath),
            runner);

        var result = await operation.ExecuteAsync(
            Payload("""{"packageId":"7zip.7zip"}"""),
            CancellationToken.None);

        // Un fallo del CLI NO es un error del puente: éxito con DTO.
        Assert.True(result.Success);
        var dto = result.Data!.Value;
        Assert.False(dto.GetProperty("succeeded").GetBoolean());
        Assert.Equal(1, dto.GetProperty("exitCode").GetInt32());
        Assert.True(dto.GetProperty("elevated").GetBoolean());
        Assert.Equal("segundacon control", dto.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Detail_IsTruncatedTo300Characters()
    {
        using var temp = new TempExecutablePath();
        temp.Add("winget.exe");
        var runner = FakeProcessRunner.Returning(new string('x', 400), exitCode: 1);
        var operation = new WinGetElevatedOperation(
            WinGetMutationKind.Install,
            new ExecutableLocator(temp.DirectoryPath),
            runner);

        var result = await operation.ExecuteAsync(
            Payload("""{"packageId":"7zip.7zip"}"""),
            CancellationToken.None);

        var detail = result.Data!.Value.GetProperty("detail").GetString();
        Assert.Equal(301, detail!.Length); // 300 + "…"
        Assert.EndsWith("…", detail);
    }

    [Fact]
    public async Task UnexpectedException_FailsOperationFailed_WithoutLeakingInternals()
    {
        using var temp = new TempExecutablePath();
        temp.Add("winget.exe");
        var runner = FakeProcessRunner.Throwing(
            new InvalidOperationException("detalle interno secreto"));
        var operation = new WinGetElevatedOperation(
            WinGetMutationKind.Install,
            new ExecutableLocator(temp.DirectoryPath),
            runner);

        var result = await operation.ExecuteAsync(
            Payload("""{"packageId":"7zip.7zip"}"""),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(BridgeErrorCodes.OperationFailed, result.ErrorCode);
        Assert.Equal("La operación elevada falló.", result.ErrorMessage);
        Assert.DoesNotContain("detalle interno secreto", result.ErrorMessage);
        Assert.DoesNotContain("InvalidOperationException", result.ErrorMessage);
        Assert.DoesNotContain("   at ", result.ErrorMessage);
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        using var temp = new TempExecutablePath();
        temp.Add("winget.exe");
        var runner = FakeProcessRunner.Throwing(new OperationCanceledException());
        var operation = new WinGetElevatedOperation(
            WinGetMutationKind.Install,
            new ExecutableLocator(temp.DirectoryPath),
            runner);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => operation.ExecuteAsync(
                Payload("""{"packageId":"7zip.7zip"}"""),
                CancellationToken.None));
    }

    [Fact]
    public async Task OnNonWindows_FailsNotSupported_BeforeDoingAnything()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // la guardia solo puede evaluarse fuera de Windows (CI)
        }

        var operation = new WinGetElevatedOperation(WinGetMutationKind.Install);

        var result = await operation.ExecuteAsync(
            Payload("""{"packageId":"7zip.7zip"}"""),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(BridgeErrorCodes.NotSupported, result.ErrorCode);
    }

    private static OperationContext Payload(string json) =>
        new() { Payload = JsonSerializer.Deserialize<JsonElement>(json) };
}
