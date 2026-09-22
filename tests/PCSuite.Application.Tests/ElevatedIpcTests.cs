using System.IO.Pipes;
using System.Text;
using PCSuite.Contracts;
using PCSuite.Infrastructure.Elevation;

namespace PCSuite.Application.Tests;

/// <summary>
/// Intercambio IPC real por pipe nombrado SIN UAC: el servidor corre en el
/// mismo proceso (hilo aparte) y el cliente es el auténtico cliente del
/// ElevationManager. Cobertura de identidad, nonce, allow-list, framing y tope.
/// </summary>
public sealed class ElevatedIpcTests
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ServerTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan TestDeadline = TimeSpan.FromSeconds(30);

    private static string NewPipeName() => $"pcsuite-test-{Guid.NewGuid():N}";

    private static ElevatedIpcServer NewServer() => new(NewRegistry(), ServerTimeout);

    private static ElevatedOperationRegistry NewRegistry()
    {
        var registry = new ElevatedOperationRegistry();
        registry.Register(new AdminInfoElevatedOperation());
        return registry;
    }

    [Fact]
    public async Task IntercambioValido_ExecutaAdminInfoYDevuelveDatos()
    {
        var pipeName = NewPipeName();
        var nonce = ElevatedProtocol.NewNonce();

        var serverTask = NewServer().ServeAsync(pipeName, nonce, Environment.UserName, CancellationToken.None);

        var response = await ElevatedPipeClient.ExchangeAsync(
            pipeName,
            new ElevatedRequest { Nonce = nonce, Operation = ElevatedOperationIds.AdminInfo },
            ConnectTimeout,
            CancellationToken.None).WaitAsync(TestDeadline);

        Assert.True(response.Success, response.Error?.Message);
        Assert.Equal(Environment.MachineName, response.Result!.Value.GetProperty("machineName").GetString());

        var serverResponse = await serverTask.WaitAsync(TestDeadline);
        Assert.True(serverResponse.Success);
    }

    [Fact]
    public async Task NonceIncorrecto_ReturnUnauthorized()
    {
        var pipeName = NewPipeName();
        var serverNonce = ElevatedProtocol.NewNonce();

        var serverTask = NewServer().ServeAsync(pipeName, serverNonce, Environment.UserName, CancellationToken.None);

        var response = await ElevatedPipeClient.ExchangeAsync(
            pipeName,
            new ElevatedRequest { Nonce = ElevatedProtocol.NewNonce(), Operation = ElevatedOperationIds.AdminInfo },
            ConnectTimeout,
            CancellationToken.None).WaitAsync(TestDeadline);

        Assert.False(response.Success);
        Assert.Equal(BridgeErrorCodes.Unauthorized, response.Error!.Code);
        await serverTask.WaitAsync(TestDeadline);
    }

    [Fact]
    public async Task OperacionNoRegistrada_ReturnOperationNotFound()
    {
        var pipeName = NewPipeName();
        var nonce = ElevatedProtocol.NewNonce();

        var serverTask = NewServer().ServeAsync(pipeName, nonce, Environment.UserName, CancellationToken.None);

        var response = await ElevatedPipeClient.ExchangeAsync(
            pipeName,
            new ElevatedRequest { Nonce = nonce, Operation = "elevated.inventada" },
            ConnectTimeout,
            CancellationToken.None).WaitAsync(TestDeadline);

        Assert.False(response.Success);
        Assert.Equal(BridgeErrorCodes.OperationNotFound, response.Error!.Code);
        await serverTask.WaitAsync(TestDeadline);
    }

    [Fact]
    public async Task ClienteConOtraIdentidad_ReturnUnauthorized()
    {
        var pipeName = NewPipeName();
        var nonce = ElevatedProtocol.NewNonce();

        // El servidor exige un usuario que NO es el que conecta.
        var serverTask = NewServer().ServeAsync(
            pipeName,
            nonce,
            "otro-usuario-inesperado",
            CancellationToken.None);

        var response = await ElevatedPipeClient.ExchangeAsync(
            pipeName,
            new ElevatedRequest { Nonce = nonce, Operation = ElevatedOperationIds.AdminInfo },
            ConnectTimeout,
            CancellationToken.None).WaitAsync(TestDeadline);

        Assert.False(response.Success);
        Assert.Equal(BridgeErrorCodes.Unauthorized, response.Error!.Code);
        Assert.Contains("Identidad", response.Error!.Message);
        await serverTask.WaitAsync(TestDeadline);
    }

    [Fact]
    public async Task MensajeCrudoSinJson_ReturnMalformed()
    {
        var pipeName = NewPipeName();
        var nonce = ElevatedProtocol.NewNonce();

        var serverTask = NewServer().ServeAsync(pipeName, nonce, Environment.UserName, CancellationToken.None);

        var response = await RawExchangeAsync(pipeName, "esto no es json");

        Assert.False(response.Success);
        Assert.Equal(BridgeErrorCodes.MalformedMessage, response.Error!.Code);
        await serverTask.WaitAsync(TestDeadline);
    }

    [Fact]
    public async Task MensajeCrudoSinNonce_ReturnUnauthorized()
    {
        var pipeName = NewPipeName();
        var nonce = ElevatedProtocol.NewNonce();

        var serverTask = NewServer().ServeAsync(pipeName, nonce, Environment.UserName, CancellationToken.None);

        var response = await RawExchangeAsync(pipeName, """{"operation":"elevated.adminInfo"}""");

        Assert.False(response.Success);
        Assert.Equal(BridgeErrorCodes.Unauthorized, response.Error!.Code);
        await serverTask.WaitAsync(TestDeadline);
    }

    [Fact]
    public async Task MensajeCrudoSobreElTope_ReturnMalformed_SinParsear()
    {
        var pipeName = NewPipeName();
        var nonce = ElevatedProtocol.NewNonce();

        var serverTask = NewServer().ServeAsync(pipeName, nonce, Environment.UserName, CancellationToken.None);

        // Por encima de 1 MiB y sin newline: el servidor debe rechazarlo sin
        // intentar parsearlo (los tests escriben bytes crudos a propósito).
        var oversized = new string('a', ElevatedProtocol.MaxMessageBytes + 16);
        var response = await RawExchangeAsync(pipeName, oversized, enforceProtocolCap: false);

        Assert.False(response.Success);
        Assert.Equal(BridgeErrorCodes.MalformedMessage, response.Error!.Code);
        await serverTask.WaitAsync(TestDeadline);
    }

    [Fact]
    public async Task ClienteDesconectadoSinEnviar_ServidorFallaControlado()
    {
        var pipeName = NewPipeName();
        var nonce = ElevatedProtocol.NewNonce();

        var serverTask = NewServer().ServeAsync(pipeName, nonce, Environment.UserName, CancellationToken.None);

        using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            await Task.Run(() => client.Connect((int)ConnectTimeout.TotalMilliseconds));
        }

        // El servidor nunca recibe request: debe terminar con un fallo
        // controlado (no colgado, no excepción).
        var serverResponse = await serverTask.WaitAsync(TestDeadline);
        Assert.False(serverResponse.Success);
        Assert.NotNull(serverResponse.Error);
    }

    /// <summary>Escribe un mensaje crudo (sin validar con el protocolo) y devuelve la respuesta.</summary>
    private static async Task<ElevatedResponse> RawExchangeAsync(
        string pipeName,
        string rawRequest,
        bool enforceProtocolCap = true)
    {
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await Task.Run(() => client.Connect((int)ConnectTimeout.TotalMilliseconds));

        if (enforceProtocolCap)
        {
            await ElevatedProtocol.WriteMessageAsync(client, rawRequest, CancellationToken.None);
        }
        else
        {
            var bytes = Encoding.UTF8.GetBytes(rawRequest + "\n");
            await client.WriteAsync(bytes);
            await client.FlushAsync();
        }

        var line = await ElevatedProtocol.ReadMessageAsync(client, CancellationToken.None);
        Assert.NotNull(line);
        Assert.True(
            ElevatedProtocol.TryParseResponse(line!, out var response, out var error),
            error?.Message ?? "La respuesta del servidor no forma parte del protocolo.");

        return response!;
    }
}
