using PCSuite.Contracts;
using PCSuite.Core.Elevation;

namespace PCSuite.Infrastructure.Elevation;

/// <summary>
/// Registro de operaciones del proceso elevado. Doble barrera: solo admite IDs
/// presentes en <see cref="ElevatedOperationIds.Allowed"/> y prohíbe
/// duplicados. La autorización efectiva del hijo es "estar registrada": sin
/// registro no hay ejecución, por mucho nonce válido que llegue.
/// </summary>
public sealed class ElevatedOperationRegistry
{
    private readonly Dictionary<string, IElevatedOperation> _operations = new(StringComparer.Ordinal);

    /// <exception cref="ArgumentException">ID fuera de la lista blanca o ya registrado.</exception>
    public void Register(IElevatedOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (string.IsNullOrEmpty(operation.Id))
        {
            throw new ArgumentException("La operación elevada requiere un Id no vacío.", nameof(operation));
        }

        if (!ElevatedOperationIds.Allowed.Contains(operation.Id))
        {
            throw new ArgumentException(
                $"El ID '{operation.Id}' no está en la lista blanca de operaciones elevadas.",
                nameof(operation));
        }

        if (!_operations.TryAdd(operation.Id, operation))
        {
            throw new ArgumentException(
                $"Ya hay una operación elevada registrada con el ID '{operation.Id}'.",
                nameof(operation));
        }
    }

    /// <summary>Devuelve la operación registrada o null (ID desconocido).</summary>
    public IElevatedOperation? Get(string? id) => id is null ? null : _operations.GetValueOrDefault(id);
}
