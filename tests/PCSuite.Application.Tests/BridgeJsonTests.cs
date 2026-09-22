using PCSuite.Contracts;

namespace PCSuite.Application.Tests;

public class BridgeJsonTests
{
    [Fact]
    public void ParseRequest_ValidMessage_ReturnsRequest()
    {
        var outcome = BridgeJson.ParseRequest("""{"id":"abc-123","operation":"system.info"}""");

        Assert.Null(outcome.Error);
        Assert.NotNull(outcome.Request);
        Assert.Equal("abc-123", outcome.Request!.Id);
        Assert.Equal("system.info", outcome.Request.Operation);
        Assert.Null(outcome.Request.Payload);
    }

    [Fact]
    public void ParseRequest_WithPayload_CarriesTypedPayload()
    {
        var outcome = BridgeJson.ParseRequest("""{"id":"1","operation":"demo","payload":{"value":7}}""");

        Assert.Null(outcome.Error);
        Assert.NotNull(outcome.Request?.Payload);
        Assert.Equal(7, outcome.Request!.Payload!.Value.GetProperty("value").GetInt32());
    }

    [Fact]
    public void ParseRequest_MissingOperation_ReturnsMalformedAndKeepsId()
    {
        var outcome = BridgeJson.ParseRequest("""{"id":"abc-123"}""");

        Assert.NotNull(outcome.Error);
        Assert.Equal(BridgeErrorCodes.MalformedMessage, outcome.Error!.Code);
        Assert.Equal("abc-123", outcome.Id);
    }

    [Fact]
    public void ParseRequest_MissingId_ReturnsMalformed()
    {
        var outcome = BridgeJson.ParseRequest("""{"operation":"system.info"}""");

        Assert.NotNull(outcome.Error);
        Assert.Equal(BridgeErrorCodes.MalformedMessage, outcome.Error!.Code);
        Assert.Equal("", outcome.Id);
    }

    [Fact]
    public void ParseRequest_InvalidJson_ReturnsMalformed()
    {
        var outcome = BridgeJson.ParseRequest("not json");

        Assert.NotNull(outcome.Error);
        Assert.Equal(BridgeErrorCodes.MalformedMessage, outcome.Error!.Code);
        Assert.Equal("", outcome.Id);
    }

    [Fact]
    public void ParseRequest_EmptyInput_ReturnsMalformed()
    {
        Assert.NotNull(BridgeJson.ParseRequest("").Error);
        Assert.NotNull(BridgeJson.ParseRequest("   ").Error);
        Assert.NotNull(BridgeJson.ParseRequest("null").Error);
    }

    [Fact]
    public void SerializeResponse_UsesCamelCaseWireFormat()
    {
        var json = BridgeJson.SerializeResponse(new BridgeResponse { Id = "x", Success = true });

        Assert.Contains("\"id\":\"x\"", json);
        Assert.Contains("\"success\":true", json);
        Assert.DoesNotContain("\"Success\"", json);
        Assert.DoesNotContain("\"Id\"", json);
    }

    [Fact]
    public void SerializeEvent_UsesEventField()
    {
        var json = BridgeJson.SerializeEvent(new BridgeEvent { Event = EventIds.AppReady });

        Assert.Contains("\"event\":\"app.ready\"", json);
    }
}
