namespace PCSuite.Core.Elevation;

/// <summary>
/// Marco de elevación POR OPERACIÓN (Task 005): la app completa corre siempre
/// con privilegios de usuario normal; solo una operación concreta de la lista
/// blanca se ejecuta en un proceso elevado de vida corta. El prompt UAC lo
/// genera Windows; esta interfaz jamás lo simula ni lo automatiza.
/// </summary>
public interface IElevationManager
{
    /// <summary>True si el proceso actual ya corre elevado (sin pedir UAC).</summary>
    Task<bool> IsAdministratorAsync();

    /// <summary>
    /// Ejecuta una única operación de la lista blanca en un proceso elevado.
    /// Los fallos de elevación no lanzan excepciones: vuelven como códigos
    /// controlados en <see cref="ElevationResult"/>. La única excepción que
    /// se propaga es la cancelación externa
    /// (<see cref="OperationCanceledException"/>).
    /// </summary>
    Task<ElevationResult> RunElevatedAsync(ElevatedOperation operation, CancellationToken cancellationToken);
}
