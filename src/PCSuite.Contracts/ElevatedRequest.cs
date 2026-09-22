using System.Text.Json;

namespace PCSuite.Contracts;

/// <summary>
/// Único mensaje del cliente no elevado → proceso elevado, por pipe nombrado.
/// </summary>
public sealed class ElevatedRequest
{
    /// <summary>
    /// Nonce de sesión generado por el cliente y entregado al hijo por línea
    /// de comandos. Secreto: jamás se escribe en log.
    /// </summary>
    public string? Nonce { get; init; }

    /// <summary>
    /// ID de operación: debe estar en <see cref="ElevatedOperationIds.Allowed"/>
    /// y registrada en el proceso elevado.
    /// </summary>
    public string? Operation { get; init; }

    /// <summary>Payload de la operación (JSON); opcional.</summary>
    public JsonElement? Payload { get; init; }
}
