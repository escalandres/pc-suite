using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PCSuite.Application;
using PCSuite.Application.Tools;
using PCSuite.Contracts;
using PCSuite.Core.Operations;
using PCSuite.Infrastructure.Operations;
using PCSuite.Infrastructure.Tools;

namespace PCSuite.Application.Tests;

/// <summary>
/// Rule 7 — Tests de los endpoints del puente de tools
/// (tools.list, tools.search, tool.execute) con payloads válidos e inválidos.
/// </summary>
public class ToolOperationsTests
{
    [Fact]
    public async Task ToolsList_ReturnsAllMetadata()
    {
        var registry = CreateRegistry();
        var operation = new ToolsListOperation(registry);

        var result = await ExecuteAsync(operation, payload: null);

        Assert.True(result.Success);
        var tools = result.Data!.Value.GetProperty("tools").EnumerateArray().ToArray();
        Assert.Equal(5, tools.Length);
        Assert.Contains(tools, t => t.GetProperty("id").GetString() == "json.format");
        Assert.Contains(tools, t => t.GetProperty("id").GetString() == "password.generate");
        // Cada tool expone la metadata que necesita la UI genérica.
        Assert.All(tools, t =>
        {
            Assert.False(string.IsNullOrEmpty(t.GetProperty("name").GetString()));
            Assert.False(string.IsNullOrEmpty(t.GetProperty("description").GetString()));
            Assert.True(t.GetProperty("keywords").ValueKind == JsonValueKind.Array);
        });

        // El formulario de la UI se genera desde el esquema de parámetros:
        // toda tool del módulo declara sus parámetros con tipo y control.
        Assert.All(tools, t =>
        {
            var parameters = t.GetProperty("parameters");
            Assert.Equal(JsonValueKind.Array, parameters.ValueKind);
            Assert.All(parameters.EnumerateArray(), p =>
            {
                Assert.False(string.IsNullOrEmpty(p.GetProperty("name").GetString()));
                Assert.False(string.IsNullOrEmpty(p.GetProperty("label").GetString()));
                var type = p.GetProperty("type").GetString();
                Assert.Contains(type, new[] { "text", "textArea", "number", "boolean", "select" });
            });
        });

        // Contrato puntual: password.generate declara length (number con
        // límites) y los flags booleanos con default true.
        var password = tools.Single(t => t.GetProperty("id").GetString() == "password.generate");
        var passwordParameters = password.GetProperty("parameters").EnumerateArray().ToList();
        var length = passwordParameters.Single(p => p.GetProperty("name").GetString() == "length");
        Assert.Equal("number", length.GetProperty("type").GetString());
        Assert.Equal(8, length.GetProperty("min").GetInt32());
        Assert.Equal(128, length.GetProperty("max").GetInt32());
        Assert.True(length.GetProperty("default").GetInt32() == 16);
        Assert.All(
            passwordParameters.Where(p => p.GetProperty("type").GetString() == "boolean"),
            p => Assert.True(p.GetProperty("default").GetBoolean()));

        // Los select exponen sus opciones (hash: md5/sha1/sha256/sha512).
        var hash = tools.Single(t => t.GetProperty("id").GetString() == "hash");
        var algorithm = hash.GetProperty("parameters").EnumerateArray()
            .Single(p => p.GetProperty("name").GetString() == "algorithm");
        Assert.Equal("select", algorithm.GetProperty("type").GetString());
        Assert.Equal(
            ["md5", "sha1", "sha256", "sha512"],
            algorithm.GetProperty("options").EnumerateArray().Select(o => o.GetString()));
    }

    [Fact]
    public async Task ToolsSearch_FiltersByQuery()
    {
        var operation = new ToolsSearchOperation(new ToolSearchService(CreateRegistry()));

        var result = await ExecuteAsync(operation, """{"query":"base64"}""");

        Assert.True(result.Success);
        var tools = result.Data!.Value.GetProperty("tools").EnumerateArray().ToArray();
        Assert.Equal("base64", Assert.Single(tools).GetProperty("id").GetString());
    }

    [Fact]
    public async Task ToolsSearch_EmptyPayload_ReturnsAll()
    {
        var operation = new ToolsSearchOperation(new ToolSearchService(CreateRegistry()));

        var result = await ExecuteAsync(operation, payload: null);

        Assert.True(result.Success);
        Assert.Equal(5, result.Data!.Value.GetProperty("tools").GetArrayLength());
    }

