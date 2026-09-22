using System.Text.Json;

namespace PCSuite.Core.Operations;

/// <summary>
/// An allow-listed operation invocable from the frontend through the bridge.
/// Operations never receive arbitrary commands; only typed payloads.
/// </summary>
public interface IOperation
{
    string Id { get; }

    bool RequiresElevation { get; }

    Task<OperationResult> ExecuteAsync(
        OperationContext context,
        CancellationToken cancellationToken);
}
