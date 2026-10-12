using Microsoft.Extensions.Logging.Abstractions;
using PCSuite.Application.Operations;
using PCSuite.Contracts;
using PCSuite.Core.Operations;

namespace PCSuite.Application.Tests;

/// <summary>
/// explorer.openUrl por el puente: validación estricta de la URL.
/// Solo se ejercitan las rutas de RECHAZO — el camino de éxito ejecuta
/// Process.Start y abriría un navegador real en la máquina de CI.
/// El contrato de seguridad clave: con UseShellExecute=true cualquier
/// esquema que no sea http/https podría ejecutar protocolos registrados
/// del sistema (file:, ms-settings:…), así que deben rechazarse todos.
/// </summary>
public class ExplorerOpenUrlOperationTests
{
    private static CommandRouter Router() =>
        new([new ExplorerOpenUrlOperation()], NullLogger<CommandRouter>.Instance);

    private static Task<BridgeResponse> Send(string id, string? payloadJson = null)
    {
        var router = Router();
        var payload = payloadJson is null ? string.Empty : $",\"payload\":{payloadJson}";
        return router.RouteAsync(
            $"{{\"id\":\"{id}\",\"operation\":\"{OperationIds.ExplorerOpenUrl}\"{payload}}}");
    }

    [Fact]
    public async Task MissingPayload_Fails()
    {
        var response = await Send("u1");

        Assert.False(response.Success);
        Assert.Equal("error", response.Error!.Code);
    }

    [Fact]
    public async Task EmptyUrl_Fails()
    {
        var response = await Send("u2", """{"url":"   "}""");

        Assert.False(response.Success);
        Assert.Equal("error", response.Error!.Code);
    }

    [Theory]
    [InlineData("""{"url":"not-a-url"}""")]
    [InlineData("""{"url":"/relativa/sin/esquema"}""")]
    [InlineData("""{"url":"file://C:/Windows/System32/calc.exe"}""")]
    [InlineData("""{"url":"ms-settings:display"}""")]
    [InlineData("""{"url":"shell:Downloads"}""")]
    public async Task NonHttpAbsoluteUrl_IsRejected(string payloadJson)
    {
        var response = await Send("u3", payloadJson);

        Assert.False(response.Success);
        Assert.Equal("invalid_url", response.Error!.Code);
    }

    [Fact]
    public void Metadata_IsReadOnly_AndNeverElevates()
    {
        var operation = new ExplorerOpenUrlOperation();

        Assert.Equal(OperationIds.ExplorerOpenUrl, operation.Id);
        Assert.False(operation.RequiresElevation);
    }
}
