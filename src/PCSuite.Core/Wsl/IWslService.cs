namespace PCSuite.Core.Wsl;

/// <summary>
/// Fachada de WSL (Task 007). El frontend NUNCA envía argumentos de
/// comando: las 5 operaciones wsl.* son de payload cero y los argv son
/// arreglos fijos construidos en Infrastructure (§12/§53). Las lecturas y
/// mutaciones se serializan entre sí (una invocación wsl a la vez).
/// </summary>
public interface IWslService
{
    /// <summary>Estado: instalación, versión, distribución por defecto y running/stopped.</summary>
    Task<WslStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>Distribuciones con su estado en crudo y versión de plataforma ([] si no hay ninguna).</summary>
    Task<IReadOnlyList<WslDistro>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Actualiza el componente WSL (wsl --update); puede elevar vía UAC si hace falta.</summary>
    Task<WslMutationOutcome> UpdateAsync(CancellationToken cancellationToken = default);

    /// <summary>Apaga el subsistema WSL; jamás eleva.</summary>
    Task<WslMutationOutcome> ShutdownAsync(CancellationToken cancellationToken = default);

    /// <summary>Arranca la distribución por defecto (wsl --exec true); jamás eleva.</summary>
    Task<WslMutationOutcome> StartAsync(CancellationToken cancellationToken = default);
}
