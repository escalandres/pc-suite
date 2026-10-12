using System.Text.Json;
using PCSuite.Contracts;
using PCSuite.Core.Elevation;
using PCSuite.Core.LocalShare;
using PCSuite.Core.Operations;
using PCSuite.Core.Processes;
using PCSuite.Infrastructure.LocalShare;
using PCSuite.Infrastructure.Operations;

namespace PCSuite.Application.Tests;

/// <summary>
/// Tests del módulo de firewall de Local Share (Task 016.6):
/// diagnóstico de solo lectura (sin elevación) y operación elevada
/// con puerto revalidado y argv fijo.
/// </summary>
public class LocalShareFirewallTests
{
    // ------------------------------------------------------------------
    // Diagnóstico (netsh show rule, sin elevación)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Diagnose_NetshExitCodeNoCero_NoHayRegla()
    {
        // netsh devuelve 1 cuando ninguna regla coincide (independiente del idioma).
        var runner = FakeProcessRunner.Returning("No rules match the specified criteria.", exitCode: 1);

        var result = await LocalShareFirewall.DiagnoseAsync(runner, 45100);

        Assert.False(result.RuleExists);
        Assert.False(result.PortMatches);
    }

    [Fact]
    public async Task Diagnose_ReglaConPuertoActual_Coincide()
    {
        var runner = FakeProcessRunner.Returning($"""
            Rule Name:        PCSuite Local Share
            Enabled:          Yes
            Direction:        In
            Protocol:         TCP
            LocalPort:        45100
            Program:          C:\\PCSuite\\PCSuite.exe
            """);

        var result = await LocalShareFirewall.DiagnoseAsync(runner, 45100);

        Assert.True(result.RuleExists);
        Assert.True(result.PortMatches);
    }

    [Fact]
    public async Task Diagnose_ReglaConOtroPuerto_NoCoincide()
    {
        // El usuario cambió el puerto del servicio tras crear la regla.
        var runner = FakeProcessRunner.Returning("""
            Rule Name:        PCSuite Local Share
            LocalPort:        51980
            """);

        var result = await LocalShareFirewall.DiagnoseAsync(runner, 45100);

        Assert.True(result.RuleExists);
        Assert.False(result.PortMatches);
    }

    [Fact]
    public async Task Diagnose_FalloDeNetsh_DevuelveUnknown_SinLanzar()
    {
        var runner = FakeProcessRunner.Throwing(new InvalidOperationException("boom"));

        var result = await LocalShareFirewall.DiagnoseAsync(runner, 45100);

        Assert.Null(result.RuleExists);
        Assert.False(result.PortMatches);
    }

    [Fact]
    public async Task Diagnose_UsaNetshConNombreFijoDeRegla()
    {
        var runner = FakeProcessRunner.Returning("ok");

        await LocalShareFirewall.DiagnoseAsync(runner, 45100);

        var request = Assert.Single(runner.Requests);
        Assert.Equal("netsh", request.FileName);
        Assert.Contains($"name={LocalShareFirewall.RuleName}", request.Arguments);
        Assert.DoesNotContain("add", request.Arguments);
        Assert.DoesNotContain("delete", request.Arguments);
    }

    // ------------------------------------------------------------------
    // Operación elevada: validación de payload
    // ------------------------------------------------------------------

    private static LocalShareFirewallElevatedOperation CreateOperation(
        FakeProcessRunner runner, string programPath) =>
        new(processRunner: runner, programPath: programPath);

