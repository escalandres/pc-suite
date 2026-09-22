using Microsoft.Extensions.Logging;
using PCSuite.Contracts;
using PCSuite.Core.Operations;

namespace PCSuite.Application;

/// <summary>
/// Routes raw bridge messages to allow-listed operations and always answers
/// with a structured response. Never throws and never leaks stack traces.
/// </summary>
public sealed class CommandRouter
{
    private readonly Dictionary<string, IOperation> _operations;
    private readonly ILogger<CommandRouter> _logger;

    public CommandRouter(IEnumerable<IOperation> operations, ILogger<CommandRouter> logger)
    {
        _operations = new Dictionary<string, IOperation>(StringComparer.Ordinal);
        foreach (var operation in operations)
        {
            if (!_operations.TryAdd(operation.Id, operation))
            {
                throw new InvalidOperationException($"Duplicate operation id '{operation.Id}'.");
            }
        }

        _logger = logger;
    }

    public async Task<BridgeResponse> RouteAsync(
        string rawMessage,
        CancellationToken cancellationToken = default)
    {
        var parsed = BridgeJson.ParseRequest(rawMessage);
        if (parsed.Error is not null)
        {
            _logger.LogWarning("Rejected bridge message {Code}: {Message}", parsed.Error.Code, parsed.Error.Message);
            return new BridgeResponse { Id = parsed.Id, Success = false, Error = parsed.Error };
        }

        var request = parsed.Request!;

        if (!_operations.TryGetValue(request.Operation, out var operation))
        {
            _logger.LogWarning("Unknown operation {Operation}", request.Operation);
            return Failure(request.Id, BridgeErrorCodes.OperationNotFound, $"Unknown operation '{request.Operation}'.");
        }

        try
        {
            var context = new OperationContext { Payload = request.Payload };
            var result = await operation.ExecuteAsync(context, cancellationToken);

            return result.Success
                ? new BridgeResponse { Id = request.Id, Success = true, Result = result.Data }
                : Failure(
                    request.Id,
                    result.ErrorCode ?? BridgeErrorCodes.OperationFailed,
                    result.ErrorMessage ?? "The operation failed.");
        }
        catch (Exception exception)
        {
            // Details go to the log; the frontend only ever sees a generic message.
            _logger.LogError(exception, "Operation {Operation} failed.", request.Operation);
            return Failure(
                request.Id,
                BridgeErrorCodes.InternalError,
                $"Operation '{request.Operation}' failed. See logs for details.");
        }
    }

    private static BridgeResponse Failure(string id, string code, string message) =>
        new()
        {
            Id = id,
            Success = false,
            Error = new BridgeError { Code = code, Message = message },
        };
}
