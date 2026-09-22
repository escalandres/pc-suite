using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PCSuite.Application;
using PCSuite.Application.Developers;
using PCSuite.Contracts;
using PCSuite.Core.Developers;
using PCSuite.Core.Operations;
using PCSuite.Infrastructure.Operations;

namespace PCSuite.Application.Tests;

/// <summary>
/// Rule 7 — Tests de los endpoints del puente de herramientas de
/// desarrollador (developer.list, developer.checkUpdate, developer.update,
/// developer.launch) con payloads válidos e inválidos. Los proveedores se
/// fanean: ningún test depende de herramientas instaladas en el equipo.
/// </summary>
public class DeveloperOperationTests
{
    [Fact]
    public async Task DeveloperList_ReturnsStatusOfAllRegisteredTools()
    {
        var operation = new DeveloperListOperation(CreateRegistry(
            new FakeProvider { Id = "node", Name = "Node.js" },
            new FakeProvider
            {
                Id = "bun",
                Name = "Bun",
                Status = new ToolStatus { IsInstalled = false },
            }));

        var result = await ExecuteAsync(operation, payload: null);

        Assert.True(result.Success);
        var tools = result.Data!.Value.GetProperty("tools").EnumerateArray().ToArray();
        Assert.Equal(2, tools.Length);

        var node = Assert.Single(tools, t => t.GetProperty("id").GetString() == "node");
        Assert.True(node.GetProperty("isInstalled").GetBoolean());
        Assert.Equal("1.2.3", node.GetProperty("version").GetString());
        Assert.False(string.IsNullOrEmpty(node.GetProperty("path").GetString()));

        var bun = Assert.Single(tools, t => t.GetProperty("id").GetString() == "bun");
        Assert.False(bun.GetProperty("isInstalled").GetBoolean());
        // WhenWritingNull: las propiedades null se omiten del JSON del puente.
        Assert.False(bun.TryGetProperty("version", out _));
        Assert.False(bun.TryGetProperty("path", out _));
    }

    [Fact]
    public void DeveloperList_DuplicateId_ThrowsOnRegister()
    {
        var registry = new DeveloperToolRegistry();
        registry.Register(new FakeProvider { Id = "node" });

        Assert.Throws<InvalidOperationException>(() => registry.Register(new FakeProvider { Id = "node" }));
    }

    [Fact]
    public async Task DeveloperCheckUpdate_ReturnsUpdateInfo()
    {
        var operation = new DeveloperCheckUpdateOperation(CreateRegistry(
            new FakeProvider
            {
                Id = "npm",
                Update = new UpdateInfo
                {
                    CurrentVersion = "11.7.0",
                    LatestVersion = "12.1.0",
                    UpdateAvailable = true,
                },
            }));

        var result = await ExecuteAsync(operation, """{"toolId":"npm"}""");

        Assert.True(result.Success);
        var data = result.Data!.Value;
        Assert.Equal("npm", data.GetProperty("toolId").GetString());
        Assert.Equal("11.7.0", data.GetProperty("currentVersion").GetString());
        Assert.Equal("12.1.0", data.GetProperty("latestVersion").GetString());
        Assert.True(data.GetProperty("updateAvailable").GetBoolean());
        Assert.True(data.GetProperty("supported").GetBoolean());
    }

    [Fact]
    public async Task DeveloperCheckUpdate_NoSource_ReturnsDegradedNotError()
    {
        // Sin fuente de updates: comprobación degradada, no un error de puente.
        var operation = new DeveloperCheckUpdateOperation(CreateRegistry(
            new FakeProvider { Id = "node", Update = null }));

        var result = await ExecuteAsync(operation, """{"toolId":"node"}""");

        Assert.True(result.Success);
        var data = result.Data!.Value;
        Assert.False(data.GetProperty("supported").GetBoolean());
        Assert.False(data.GetProperty("updateAvailable").GetBoolean());
        // WhenWritingNull: sin versión disponible, la clave se omite.
        Assert.False(data.TryGetProperty("latestVersion", out _));
        // La versión instalada sigue reportándose para la UI.
        Assert.Equal("1.2.3", data.GetProperty("currentVersion").GetString());
    }