    private static string CreateFakeProgram()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pcshare-fw-test-{Guid.NewGuid():N}.exe");
        File.WriteAllText(path, "stub");
        return path;
    }

    [Fact]
    public async Task Elevated_PayloadSinPuerto_FallaMalformed()
    {
        using var payload = JsonDocument.Parse("""{"other":true}""");
        var program = CreateFakeProgram();
        var operation = CreateOperation(FakeProcessRunner.Returning(""), program);

        var result = await operation.ExecuteAsync(
            new OperationContext { Payload = payload.RootElement },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(BridgeErrorCodes.MalformedMessage, result.ErrorCode);
        File.Delete(program);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    [InlineData(99999)]
    public async Task Elevated_PuertoFueraDeRango_Falla(int port)
    {
        using var payload = JsonDocument.Parse($$"""{"port":{{port}}}""");
        var program = CreateFakeProgram();
        var operation = CreateOperation(FakeProcessRunner.Returning(""), program);

        var result = await operation.ExecuteAsync(
            new OperationContext { Payload = payload.RootElement },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(BridgeErrorCodes.MalformedMessage, result.ErrorCode);
        File.Delete(program);
    }

    [Fact]
    public async Task Elevated_SoloBorraYAnade_Idempotente()
    {
        using var payload = JsonDocument.Parse("""{"port":45100}""");
        var program = CreateFakeProgram();
        var runner = FakeProcessRunner.Returning("");
        var operation = CreateOperation(runner, program);

        var result = await operation.ExecuteAsync(
            new OperationContext { Payload = payload.RootElement },
            CancellationToken.None);

        Assert.True(result.Success, result.ErrorMessage);

        // Dos comandos: delete (idempotencia) + add (regla nueva).
        Assert.Equal(2, runner.Requests.Count);
        Assert.All(runner.Requests, r =>
            Assert.EndsWith("netsh.exe", r.FileName, StringComparison.OrdinalIgnoreCase));

        var delete = runner.Requests[0];
        Assert.Contains("delete", delete.Arguments);
        Assert.Contains($"name={LocalShareFirewall.RuleName}", delete.Arguments);
        Assert.DoesNotContain(delete.Arguments, r => r.Contains("localport="));

        var add = runner.Requests[1];
        Assert.Contains("add", add.Arguments);
        Assert.Contains("dir=in", add.Arguments);
        Assert.Contains("action=allow", add.Arguments);
        Assert.Contains("protocol=TCP", add.Arguments);
        Assert.Contains("localport=45100", add.Arguments);
        Assert.Contains($"program={program}", add.Arguments);
        // Jamás un shell: todo argv fijo con el puerto revalidado.
        Assert.DoesNotContain(add.Arguments, r => r.Contains("cmd") || r.Contains("/c"));
        File.Delete(program);
    }

    [Fact]
    public async Task Elevated_NetshRechazaLaRegla_DevuelveFalloControlado()
    {
        using var payload = JsonDocument.Parse("""{"port":45100}""");
        var program = CreateFakeProgram();
        // El delete "funciona" (código 0) pero el add es rechazado (código 1).
        var runner = new FakeProcessRunner((_, _) =>
            Task.FromResult(new ProcessResult { ExitCode = 1, StandardOutput = "" }));
        var operation = CreateOperation(runner, program);

        var result = await operation.ExecuteAsync(
            new OperationContext { Payload = payload.RootElement },
            CancellationToken.None);

        // Éxito del transporte IPC pero Succeeded=false dentro del DTO.
        Assert.True(result.Success);
        Assert.False(result.Data!.Value.GetProperty("succeeded").GetBoolean());
        File.Delete(program);
    }

    [Fact]
    public async Task Elevated_ProgramaNoExiste_FallaSinEjecutarNada()
    {
        using var payload = JsonDocument.Parse("""{"port":45100}""");
        var runner = FakeProcessRunner.Returning("");
        var operation = CreateOperation(runner, "Z:\\no-existe\\PCSuite.exe");

        var result = await operation.ExecuteAsync(
            new OperationContext { Payload = payload.RootElement },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public void Elevated_IdEstaEnLaAllowList()
    {
        // Barrera doble: el padre valida el ID antes del UAC y el hijo al servir.
        Assert.Contains(LocalShareFirewallElevatedOperationId(), ElevatedOperationIds.Allowed);
    }

    private static string LocalShareFirewallElevatedOperationId() =>
        new LocalShareFirewallElevatedOperation().Id;
}
