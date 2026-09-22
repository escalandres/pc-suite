using System.Text.Json;

namespace PCSuite.Core.Elevation;

/// <summary>Solicitud de una operación elevada: ID obligatorio + payload JSON opcional.</summary>
public sealed class ElevatedOperation
{
    /// <summary>Debe figurar en la lista blanca; si no, falla sin pedir UAC.</summary>
    public required string OperationId { get; init; }

    public JsonElement? Payload { get; init; }
}
