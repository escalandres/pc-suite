using System.Text.Json;
using PCSuite.Contracts;
using PCSuite.Core.Elevation;
using PCSuite.Core.Operations;
using PCSuite.Core.Uninstall;
using PCSuite.Infrastructure.Uninstall;

namespace PCSuite.Infrastructure.Operations;

/// <summary>Resultado de uninstall.list (Task 020): apps del registro.</summary>
public sealed class InstalledAppDto
{
    public string RegistryKeyPath { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Publisher { get; init; } = "";
    public string DisplayVersion { get; init; } = "";
    public string InstallLocation { get; init; } = "";
    public string UninstallString { get; init; } = "";
    public string? MsiGuid { get; init; }
    public long EstimatedSizeKb { get; init; }
    public bool IsSystemComponent { get; init; }
}

public sealed class InstalledAppListResultDto
{
    public List<InstalledAppDto> Apps { get; init; } = [];
}

/// <summary>Resultado de uninstall.discover: el plan revisable.</summary>
public sealed class RemovalPlanDto
{
    public InstalledAppDto App { get; init; } = new();
    public bool HasOfficialUninstaller { get; init; }
    public List<RemovalPlanItemDto> Items { get; init; } = [];
}

public sealed class RemovalPlanItemDto
{
    /// <summary>process | service | scheduledtask | folder | registrykey.</summary>
    public string Kind { get; init; } = "";
    /// <summary>derived (pre-marcado: quirúrgico) | candidate (heurístico, sin marcar).</summary>
    public string Tier { get; init; } = "";
    public string Identifier { get; init; } = "";
    public string Detail { get; init; } = "";
}

/// <summary>Resultado de uninstall.runOfficial / uninstall.cleanup (vía elevado).</summary>
public sealed class UninstallOfficialResultDto
{
    public bool Launched { get; init; }
    public string Launcher { get; init; } = "";
}

public sealed class UninstallCleanupResultDto
{
    public List<RemovalItemResultDto> Results { get; init; } = [];
    public bool RequiresRestart { get; init; }
}

// =====================================================================
// Operaciones del puente
// =====================================================================

/// <summary>
/// uninstall.list — apps instaladas según el registro de desinstalación de
/// Windows. VE lo que winget no ve. Solo lectura, sin elevación.
/// </summary>
public sealed class UninstallListOperation : IOperation
{
    private readonly UninstallCatalog _catalog;

    public UninstallListOperation(UninstallCatalog? catalog = null)
    {
        _catalog = catalog ?? new UninstallCatalog();
    }

    public string Id => OperationIds.UninstallList;
    public bool RequiresElevation => false;

    public Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken ct)
    {
        var apps = _catalog.ListApps();
        return Task.FromResult(OperationResult.Ok(BridgeJson.ToJson(new InstalledAppListResultDto
        {
            Apps = apps.Select(ToDto).ToList(),
        })));
    }

    internal static InstalledAppDto ToDto(InstalledAppInfo app) => new()
    {
        RegistryKeyPath = app.RegistryKeyPath,
        DisplayName = app.DisplayName,
        Publisher = app.Publisher,
        DisplayVersion = app.DisplayVersion,
        InstallLocation = app.InstallLocation,
        UninstallString = app.UninstallString,
        MsiGuid = app.MsiGuid,
        EstimatedSizeKb = app.EstimatedSizeKb,
        IsSystemComponent = app.IsSystemComponent,
    };
}

/// <summary>
/// uninstall.discover — plan de eliminación de UNA app (por clave de
/// registro validada). Solo lectura, sin elevación: el usuario revisa el
/// plan ANTES de que exista UAC alguno.
/// </summary>
public sealed class UninstallDiscoverOperation(UninstallCatalog? catalog = null, UninstallPlanner? planner = null) : IOperation
{
    private readonly UninstallCatalog _catalog = catalog ?? new UninstallCatalog();
    private readonly UninstallPlanner _planner = planner ?? new UninstallPlanner();

    public string Id => OperationIds.UninstallDiscover;
    public bool RequiresElevation => false;

