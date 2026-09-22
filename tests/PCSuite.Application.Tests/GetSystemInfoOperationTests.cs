using PCSuite.Contracts;
using PCSuite.Core.Operations;
using PCSuite.Infrastructure.Operations;

namespace PCSuite.Application.Tests;

public class GetSystemInfoOperationTests
{
    [Fact]
    public void Metadata_IsAllowListedAndUnprivileged()
    {
        var operation = new GetSystemInfoOperation();

        Assert.Equal(OperationIds.GetSystemInfo, operation.Id);
        Assert.False(operation.RequiresElevation);
    }

    [Fact]
    public async Task Execute_ReturnsPopulatedCamelCaseSystemInfo()
    {
        var operation = new GetSystemInfoOperation();

        var result = await operation.ExecuteAsync(new OperationContext(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);

        var data = result.Data!.Value;
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("appVersion").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("osDescription").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("runtimeVersion").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("machineName").GetString()));
        Assert.True(data.GetProperty("processorCount").GetInt32() > 0);
        Assert.True(data.TryGetProperty("isAdministrator", out _));
    }
}
