using PCSuite.Contracts;
using PCSuite.Core.Elevation;

namespace PCSuite.Application.Tests;

/// <summary>
/// <see cref="IElevationManager"/> en memoria: registra las solicitudes de
/// elevación y responde con el resultado programado. Por defecto simula "UAC
/// aceptado y elevación exitosa"; nunca muestra un UAC real (el prompt de
/// Windows jamás se simula en los tests).
/// </summary>
internal sealed class FakeElevationManager : IElevationManager
{
    public bool IsAdministrator { get; set; }

    /// <summary>Resultado que devuelve <c>RunElevatedAsync</c>.</summary>
    public ElevationResult Result { get; set; } = ElevationResult.Ok(
        BridgeJson.ToJson(new WinGetMutationResult
        {
            Succeeded = true,
            ExitCode = 0,
            Elevated = true,
        }));

    /// <summary>Solicitudes recibidas, en orden.</summary>
    public List<ElevatedOperation> Requests { get; } = [];

    public Task<bool> IsAdministratorAsync() => Task.FromResult(IsAdministrator);

    public Task<ElevationResult> RunElevatedAsync(
        ElevatedOperation operation,
        CancellationToken cancellationToken)
    {
        Requests.Add(operation);
        return Task.FromResult(Result);
    }
}