    public Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken ct)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out UninstallKeyPayload payload)
            || payload is null || string.IsNullOrWhiteSpace(payload.RegistryKeyPath))
        {
            return Task.FromResult(OperationResult.Fail(
                BridgeErrorCodes.MalformedMessage,
                "El payload debe ser { registryKeyPath }."));
        }

        var app = _catalog.GetApp(payload.RegistryKeyPath);
        if (app is null)
        {
            return Task.FromResult(OperationResult.Fail(
                BridgeErrorCodes.UninstallFailed,
                "La entrada de desinstalación no existe (o está fuera de las raíces permitidas)."));
        }

        var plan = _planner.BuildPlan(app);
        return Task.FromResult(OperationResult.Ok(BridgeJson.ToJson(new RemovalPlanDto
        {
            App = UninstallListOperation.ToDto(app),
            HasOfficialUninstaller = plan.HasOfficialUninstaller,
            Items = plan.Items.Select(item => new RemovalPlanItemDto
            {
                Kind = item.Kind.ToString().ToLowerInvariant(),
                Tier = item.Tier.ToString().ToLowerInvariant(),
                Identifier = item.Identifier,
                Detail = item.Detail,
            }).ToList(),
        })));
    }
}

/// <summary>
/// uninstall.runOfficial — lanza el desinstalador OFICIAL de la app en el
/// proceso elevado (UAC puntual). El payload solo porta la clave de
/// registro; el comando se RELEE allí (jamás viaja por el puente).
/// La app NO espera al asistente: el usuario lo completa y vuelve.
/// </summary>
public sealed class UninstallRunOfficialOperation(IElevationManager elevation) : IOperation
{
    public string Id => OperationIds.UninstallRunOfficial;
    public bool RequiresElevation => false; // la elevación es por UAC, no de puente

    public async Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken ct)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out UninstallKeyPayload payload)
            || payload is null || string.IsNullOrWhiteSpace(payload.RegistryKeyPath))
        {
            return OperationResult.Fail(
                BridgeErrorCodes.MalformedMessage,
                "El payload debe ser { registryKeyPath }.");
        }

        // Validar ANTES de pedir UAC: una clave inexistente no debe abrir prompt.
        var catalog = new UninstallCatalog();
        if (catalog.GetApp(payload.RegistryKeyPath) is null)
        {
            return OperationResult.Fail(
                BridgeErrorCodes.UninstallFailed,
                "La entrada de desinstalación no existe.");
        }

        var elevated = await elevation.RunElevatedAsync(
            new ElevatedOperation
            {
                OperationId = ElevatedOperationIds.UninstallRunOfficial,
                Payload = BridgeJson.ToJson(payload),
            },
            ct).ConfigureAwait(false);

        return elevated.Success
            ? OperationResult.Ok(elevated.Data)
            : OperationResult.Fail(
                elevated.ErrorCode ?? BridgeErrorCodes.ElevationFailed,
                elevated.ErrorMessage ?? "No se pudo ejecutar el desinstalador.");
    }
}

/// <summary>
/// uninstall.cleanup — aplica la limpieza sobre los ítems aprobados por el
/// usuario. El proceso elevado REVALIDA cada ítem (segunda barrera).
/// </summary>
public sealed class UninstallCleanupOperation(IElevationManager elevation) : IOperation
{
    public string Id => OperationIds.UninstallCleanup;
    public bool RequiresElevation => false;

    public async Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken ct)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out CleanupPayload payload)
            || payload?.Items is null || payload.Items.Count == 0)
        {
            return OperationResult.Fail(
                BridgeErrorCodes.MalformedMessage,
                "El payload debe ser { items: [{ kind, value }] }.");
        }

        // Primera barrera en el puente: cada ítem debe ser válido ANTES de
        // abrir el UAC (los inválidos se filtran; si no queda nada, error).
        var validItems = payload.Items
            .Where(IsValidItem)
            .ToList();
        if (validItems.Count == 0)
        {
            return OperationResult.Fail(
                BridgeErrorCodes.UninstallFailed,
                "Ningún ítem del plan es válido (¿se borraron las carpetas base?).");
        }

        var elevated = await elevation.RunElevatedAsync(
            new ElevatedOperation
            {
                OperationId = ElevatedOperationIds.UninstallCleanup,
                Payload = BridgeJson.ToJson(new CleanupPayload { Items = validItems }),
            },
            ct).ConfigureAwait(false);

        return elevated.Success
            ? OperationResult.Ok(elevated.Data)
            : OperationResult.Fail(
                elevated.ErrorCode ?? BridgeErrorCodes.ElevationFailed,
                elevated.ErrorMessage ?? "No se pudo ejecutar la limpieza.");
    }

    private static bool IsValidItem(CleanupItemPayload item) =>
        item.Kind switch
        {
            "folder" => UninstallValidator.IsSafeFolder(item.Value),
            "registrykey" => UninstallValidator.IsSafeRegistryKey(item.Value),
            "service" => UninstallValidator.IsSafeIdentifier(item.Value),
            "scheduledtask" => UninstallValidator.IsSafeTaskPath(item.Value),
            "process" => item.Value.Contains(':'),
            _ => false,
        };
}
