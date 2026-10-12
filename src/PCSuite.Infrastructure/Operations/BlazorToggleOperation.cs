using PCSuite.Contracts;
using PCSuite.Core.Operations;

namespace PCSuite.Infrastructure.Operations;

/// <summary>
/// blazor.toggle — alterna el piloto Blazor WebView (Task 015).
/// Solo disponible cuando MainForm pudo inicializarlo; sin payload.
/// </summary>
public sealed class BlazorToggleOperation(Action toggle) : IOperation
{
    public string Id => OperationIds.BlazorToggle;

    public bool RequiresElevation => false;

    public Task<OperationResult> ExecuteAsync(
        OperationContext context,
        CancellationToken cancellationToken)
    {
        toggle();
        return Task.FromResult(OperationResult.Ok());
    }
}
