using PCSuite.Contracts;
using PCSuite.Core.Processes;
using PCSuite.Infrastructure.Operations;

namespace PCSuite.Application.Tests;

/// <summary>
/// Las 5 operaciones wsl a nivel de router: enrutado, formas de resultado
/// camelCase, payload hostil ignorado (payload cero: nunca se parsea → nunca
/// MALFORMED), errores controlados (nunca INTERNAL_ERROR ni stack traces) y
/// RequiresElevation solo en wsl.update.
/// </summary>
public sealed class WslOperationTests
{
    // ------------------------------------------------------------------
    // Enrutado y formas de resultado
    // ------------------------------------------------------------------

    [Fact]
    public async Task Status_ReturnsTypedResult_CamelCase()
    {
        using var fx = WslFixture.Create(CreateArgvAwareRunner());

        var response = await Send(fx.Router, "s1", OperationIds.WslStatus);

        Assert.True(response.Success);
        var data = response.Result!.Value;
        Assert.True(data.GetProperty("installed").GetBoolean());
        Assert.Equal(fx.WslPath, data.GetProperty("path").GetString());
        Assert.Equal("stopped", data.GetProperty("state").GetString());
        Assert.Equal("2.7.14.0", data.GetProperty("version").GetString());
        Assert.Equal("docker-desktop", data.GetProperty("defaultDistro").GetString());
    }

    [Fact]
    public async Task List_ReturnsDistributions_CamelCase()
    {
        using var fx = WslFixture.Create(
            FakeProcessRunner.Returning(WslFixtures.Read("wsl-list.txt")));

        var response = await Send(fx.Router, "l1", OperationIds.WslList);

        Assert.True(response.Success);
        var distros = response.Result!.Value.GetProperty("distributions");
        var distro = Assert.Single(distros.EnumerateArray());
        Assert.Equal("docker-desktop", distro.GetProperty("name").GetString());
        Assert.Equal("Stopped", distro.GetProperty("state").GetString());
        Assert.Equal(2, distro.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task Mutation_ReturnsStructuredResult_CamelCase()
    {
        using var fx = WslFixture.Create(FakeProcessRunner.Returning(""));

        var response = await Send(fx.Router, "u1", OperationIds.WslUpdate);

        Assert.True(response.Success);
        var data = response.Result!.Value;
        Assert.True(data.GetProperty("succeeded").GetBoolean());
        Assert.Equal(0, data.GetProperty("exitCode").GetInt32());
        Assert.False(data.GetProperty("elevated").GetBoolean());
        Assert.False(data.TryGetProperty("detail", out _)); // sin salida → sin detalle
    }

    // ------------------------------------------------------------------
    // Payload cero (§12/§53): jamás argumentos arbitrarios
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(OperationIds.WslUpdate, """{"command":"--shutdown; rm -rf C:\\"}""")]
    [InlineData(OperationIds.WslShutdown, """{"args":["--update","--distro","x"]}""")]
    [InlineData(OperationIds.WslStart, "\"wsl --exec calc\"")]
    public async Task HostilePayload_IsIgnored_FixedArgvStillUsed(string operation, string payloadJson)
    {
        using var fx = WslFixture.Create(FakeProcessRunner.Returning(""));

        var response = await Send(fx.Router, "h1", operation, payloadJson);

        // Payload cero: ni se parsea (sin MALFORMED) ni llega al argv.
        Assert.True(response.Success);
        var request = Assert.Single(fx.Runner.Requests);
        var expected = operation switch
        {
            OperationIds.WslUpdate => new[] { "--update" },
            OperationIds.WslShutdown => new[] { "--shutdown" },
            _ => new[] { "--exec", "true" },
        };
        Assert.Equal<string>(expected, request.Arguments);
    }

    // ------------------------------------------------------------------
    // Errores controlados
    // ------------------------------------------------------------------

    [Fact]
    public async Task ControlledFailure_SurfacesItsCode_NotInternalError()
    {
        using var fx = WslFixture.Create(FakeProcessRunner.Returning("", 4711));

        var response = await Send(fx.Router, "f1", OperationIds.WslList);

        Assert.False(response.Success);
        Assert.Equal(BridgeErrorCodes.WslFailed, response.Error!.Code);
        Assert.Equal("wsl falló con el código de salida 4711.", response.Error.Message);
        Assert.DoesNotContain("   at ", response.Error.Message);
    }

    [Fact]
    public async Task MissingWsl_SurfacesNotInstalled_NotInternalError()
    {
        using var fx = WslFixture.Create(FakeProcessRunner.Returning(""), withWsl: false);

        var response = await Send(fx.Router, "f2", OperationIds.WslList);

        Assert.False(response.Success);
        Assert.Equal(BridgeErrorCodes.NotInstalled, response.Error!.Code);
        Assert.Equal("No se encontró wsl.exe en la PATH.", response.Error.Message);
    }

    // ------------------------------------------------------------------
    // Metadata de dos barreras
    // ------------------------------------------------------------------

    [Fact]
    public void OnlyUpdateOperation_DeclaresElevation()
    {
        using var fx = WslFixture.Create(FakeProcessRunner.Returning(""));

        var operations = new WslOperation[]
        {
            new WslStatusOperation(fx.Service),
            new WslListOperation(fx.Service),
            new WslUpdateOperation(fx.Service),
            new WslShutdownOperation(fx.Service),
            new WslStartOperation(fx.Service),
        };

        Assert.True(operations.Single(op => op.Id == OperationIds.WslUpdate).RequiresElevation);
        Assert.All(
            operations.Where(op => op.Id != OperationIds.WslUpdate),
            op => Assert.False(op.RequiresElevation));
    }

    private static Task<BridgeResponse> Send(
        CommandRouter router,
        string id,
        string operation,
        string? payloadJson = null)
    {
        var payload = payloadJson is null ? string.Empty : $",\"payload\":{payloadJson}";
        return router.RouteAsync(
            $"{{\"id\":\"{id}\",\"operation\":\"{operation}\"{payload}}}");
    }

    private static FakeProcessRunner CreateArgvAwareRunner() =>
        new((request, _) => Task.FromResult(
            new ProcessResult
            {
                ExitCode = 0,
                StandardOutput = request.Arguments[0] switch
                {
                    "--version" => WslFixtures.Read("wsl-version.txt"),
                    "--status" => WslFixtures.Read("wsl-status.txt"),
                    "--list" => WslFixtures.Read("wsl-list.txt"),
                    _ => string.Empty,
                },
            }));
}