    [Fact]
    public async Task DeveloperCheckUpdate_MissingToolId_ReturnsMalformed()
    {
        var operation = new DeveloperCheckUpdateOperation(CreateRegistry(new FakeProvider()));

        var result = await ExecuteAsync(operation, "{}");

        Assert.False(result.Success);
        Assert.Equal(BridgeErrorCodes.MalformedMessage, result.ErrorCode);
        Assert.Contains("toolId", result.ErrorMessage);
    }

    [Fact]
    public async Task DeveloperCheckUpdate_UnknownTool_ReturnsOperationNotFound()
    {
        var operation = new DeveloperCheckUpdateOperation(CreateRegistry(new FakeProvider()));

        var result = await ExecuteAsync(operation, """{"toolId":"rm.rf"}""");

        Assert.False(result.Success);
        Assert.Equal(BridgeErrorCodes.OperationNotFound, result.ErrorCode);
        Assert.DoesNotContain("   at ", result.ErrorMessage);
    }

    [Fact]
    public async Task DeveloperCheckUpdate_NonObjectPayload_ReturnsMalformed()
    {
        var operation = new DeveloperCheckUpdateOperation(CreateRegistry(new FakeProvider()));

        var result = await ExecuteAsync(operation, "42");

        Assert.False(result.Success);
        Assert.Equal(BridgeErrorCodes.MalformedMessage, result.ErrorCode);
    }

    [Fact]
    public async Task DeveloperUpdate_Success_ReturnsVersionChanges()
    {
        var payload = BridgeJson.ToJson(new { previousVersion = "11.7.0", currentVersion = "12.1.0" });
        var operation = new DeveloperUpdateOperation(CreateRegistry(
            new FakeProvider { Id = "npm", UpdateResult = OperationResult.Ok(payload) }));

        var result = await ExecuteAsync(operation, """{"toolId":"npm"}""");

        Assert.True(result.Success);
        Assert.Equal("12.1.0", result.Data!.Value.GetProperty("currentVersion").GetString());
    }

    [Fact]
    public async Task DeveloperUpdate_ControlledFailure_PassesCodeThrough()
    {
        // NOT_SUPPORTED del proveedor llega a la UI tal cual, sin stack trace.
        var operation = new DeveloperUpdateOperation(CreateRegistry(
            new FakeProvider
            {
                Id = "node",
                UpdateResult = OperationResult.Fail(
                    BridgeErrorCodes.NotSupported,
                    "Node.js no tiene autoactualización propia."),
            }));

        var result = await ExecuteAsync(operation, """{"toolId":"node"}""");

        Assert.False(result.Success);
        Assert.Equal(BridgeErrorCodes.NotSupported, result.ErrorCode);
        Assert.Equal("Node.js no tiene autoactualización propia.", result.ErrorMessage);
        Assert.DoesNotContain("   at ", result.ErrorMessage);
    }

    [Fact]
    public async Task DeveloperUpdate_UnknownTool_ReturnsOperationNotFound()
    {
        var operation = new DeveloperUpdateOperation(CreateRegistry(new FakeProvider()));

        var result = await ExecuteAsync(operation, """{"toolId":"no.existe"}""");

        Assert.False(result.Success);
        Assert.Equal(BridgeErrorCodes.OperationNotFound, result.ErrorCode);
    }

    [Fact]
    public async Task DeveloperLaunch_Success_ReturnsOk()
    {
        var operation = new DeveloperLaunchOperation(CreateRegistry(
            new FakeProvider { Id = "node" }));

        var result = await ExecuteAsync(operation, """{"toolId":"node"}""");

        Assert.True(result.Success);
    }

