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
/// Tests del módulo de firewall de Local Share (Task 016.6 + fix discovery):
/// diagnóstico de solo lectura (transferencia TCP + detección UDP) y
/// operación elevada con puerto revalidado y argv fijo.
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
        Assert.False(result.DiscoveryPortMatches);
    }

    [Fact]
    public async Task Diagnose_ReglasCompletas_TodoCoincide()
    {
        var runner = FakeProcessRunner.Returning($"""
            Rule Name:        PCSuite Local Share
            Enabled:          Yes
            Direction:        In
            Protocol:         TCP
            LocalPort:        45100
            Program:          C:\\PCSuite\\PCSuite.exe

            Rule Name:        PCSuite Local Share
            Enabled:          Yes
            Direction:        In
            Protocol:         UDP
            LocalPort:        5354
            Program:          C:\\PCSuite\\PCSuite.exe
            """);

        var result = await LocalShareFirewall.DiagnoseAsync(runner, 45100);

        Assert.True(result.RuleExists);
        Assert.True(result.PortMatches);
        Assert.True(result.DiscoveryPortMatches);
    }

    [Fact]
    public async Task Diagnose_SoloReglaTcp_ElDiscoveryQuedaCubiertoFalse()
    {
        // El estado EXACTO de las máquinas tras el build con solo-TCP:
        // la transferencia tiene regla, pero la detección multicast no.
        var runner = FakeProcessRunner.Returning($"""
            Rule Name:        PCSuite Local Share
            Protocol:        TCP
            LocalPort:        45100
            """);

        var result = await LocalShareFirewall.DiagnoseAsync(runner, 45100);

        Assert.True(result.RuleExists);
        Assert.True(result.PortMatches);
        Assert.False(result.DiscoveryPortMatches);
    }

    [Fact]
    public async Task Diagnose_ReglaConOtroPuerto_NadaCoincide()
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
    public async Task Diagnose_MatchingPorToken_NoSubstring()
    {
        // "15354" contiene "5354" como substring: el matching por token
        // exacto NO debe darlo por cubierto.
        var runner = FakeProcessRunner.Returning("""
            Rule Name:        PCSuite Local Share
            Protocol:        UDP
            LocalPort:        15354
            """);

        var result = await LocalShareFirewall.DiagnoseAsync(runner, 45100);

        Assert.True(result.RuleExists);
        Assert.False(result.DiscoveryPortMatches);
    }

    [Fact]
    public async Task Diagnose_FalloDeNetsh_DevuelveUnknown_SinLanzar()
    {
        var runner = FakeProcessRunner.Throwing(new InvalidOperationException("boom"));

        var result = await LocalShareFirewall.DiagnoseAsync(runner, 45100);

        Assert.Null(result.RuleExists);
        Assert.False(result.PortMatches);
        Assert.False(result.DiscoveryPortMatches);
    }

    [Fact]
    public async Task Diagnose_UsaNetshConNombreFijoDeRegla()
    {
        var runner = FakeProcessRunner.Returning("ok");

        await LocalShareFirewall.DiagnoseAsync(runner, 45100);

        var request = Assert.Single(runner.Requests);
        Assert.Equal("netsh", request.FileName);
        Assert.Contains($"name={LocalShareFirewall.RuleName}", request.Arguments);
        Assert.DoesNotContain(request.Arguments, r => r.Contains("add"));
        Assert.DoesNotContain(request.Arguments, r => r.Contains("delete"));
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
    public async Task Elevated_BorraYAnadeLasDosReglas_TcpYUdp()
    {
        using var payload = JsonDocument.Parse("""{"port":45100}""");
        var program = CreateFakeProgram();
        var runner = FakeProcessRunner.Returning("");
        var operation = CreateOperation(runner, program);

        var result = await operation.ExecuteAsync(
            new OperationContext { Payload = payload.RootElement },
            CancellationToken.None);

        Assert.True(result.Success, result.ErrorMessage);

        // Tres comandos: delete (idempotencia, borra TODAS las reglas del
        // nombre) + add TCP (transferencia) + add UDP (detección multicast).
        Assert.Equal(3, runner.Requests.Count);
        Assert.All(runner.Requests, r =>
            Assert.EndsWith("netsh.exe", r.FileName, StringComparison.OrdinalIgnoreCase));

        var delete = runner.Requests[0];
        Assert.Contains("delete", delete.Arguments);
        Assert.Contains($"name={LocalShareFirewall.RuleName}", delete.Arguments);

        var addTcp = runner.Requests[1];
        Assert.Contains("add", addTcp.Arguments);
        Assert.Contains("dir=in", addTcp.Arguments);
        Assert.Contains("action=allow", addTcp.Arguments);
        Assert.Contains("protocol=TCP", addTcp.Arguments);
        Assert.Contains("localport=45100", addTcp.Arguments);
        Assert.Contains($"program={program}", addTcp.Arguments);

        var addUdp = runner.Requests[2];
        Assert.Contains("add", addUdp.Arguments);
        Assert.Contains("protocol=UDP", addUdp.Arguments);
        Assert.Contains("localport=5354", addUdp.Arguments);
        Assert.Contains($"program={program}", addUdp.Arguments);
        // Jamás un shell: todo argv fijo con el puerto revalidado.
        Assert.DoesNotContain(addUdp.Arguments, r => r.Contains("cmd") || r.Contains("/c"));
        File.Delete(program);
    }

    [Fact]
    public async Task Elevated_NetshRechazaUnaRegla_DevuelveFalloControlado()
    {
        using var payload = JsonDocument.Parse("""{"port":45100}""");
        var program = CreateFakeProgram();
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
        Assert.Contains(new LocalShareFirewallElevatedOperation().Id, ElevatedOperationIds.Allowed);
    }
}
