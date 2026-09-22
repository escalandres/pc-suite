namespace PCSuite.Contracts;

/// <summary>
/// Allow-list de operaciones elevadas. El proceso NO elevado valida contra
/// esta lista ANTES de pedir UAC (ID fuera de la lista = cero prompts) y el
/// proceso elevado solo admite operaciones cuyo ID figura aquí: dos
/// barreras independientes sobre la misma lista. Tampoco se expone nunca un
/// shell genérico ni ejecución arbitraria.
/// </summary>
public static class ElevatedOperationIds
{
    /// <summary>Operación PoC de solo lectura: datos de la cuenta/proceso elevado.</summary>
    public const string AdminInfo = "elevated.adminInfo";

    /// <summary>
    /// IDs permitidos. Añadir un ID aquí solo habilita poder registrarlo;
    /// no registra nada por sí mismo.
    /// </summary>
    public static IReadOnlySet<string> Allowed { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        AdminInfo,
    };
}