    [Fact]
    public async Task ToolsSearch_NonObjectPayload_ReturnsMalformed()
    {
        var operation = new ToolsSearchOperation(new ToolSearchService(CreateRegistry()));

        var result = await ExecuteAsync(operation, "\"no-soy-un-objeto\"");

        Assert.False(result.Success);
        Assert.Equal(BridgeErrorCodes.MalformedMessage, result.ErrorCode);
    }

    [Fact]
    public async Task ToolExecute_RunsRegisteredTool()
    {
        var operation = new ToolExecuteOperation(CreateRegistry());
        var payload = """{"toolId":"hash","input":{"algorithm":"sha256","text":"abc"}}""";

        var result = await ExecuteAsync(operation, payload);

        Assert.True(result.Success);
        Assert.Equal("hash", result.Data!.Value.GetProperty("toolId").GetString());
        // Salida plana: el hash es el output directo.
        var hash = result.Data!.Value.GetProperty("output").GetString();
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hash);
    }

    [Fact]
    public async Task ToolExecute_UnknownTool_ReturnsOperationNotFound()
    {
        var operation = new ToolExecuteOperation(CreateRegistry());

        var result = await ExecuteAsync(operation, """{"toolId":"rm.rf","input":{}}""");

        Assert.False(result.Success);
        Assert.Equal(BridgeErrorCodes.OperationNotFound, result.ErrorCode);
        Assert.DoesNotContain("   at ", result.ErrorMessage);
    }

    [Fact]
    public async Task ToolExecute_MissingToolId_ReturnsMalformed()
    {
        var operation = new ToolExecuteOperation(CreateRegistry());

        var result = await ExecuteAsync(operation, """{"input":{}}""");

        Assert.False(result.Success);
        Assert.Equal(BridgeErrorCodes.MalformedMessage, result.ErrorCode);
        Assert.Contains("toolId", result.ErrorMessage);
    }

    [Fact]
    public async Task ToolExecute_FailingTool_PropagatesControlledError()
    {
        var operation = new ToolExecuteOperation(CreateRegistry());
        var payload = """{"toolId":"json.format","input":{"text":"{roto"}}""";

        var result = await ExecuteAsync(operation, payload);

        Assert.False(result.Success);
        Assert.Equal(BridgeErrorCodes.OperationFailed, result.ErrorCode);
        Assert.DoesNotContain("   at ", result.ErrorMessage);
        Assert.DoesNotContain("JsonException", result.ErrorMessage);
    }

    [Fact]
    public async Task ToolExecute_NonObjectPayload_ReturnsMalformed()
    {
        var operation = new ToolExecuteOperation(CreateRegistry());

        var result = await ExecuteAsync(operation, "42");

        Assert.False(result.Success);
        Assert.Equal(BridgeErrorCodes.MalformedMessage, result.ErrorCode);
    }

    [Fact]
    public async Task Router_RoutesToolOperations_ThroughBridgeProtocol()
    {
        // Extremo a extremo por el path real del puente: mensaje JSON crudo
        // → CommandRouter → operación de tools → respuesta tipada.
        var registry = CreateRegistry();
        var router = new CommandRouter(
            [
                new ToolsListOperation(registry),
                new ToolsSearchOperation(new ToolSearchService(registry)),
                new ToolExecuteOperation(registry),
            ],
            NullLogger<CommandRouter>.Instance);

        var list = await router.RouteAsync("""{"id":"r1","operation":"tools.list"}""");
        Assert.True(list.Success);
        Assert.Equal(5, list.Result!.Value.GetProperty("tools").GetArrayLength());

        var search = await router.RouteAsync(
            """{"id":"r2","operation":"tools.search","payload":{"query":"password"}}""");
        Assert.True(search.Success);
        Assert.Equal(
            "password.generate",
            Assert.Single(search.Result!.Value.GetProperty("tools").EnumerateArray())
                .GetProperty("id").GetString());

        var execute = await router.RouteAsync(
            """{"id":"r3","operation":"tool.execute","payload":{"toolId":"uuid.generate","input":{"count":2}}}""");
        Assert.True(execute.Success);
        // Salida plana: el array de UUIDs es el output directo.
        Assert.Equal(2, execute.Result!.Value.GetProperty("output").GetArrayLength());

        var unknown = await router.RouteAsync(
            """{"id":"r4","operation":"tool.execute","payload":{"toolId":"no.existe"}}""");
        Assert.False(unknown.Success);
        Assert.Equal(BridgeErrorCodes.OperationNotFound, unknown.Error!.Code);
    }

    private static ToolRegistry CreateRegistry()
    {
        var registry = new ToolRegistry();
        registry.RegisterModule(new ToolsModule());
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
}
