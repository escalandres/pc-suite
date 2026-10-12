namespace PCSuite.Contracts;

/// <summary>Metadatos de un plugin instalado.</summary>
public sealed class PluginMetadata
{
    /// <summary>Id único del plugin (clave).</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Nombre visible del plugin.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Versión actual del plugin.</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>Estado del plugin.</summary>
    public PluginStatus Status { get; init; } = PluginStatus.Unknown;

    /// <summary>Indica si hay actualización disponible.</summary>
    public bool UpdateAvailable { get; init; } = false;

    /// <summary>Versión disponible (si hay update).</summary>
    public string? AvailableVersion { get; init; }
}

/// <summary>Estado posible de un plugin.</summary>
public sealed class PluginStatus : IEquatable<PluginStatus>
{
    /// <summary>Plugin activo y funcional.</summary>
    public static PluginStatus Active => new() { Value = "active" };

    /// <summary>Plugin instalado pero desactivado.</summary>
    public static PluginStatus Inactive => new() { Value = "inactive" };

    /// <summary>Plugin con error de carga o ejecución.</summary>
    public static PluginStatus Error => new() { Value = "error" };

    /// <summary>No se pudo determinar el estado.</summary>
    public static PluginStatus Unknown => new() { Value = "unknown" };

    /// <summary>Valor subyacente del estado.</summary>
    public string Value { get; init; } = "unknown";

    /// <summary>Mensaje descriptivo del estado.</summary>
    public string? Detail { get; init; }

    public bool Equals(PluginStatus? other)
    {
        if (other is null) return false;
        return Value == other.Value && Detail == other.Detail;
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as PluginStatus);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Value, Detail);
    }

    public static bool operator ==(PluginStatus? left, PluginStatus? right)
    {
        if (left is null && right is null) return true;
        if (left is null || right is null) return false;
        return left.Equals(right);
    }

    public static bool operator !=(PluginStatus? left, PluginStatus? right)
    {
        return !(left == right);
    }
}