    [Fact]
    public async Task DeveloperLaunch_ControlledFailure_PassesCodeThrough()
    {
        var operation = new DeveloperLaunchOperation(CreateRegistry(
            new FakeProvider
            {
                Id = "npm",
                LaunchResult = OperationResult.Fail(
                    BridgeErrorCodes.NotSupported,
                    "La herramienta npm no tiene interfaz interactiva propia."),
            }));

        var result = await ExecuteAsync(operation, """{"toolId":"npm"}""");

        Assert.False(result.Success);
        Assert.Equal(BridgeErrorCodes.NotSupported, result.ErrorCode);
        Assert.DoesNotContain("   at ", result.ErrorMessage);
    }

    [Fact]
    public async Task Router_RoutesDeveloperOperations_ThroughBridgeProtocol()
    {
        // Extremo a extremo por el path real del puente: mensaje JSON crudo
        // → CommandRouter → operación developer.* → respuesta tipada.
        var registry = CreateRegistry(
            new FakeProvider
            {
                Id = "node",
                Update = new UpdateInfo
                {
                    CurrentVersion = "1.2.3",
                    LatestVersion = "2.0.0",
                    UpdateAvailable = true,
                },
                UpdateResult = OperationResult.Fail(BridgeErrorCodes.NotSupported, "Sin autoactualización."),
            });
        var router = new CommandRouter(
            [
                new DeveloperListOperation(registry),
                new DeveloperCheckUpdateOperation(registry),
                new DeveloperUpdateOperation(registry),
                new DeveloperLaunchOperation(registry),
            ],
            NullLogger<CommandRouter>.Instance);

        var list = await router.RouteAsync("""{"id":"r1","operation":"developer.list"}""");
        Assert.True(list.Success);
        Assert.Equal("node", Assert.Single(list.Result!.Value.GetProperty("tools").EnumerateArray())
            .GetProperty("id").GetString());

        var check = await router.RouteAsync(
            """{"id":"r2","operation":"developer.checkUpdate","payload":{"toolId":"node"}}""");
        Assert.True(check.Success);
        Assert.True(check.Result!.Value.GetProperty("updateAvailable").GetBoolean());

        var update = await router.RouteAsync(
            """{"id":"r3","operation":"developer.update","payload":{"toolId":"node"}}""");
        Assert.False(update.Success);
        Assert.Equal(BridgeErrorCodes.NotSupported, update.Error!.Code);

        var unknownTool = await router.RouteAsync(
            """{"id":"r4","operation":"developer.launch","payload":{"toolId":"no.existe"}}""");
        Assert.False(unknownTool.Success);
        Assert.Equal(BridgeErrorCodes.OperationNotFound, unknownTool.Error!.Code);
    }

    private static DeveloperToolRegistry CreateRegistry(params IDeveloperToolProvider[] providers)
    {
        var registry = new DeveloperToolRegistry();
        foreach (var provider in providers)
        {
            registry.Register(provider);
        }

        return registry;
    }

    private static async Task<OperationResult> ExecuteAsync(IOperation operation, string? payload)
    {
        var context = new OperationContext
        {
            Payload = payload is null ? null : JsonSerializer.Deserialize<JsonElement>(payload),
        };

        return await operation.ExecuteAsync(context, CancellationToken.None);
    }

    /// <summary>Proveedor faneado: sin procesos ni dependencia del equipo.</summary>
    private sealed class FakeProvider : IDeveloperToolProvider
    {
        public string Id { get; init; } = "fake";

        public string Name { get; init; } = "Fake Tool";

        public ToolStatus Status { get; init; } = new()
        {
            IsInstalled = true,
            Version = "1.2.3",
            Path = @"C:\fake\tool.exe",
        };

        public UpdateInfo? Update { get; init; }

        public OperationResult UpdateResult { get; init; } = OperationResult.Ok();

        public OperationResult LaunchResult { get; init; } = OperationResult.Ok();

        public Task<ToolStatus> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Status);

        public Task<UpdateInfo?> CheckUpdateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Update);

        public Task<OperationResult> UpdateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(UpdateResult);

        public Task<OperationResult> LaunchAsync(CancellationToken cancellationToken) =>
            Task.FromResult(LaunchResult);
    }
}
