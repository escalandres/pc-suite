using PCSuite.Core.Operations;

namespace PCSuite.Core.Elevation;

/// <summary>
/// Operación que corre DENTRO del proceso elevado. Debe ser de solo lectura
/// o con efecto mínimo, y su ID debe figurar en la lista blanca compartida
/// (<c>PCSuite.Contracts.ElevatedOperationIds</c>). Reutiliza el mismo
/// contrato de payload/resultado que las operaciones del puente.
/// </summary>
public interface IElevatedOperation
{
    /// <summary>ID estable de la operación (allow-list).</summary>
    string Id { get; }

    Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken cancellationToken);
}
