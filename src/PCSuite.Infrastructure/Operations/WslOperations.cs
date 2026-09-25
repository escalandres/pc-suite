using System.Text.Json;
using PCSuite.Contracts;
using PCSuite.Core.Operations;
using PCSuite.Core.Wsl;

namespace PCSuite.Infrastructure.Operations;

// ponytail: las 5 operaciones wsl.* en un solo archivo: payload cero, misma
// base y cuatro líneas cada una (Task 007 / §12 / §53).

/// <summary>
/// Base de las 5 operaciones wsl.* del puente: payload cero (el frontend jamás
/// envía argumentos de comando; los argv fijos viven en WslArguments y sólo se
/// usan dentro del servicio), traducción de <see cref="WslException"/> a
/// errores controlados y mapeo dominio → DTO camelCase.
/// </summary>
public abstract class WslOperation : IOperation
{
    protected WslOperation(IWslService service) =>
        Service = service ?? throw new ArgumentNullException(nameof(service));

    protected IWslService Service { get; }

    public abstract string Id { get; }

    /// <summary>
    /// Solo `wsl --update` puede terminar en un proceso elevado. La elevación
    /// se decide en runtime dentro de WslService (solo si wsl falla por
    /// permisos y el proceso no es ya administrador): nunca UAC preventivo.
    /// </summary>
    public virtual bool RequiresElevation => false;

    public async Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken cancellationToken)
    {
        try
        {
            // El contexto se descarta a propósito: wsl.* no tiene payload.
            var data = await ExecuteCoreAsync(cancellationToken).ConfigureAwait(false);
            return OperationResult.Ok(data);
        }
        catch (WslException exception)
        {
            return OperationResult.Fail(exception.ErrorCode, exception.ErrorMessage);
        }
    }

    protected abstract Task<JsonElement?> ExecuteCoreAsync(CancellationToken cancellationToken);

    protected static JsonElement? ToStatus(WslStatus status) =>
        BridgeJson.ToJson(new WslStatusResult
        {
            Installed = status.Installed,
            Path = status.Path,
            State = status.State,
            Version = status.Version,
            DefaultDistro = status.DefaultDistro,
        });

    protected static JsonElement? ToDistributions(IReadOnlyList<WslDistro> distros) =>
        BridgeJson.ToJson(new WslListResult
        {
            Distributions = distros.Select(distro => new WslDistroResult
            {
                Name = distro.Name,
                State = distro.State,
                Version = distro.Version,
            }).ToList(),
        });

    protected static JsonElement? ToMutation(WslMutationOutcome outcome) =>
        BridgeJson.ToJson(new WslMutationResult
        {
            Succeeded = outcome.Succeeded,
            ExitCode = outcome.ExitCode,
            Elevated = outcome.Elevated,
            Detail = outcome.Detail,
        });
}

public sealed class WslStatusOperation : WslOperation
{
    public WslStatusOperation(IWslService service)
        : base(service)
    {
    }

    public override string Id => OperationIds.WslStatus;

    protected override async Task<JsonElement?> ExecuteCoreAsync(CancellationToken cancellationToken) =>
        ToStatus(await Service.GetStatusAsync(cancellationToken).ConfigureAwait(false));
}

public sealed class WslListOperation : WslOperation
{
    public WslListOperation(IWslService service)
        : base(service)
    {
    }

    public override string Id => OperationIds.WslList;

    protected override async Task<JsonElement?> ExecuteCoreAsync(CancellationToken cancellationToken) =>
        ToDistributions(await Service.ListAsync(cancellationToken).ConfigureAwait(false));
}

public sealed class WslUpdateOperation : WslOperation
{
    public WslUpdateOperation(IWslService service)
        : base(service)
    {
    }

    public override string Id => OperationIds.WslUpdate;

    public override bool RequiresElevation => true;

    protected override async Task<JsonElement?> ExecuteCoreAsync(CancellationToken cancellationToken) =>
        ToMutation(await Service.UpdateAsync(cancellationToken).ConfigureAwait(false));
}

public sealed class WslShutdownOperation : WslOperation
{
    public WslShutdownOperation(IWslService service)
        : base(service)
    {
    }

    public override string Id => OperationIds.WslShutdown;

    protected override async Task<JsonElement?> ExecuteCoreAsync(CancellationToken cancellationToken) =>
        ToMutation(await Service.ShutdownAsync(cancellationToken).ConfigureAwait(false));
}

public sealed class WslStartOperation : WslOperation
{
    public WslStartOperation(IWslService service)
        : base(service)
    {
    }

    public override string Id => OperationIds.WslStart;

    protected override async Task<JsonElement?> ExecuteCoreAsync(CancellationToken cancellationToken) =>
        ToMutation(await Service.StartAsync(cancellationToken).ConfigureAwait(false));
}
