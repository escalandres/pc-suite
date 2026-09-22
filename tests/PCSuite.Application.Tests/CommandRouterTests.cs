using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PCSuite.Contracts;
using PCSuite.Core.Operations;

namespace PCSuite.Application.Tests;

public class CommandRouterTests
{
    [Fact]
    public async Task RouteAsync_KnownOperation_ReturnsSuccessWithData()
    {
        var operation = new StubOperation(
            "demo.op",
            _ => Task.FromResult(OperationResult.Ok(JsonSerializer.SerializeToElement(new { cpus = 8 }))));
        var router = CreateRouter(operation);

        var response = await router.RouteAsync("""{"id":"req-1","operation":"demo.op"}""");

        Assert.True(response.Success);
        Assert.Equal("req-1", response.Id);
        Assert.NotNull(response.Result);
        Assert.Equal(8, response.Result!.Value.GetProperty("cpus").GetInt32());
    }

    [Fact]
    public async Task RouteAsync_ForwardsPayloadToOperation()
    {
        JsonElement? received = null;
        var operation = new StubOperation(
            "demo.op",
            context =>
            {
                received = context.Payload;
                return Task.FromResult(OperationResult.Ok());
            });
        var router = CreateRouter(operation);

        await router.RouteAsync("""{"id":"req-1","operation":"demo.op","payload":{"value":"hola"}}""");

        Assert.NotNull(received);
        Assert.Equal("hola", received!.Value.GetProperty("value").GetString());
    }

    [Fact]
    public async Task RouteAsync_UnknownOperation_ReturnsOperationNotFound()
    {
        var router = CreateRouter(new StubOperation("demo.op", _ => Task.FromResult(OperationResult.Ok())));

        var response = await router.RouteAsync("""{"id":"req-2","operation":"nope.op"}""");

        Assert.False(response.Success);
        Assert.Equal("req-2", response.Id);
        Assert.Equal(BridgeErrorCodes.OperationNotFound, response.Error!.Code);
    }

    [Fact]
    public async Task RouteAsync_MalformedJson_ReturnsMalformedMessage()
    {
        var router = CreateRouter();

        var response = await router.RouteAsync("this is not json");

        Assert.False(response.Success);
        Assert.Equal(BridgeErrorCodes.MalformedMessage, response.Error!.Code);
        Assert.Equal("", response.Id);
    }

    [Fact]
    public async Task RouteAsync_OperationThrows_ReturnsInternalErrorWithoutLeakingDetails()
    {
        var operation = new StubOperation(
            "boom.op",
            _ => throw new InvalidOperationException("sensitive internal detail"));
        var router = CreateRouter(operation);

        var response = await router.RouteAsync("""{"id":"req-3","operation":"boom.op"}""");

        Assert.False(response.Success);
        Assert.Equal("req-3", response.Id);
        Assert.Equal(BridgeErrorCodes.InternalError, response.Error!.Code);
        Assert.DoesNotContain("sensitive internal detail", response.Error.Message);
        Assert.DoesNotContain("InvalidOperationException", response.Error.Message);
        Assert.DoesNotContain("   at ", response.Error.Message);
    }

    [Fact]
    public async Task RouteAsync_OperationFails_PropagatesErrorCode()
    {
        var operation = new StubOperation(
            "failing.op",
            _ => Task.FromResult(OperationResult.Fail("DEMO_ERROR", "Something went wrong.")));
        var router = CreateRouter(operation);

        var response = await router.RouteAsync("""{"id":"req-4","operation":"failing.op"}""");

        Assert.False(response.Success);
        Assert.Equal("DEMO_ERROR", response.Error!.Code);
        Assert.Equal("Something went wrong.", response.Error.Message);
    }

    [Fact]
    public void Constructor_DuplicateOperationIds_Throws()
    {
        var duplicate = new StubOperation("same.op", _ => Task.FromResult(OperationResult.Ok()));

        Assert.Throws<InvalidOperationException>(() => CreateRouter(duplicate, duplicate));
    }

    private static CommandRouter CreateRouter(params IOperation[] operations) =>
        new(operations, NullLogger<CommandRouter>.Instance);

    private sealed class StubOperation(string id, Func<OperationContext, Task<OperationResult>> handler) : IOperation
    {
        public string Id => id;

        public bool RequiresElevation => false;

        public Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken cancellationToken) =>
            handler(context);
    }
}
