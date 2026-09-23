using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PCSuite.Contracts;
using PCSuite.Core.Operations;
using PCSuite.Core.Processes;
using PCSuite.Core.WinGet;
using PCSuite.Infrastructure.Operations;
using PCSuite.Infrastructure.WinGet;

namespace PCSuite.Application.Tests;

/// <summary>
/// Las 8 operaciones winget a nivel de router: enrutado, formas de resultado
/// camelCase, payloads inválidos → MALFORMED_MESSAGE sin llegar a proceso y
/// RequiresElevation solo en las 4 mutaciones (metadata de dos barreras).
/// </summary>
public sealed class WinGetOperationTests
{
    // ------------------------------------------------------------------
    // Enrutado y formas de resultado
    // ------------------------------------------------------------------

    [Fact]
    public async Task ReadOperations_ReturnTypedCollections()
    {
        using var fx = WinGetFixture.Create(CreateQueryRunner());

        var detect = await Send(fx.Router, "d1", OperationIds.WingetDetect);
        Assert.True(detect.Success);
        Assert.True(detect.Result!.Value.GetProperty("installed").GetBoolean());
        Assert.False(detect.Result.Value.GetProperty("isAdministrator").GetBoolean());
        Assert.Equal("1.29.380", detect.Result.Value.GetProperty("version").GetString());

        var search = await Send(
            fx.Router, "s1", OperationIds.WingetSearch, """{"query":"visual studio code"}""");
        Assert.True(search.Success);
        var searchPackages = search.Result!.Value.GetProperty("packages");
        Assert.Equal(4, searchPackages.GetArrayLength());
        var first = searchPackages[0];
        Assert.Equal("Microsoft.VisualStudioCode", first.GetProperty("id").GetString());
        Assert.Equal("Microsoft Visual Studio Code", first.GetProperty("name").GetString());
        Assert.Equal("1.138.0", first.GetProperty("version").GetString());
        Assert.False(first.TryGetProperty("available", out _)); // solo winget.updates
        Assert.False(first.TryGetProperty("match", out _)); // sin 4ª columna

        var list = await Send(fx.Router, "l1", OperationIds.WingetList);
        Assert.True(list.Success);
        Assert.Equal(97, list.Result!.Value.GetProperty("packages").GetArrayLength());

        var updates = await Send(fx.Router, "u1", OperationIds.WingetUpdates);
        Assert.True(updates.Success);
        var updatePackages = updates.Result!.Value.GetProperty("packages");
        Assert.Equal(6, updatePackages.GetArrayLength());
        Assert.Contains(
            updatePackages.EnumerateArray(),
            item => item.GetProperty("id").GetString() == "MSYS2.MSYS2"
                && item.GetProperty("available").GetString() == "20260611");
    }

    [Fact]
    public async Task MutationOperations_ReturnStructuredResults()
    {
        using var fx = WinGetFixture.Create(CreateQueryRunner());

        var install = await Send(
            fx.Router, "i1", OperationIds.WingetInstall, """{"packageId":"7zip.7zip"}""");
        Assert.True(install.Success);
        var installData = install.Result!.Value;
        Assert.True(installData.GetProperty("succeeded").GetBoolean());
        Assert.Equal("7zip.7zip", installData.GetProperty("packageId").GetString());
        Assert.False(installData.GetProperty("elevated").GetBoolean());
        Assert.Equal(0, installData.GetProperty("exitCode").GetInt32());
        Assert.False(installData.TryGetProperty("detail", out _));

        var upgrade = await Send(
            fx.Router, "up1", OperationIds.WingetUpgrade, """{"packageId":"Git.Git"}""");
        Assert.True(upgrade.Success);
        Assert.Equal("Git.Git", upgrade.Result!.Value.GetProperty("packageId").GetString());
        Assert.True(upgrade.Result.Value.GetProperty("succeeded").GetBoolean());

        var upgradeAll = await Send(fx.Router, "ua1", OperationIds.WingetUpgradeAll);
        Assert.True(upgradeAll.Success);
        var upgradeAllData = upgradeAll.Result!.Value;
        Assert.True(upgradeAllData.GetProperty("succeeded").GetBoolean());
        Assert.False(upgradeAllData.TryGetProperty("packageId", out _));

        var uninstall = await Send(
            fx.Router, "un1", OperationIds.WingetUninstall, """{"packageId":"7zip.7zip"}""");
        Assert.True(uninstall.Success);
        Assert.True(uninstall.Result!.Value.GetProperty("succeeded").GetBoolean());
    }

    [Fact]
    public async Task ControlledFailure_SurfacesItsCode_NotInternalError()
    {
        // winget falla con un código desconocido → el WinGetException del
        // servicio debe llegar al frontend con su código, no INTERNAL_ERROR.
        using var fx = WinGetFixture.Create(FakeProcessRunner.Returning("", 4711));

        var response = await Send(fx.Router, "f1", OperationIds.WingetList);

        Assert.False(response.Success);
        Assert.Equal(BridgeErrorCodes.WingetFailed, response.Error!.Code);
        Assert.DoesNotContain("   at ", response.Error.Message);
    }

