using System.Text.Json;
using PCSuite.Contracts;
using PCSuite.Core.Elevation;
using PCSuite.Core.Operations;
using PCSuite.Infrastructure.Elevation;

namespace PCSuite.Application.Tests;

/// <summary>Lista blanca del registro elevado y validación de payload de la operación PoC.</summary>
public sealed class ElevatedOperationTests
{
    private static ElevatedOperationRegistry NewRegistry()
    {
        var registry = new ElevatedOperationRegistry();
        registry.Register(new AdminInfoElevatedOperation());
        return registry;
    }

    [Fact]
    public void Allowed_ContieneLaOperacionPoC()
    {
        Assert.Contains(ElevatedOperationIds.AdminInfo, ElevatedOperationIds.Allowed);
    }

    [Fact]
    public void Register_IdFueraDeLaListaBlanca_Lanza_YNoRegistraNada()
    {
        var registry = new ElevatedOperationRegistry();

        var exception = Assert.Throws<ArgumentException>(
            () => registry.Register(new StubElevatedOperation("elevated.inventada")));

        Assert.Contains("lista blanca", exception.Message);
        Assert.Null(registry.Get("elevated.inventada"));
    }

    [Fact]
    public void Register_Duplicado_Lanza()
    {
        var registry = NewRegistry();

        var exception = Assert.Throws<ArgumentException>(
            () => registry.Register(new AdminInfoElevatedOperation()));

        Assert.Contains("registrada", exception.Message);
    }

    [Fact]
    public void Register_SinId_Lanza()
    {
        var registry = new ElevatedOperationRegistry();
        Assert.Throws<ArgumentException>(() => registry.Register(new StubElevatedOperation("")));
    }

    [Fact]
    public void Register_Null_Lanza()
    {
        var registry = new ElevatedOperationRegistry();
        Assert.Throws<ArgumentNullException>(() => registry.Register(null!));
    }

    [Fact]
    public void Get_PorId_Conocido_Y_Desconocido()
    {
        var registry = NewRegistry();

        Assert.NotNull(registry.Get(ElevatedOperationIds.AdminInfo));
        Assert.Null(registry.Get("elevated.inventada"));
        Assert.Null(registry.Get(null));
    }

    [Fact]
    public async Task AdminInfo_SinPayload_DevuelveDatos_YOmiteTokenGroups()
    {
        var operation = new AdminInfoElevatedOperation();

        var result = await operation.ExecuteAsync(new OperationContext(), CancellationToken.None);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(ElevatedOperationIds.AdminInfo, operation.Id);

        var data = result.Data!.Value;
        Assert.Equal(Environment.MachineName, data.GetProperty("machineName").GetString());
        Assert.Equal(Environment.ProcessId, data.GetProperty("processId").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("user").GetString()));
        Assert.True(
            data.GetProperty("isAdministrator").ValueKind is JsonValueKind.True or JsonValueKind.False);

        // No se pidieron grupos: con WhenWritingNull la propiedad se omite.
        Assert.False(data.TryGetProperty("tokenGroups", out _));
    }

    [Fact]
    public async Task AdminInfo_ConTokenGroups_IncluyeGruposDelToken()
    {
        using var payload = JsonDocument.Parse("""{"includeTokenGroups":true}""");
        var operation = new AdminInfoElevatedOperation();

        var result = await operation.ExecuteAsync(
            new OperationContext { Payload = payload.RootElement },
            CancellationToken.None);

        Assert.True(result.Success, result.ErrorMessage);

        var groups = result.Data!.Value.GetProperty("tokenGroups");
        Assert.Equal(JsonValueKind.Array, groups.ValueKind);
        Assert.True(groups.GetArrayLength() > 0);
        Assert.False(string.IsNullOrWhiteSpace(groups[0].GetString()));
    }

    [Theory]
    [InlineData("42")]
    [InlineData("[1,2]")]
    [InlineData("\"texto\"")]
    [InlineData("true")]
    public async Task AdminInfo_PayloadQueNoEsObjeto_FaltaMalformed_SinStackTraces(string raw)
    {
        using var payload = JsonDocument.Parse(raw);
        var operation = new AdminInfoElevatedOperation();

        var result = await operation.ExecuteAsync(
            new OperationContext { Payload = payload.RootElement },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(BridgeErrorCodes.MalformedMessage, result.ErrorCode);
        Assert.DoesNotContain("Exception", result.ErrorMessage ?? "");
        Assert.DoesNotContain("   at ", result.ErrorMessage ?? "");
    }

    [Fact]
    public async Task AdminInfo_CampoDeTipoInvalido_FallaMalformed()
    {
        using var payload = JsonDocument.Parse("""{"includeTokenGroups":"yes"}""");
        var operation = new AdminInfoElevatedOperation();

        var result = await operation.ExecuteAsync(
            new OperationContext { Payload = payload.RootElement },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(BridgeErrorCodes.MalformedMessage, result.ErrorCode);
    }

    private sealed class StubElevatedOperation(string id) : IElevatedOperation
    {
        public string Id => id;

        public Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult.Ok());
    }
}
