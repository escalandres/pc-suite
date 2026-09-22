using PCSuite.Core.Developers;

namespace PCSuite.Application.Developers;

/// <summary>
/// Registro central de proveedores de herramientas de desarrollador. Es la
/// única fuente de verdad para las operaciones <c>developer.*</c>: la UI solo
/// puede operar sobre ids registrados aquí, jamás sobre comandos arbitrarios.
/// </summary>
public sealed class DeveloperToolRegistry
{
    private readonly Dictionary<string, IDeveloperToolProvider> _providers = new(StringComparer.Ordinal);

    public IReadOnlyCollection<IDeveloperToolProvider> Providers => _providers.Values;

    /// <summary>
    /// Registra un proveedor. Falla en arranque si hay ids duplicados
    /// (error de configuración, no del usuario).
    /// </summary>
    public void Register(IDeveloperToolProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (!_providers.TryAdd(provider.Id, provider))
        {
            throw new InvalidOperationException($"Duplicate developer tool id '{provider.Id}'.");
        }
    }

    /// <summary>Devuelve el proveedor por id o null si no existe.</summary>
    public IDeveloperToolProvider? Get(string? toolId) =>
        !string.IsNullOrEmpty(toolId) && _providers.TryGetValue(toolId, out var provider)
            ? provider
            : null;
}