    // ------------------------------------------------------------------
    // Seguridad de payloads (§10.1)
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(OperationIds.WingetSearch, "\"texto plano\"")]
    [InlineData(OperationIds.WingetInstall, "[1,2]")]
    public async Task NonObjectPayload_FailsMalformed_WithoutAnyProcess(
        string operation,
        string payloadJson)
    {
        using var fx = WinGetFixture.Create(FakeProcessRunner.Returning(""));

        var response = await Send(fx.Router, "r1", operation, payloadJson);

        Assert.False(response.Success);
        Assert.Equal(BridgeErrorCodes.MalformedMessage, response.Error!.Code);
        Assert.Empty(fx.Runner.Requests);
    }

    [Theory]
    [InlineData(OperationIds.WingetSearch, """{"query":""}""")]
    [InlineData(OperationIds.WingetSearch, """{"query":"-borrar"}""")]
    [InlineData(OperationIds.WingetInstall, """{"packageId":"id con espacios"}""")]
    [InlineData(OperationIds.WingetUninstall, """{"packageId":"-lead"}""")]
    public async Task InvalidInput_FailsMalformed_WithoutAnyProcess(
        string operation,
        string payloadJson)
    {
        using var fx = WinGetFixture.Create(FakeProcessRunner.Returning(""));

        var response = await Send(fx.Router, "r1", operation, payloadJson);

        Assert.False(response.Success);
        Assert.Equal(BridgeErrorCodes.MalformedMessage, response.Error!.Code);
        Assert.DoesNotContain("   at ", response.Error.Message);
        Assert.Empty(fx.Runner.Requests);
        Assert.Empty(fx.Elevation.Requests);
    }

    [Fact]
    public async Task Search_WithoutPayload_FailsMalformed()
    {
        using var fx = WinGetFixture.Create(FakeProcessRunner.Returning(""));

        var response = await Send(fx.Router, "np1", OperationIds.WingetSearch);

        Assert.False(response.Success);
        Assert.Equal(BridgeErrorCodes.MalformedMessage, response.Error!.Code);
        Assert.Empty(fx.Runner.Requests);
    }

    [Fact]
    public async Task UnknownOperation_ReturnsOperationNotFound()
    {
        using var fx = WinGetFixture.Create(FakeProcessRunner.Returning(""));

        var response = await Send(fx.Router, "x1", "winget.exec");

        Assert.False(response.Success);
        Assert.Equal(BridgeErrorCodes.OperationNotFound, response.Error!.Code);
        Assert.Empty(fx.Runner.Requests);
    }

    // ------------------------------------------------------------------
    // Metadata de elevación (dos barreras con ElevatedOperationIds)
    // ------------------------------------------------------------------

    [Fact]
    public void OnlyMutationOperations_DeclareElevation()
    {
        using var fx = WinGetFixture.Create(FakeProcessRunner.Returning(""));

        var queryOperations = new WinGetOperation[]
        {
            new WinGetDetectOperation(fx.Service),
            new WinGetSearchOperation(fx.Service),
            new WinGetListOperation(fx.Service),
            new WinGetUpdatesOperation(fx.Service),
        };

        var mutationOperations = new WinGetOperation[]
        {
            new WinGetInstallOperation(fx.Service),
            new WinGetUpgradeOperation(fx.Service),
            new WinGetUpgradeAllOperation(fx.Service),
            new WinGetUninstallOperation(fx.Service),
        };

        // Solo las mutaciones declaran elevación (nunca las de solo lectura).
        Assert.All(queryOperations, operation => Assert.False(operation.RequiresElevation));
        Assert.All(mutationOperations, operation => Assert.True(operation.RequiresElevation));

        // Las 4 con elevación mapean a IDs de la lista blanca.
        Assert.Equal(
            new[]
            {
                ElevatedOperationIds.WingetInstall,
                ElevatedOperationIds.WingetUpgrade,
                ElevatedOperationIds.WingetUpgradeAll,
                ElevatedOperationIds.WingetUninstall,
            },
            mutationOperations
                .Where(operation =>
                    operation.Id is OperationIds.WingetInstall
                        or OperationIds.WingetUpgrade
                        or OperationIds.WingetUpgradeAll
                        or OperationIds.WingetUninstall)
                .Select(operation => operation.Id switch
                {
                    OperationIds.WingetInstall => ElevatedOperationIds.WingetInstall,
                    OperationIds.WingetUpgrade => ElevatedOperationIds.WingetUpgrade,
                    OperationIds.WingetUpgradeAll => ElevatedOperationIds.WingetUpgradeAll,
                    _ => ElevatedOperationIds.WingetUninstall,
                })
                .ToArray());
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

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

    /// <summary>Runner que responde según el argv: consultas reales, mutaciones exitosas.</summary>
    private static FakeProcessRunner CreateQueryRunner() =>
        new((request, _) =>
        {
            var args = request.Arguments;
            string output;
            if (args[0] == "--version")
            {
                output = "v1.29.380";
            }
            else if (args[0] == "search")
            {
                output = WinGetFixtures.Read("winget-search.txt");
            }
            else if (args[0] == "list")
            {
                output = WinGetFixtures.Read("winget-list.txt");
            }
            else if (args[0] == "upgrade" && !args.Contains("--id") && !args.Contains("--all"))
            {
                output = WinGetFixtures.Read("winget-upgrade.txt");
            }
            else
            {
                output = string.Empty;
            }

            return Task.FromResult(new ProcessResult { ExitCode = 0, StandardOutput = output });
        });
}
