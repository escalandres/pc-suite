using System.Text.Json;

namespace PCSuite.Core.Jobs;

/// <summary>
/// Registro y ciclo de vida de los jobs: creación, transiciones de estado,
/// progreso, historial y cancelación por job (vía su propio token).
/// Todas las respuestas son snapshots aislados; los métodos de transición son
/// idempotentes: un job en estado terminal no cambia más.
/// </summary>
public interface IJobManager
{
    /// <summary>Crea un job y lo deja en estado <see cref="JobStatus.Running"/>.</summary>
    Job Start(string type);

    void Complete(string jobId, int? exitCode = null, JsonElement? result = null);

    void Fail(string jobId, string error, int? exitCode = null);

    void MarkCancelled(string jobId, int? exitCode = null);

    /// <summary>Reporta progreso 0..1 (se ignora en jobs terminados; se recorta al rango).</summary>
    void SetProgress(string jobId, double progress);

    /// <summary>Dispara la cancelación del job. Devuelve false si no existe o ya terminó.</summary>
    bool RequestCancel(string jobId);

    /// <summary>Token de cancelación propio del job (desconocido → <see cref="CancellationToken.None"/>).</summary>
    CancellationToken GetCancellationToken(string jobId);

    Job? Get(string jobId);

    /// <summary>Historial, del más reciente al más antiguo.</summary>
    IReadOnlyList<Job> GetHistory(int limit = 100);
}
