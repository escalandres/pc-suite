using PCSuite.Contracts;
using PCSuite.Core.Elevation;
using PCSuite.Core.Operations;
using PCSuite.Core.Processes;
using PCSuite.Core.WinGet;
using PCSuite.Infrastructure.Developers;
using PCSuite.Infrastructure.Operations;
using PCSuite.Infrastructure.Processes;

namespace PCSuite.Infrastructure.WinGet;

/// <summary>
/// Mutación winget que corre DENTRO del proceso elevado (Task 006 sobre el
/// marco del Task 005). Seguridad en profundidad:
/// - Su ID ya validó el padre contra ElevatedOperationIds.Allowed antes de
///   pedir UAC, y ElevatedIpcServer lo vuelve a comprobar al servir;
/// - el packageId se REVALIDA aquí con el mismo patrón estricto y los argv se
///   reconstruyen con arreglos fijos: aunque el payload llegase manipulado no
///   existe ruta a un comando arbitrario (nunca un shell);
/// - devuelve resultado estructurado (WinGetMutationResult) con mensaje
///   controlado: jamás salidas crudas del sistema ni stack traces.
/// </summary>
public sealed class WinGetElevatedOperation : IElevatedOperation
{
    private static readonly TimeSpan MutationTimeout = TimeSpan.FromSeconds(900);

    private readonly WinGetMutationKind _kind;
    private readonly ExecutableLocator _locator;
    private readonly IProcessRunner _processRunner;

    public WinGetElevatedOperation(
        WinGetMutationKind kind,
        ExecutableLocator? locator = null,
        IProcessRunner? processRunner = null)
    {
        _kind = kind;
        _locator = locator ?? new ExecutableLocator();
        _processRunner = processRunner ?? new ProcessRunner();
    }

    public string Id => _kind switch
    {
        WinGetMutationKind.Install => ElevatedOperationIds.WingetInstall,
        WinGetMutationKind.Upgrade => ElevatedOperationIds.WingetUpgrade,
        WinGetMutationKind.UpgradeAll => ElevatedOperationIds.WingetUpgradeAll,
        WinGetMutationKind.Uninstall => ElevatedOperationIds.WingetUninstall,
        _ => throw new ArgumentOutOfRangeException(nameof(_kind)),
    };

    public async Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return OperationResult.Fail(BridgeErrorCodes.NotSupported, "Solo disponible en Windows.");
        }

        try
        {
            string[] args;
            string? packageId = null;

            if (_kind == WinGetMutationKind.UpgradeAll)
            {
                args = WinGetArguments.UpgradeAll();
            }
            else
            {
                if (!ToolMapper.TryParsePayload(context.Payload, out WinGetPackagePayload payload))
                {
                    return OperationResult.Fail(BridgeErrorCodes.MalformedMessage, "El payload debe ser un objeto JSON.");
                }

                // Segunda barrera de validación (la primera fue en WinGetService):
                // inválido → WinGetException(MALFORMED_MESSAGE) sin tocar procesos.
                packageId = WinGetArguments.ValidatePackageId(payload.PackageId);
                args = _kind switch
                {
                    WinGetMutationKind.Install => WinGetArguments.Install(packageId),
                    WinGetMutationKind.Upgrade => WinGetArguments.Upgrade(packageId),
                    WinGetMutationKind.Uninstall => WinGetArguments.Uninstall(packageId),
                    _ => throw new InvalidOperationException(),
                };
            }

            var path = _locator.Find("winget");
            if (path is null)
            {
                return OperationResult.Fail(BridgeErrorCodes.NotInstalled, "No se encontró winget en la PATH.");
            }

            ProcessResult result;
            try
            {
                result = await _processRunner.ExecuteAsync(
                    new ProcessRequest
                    {
                        FileName = path,
                        Arguments = args,
                        Timeout = MutationTimeout,
                        OutputEncoding = WinGetEncoding.Utf8,
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (ProcessTimeoutException)
            {
                return OperationResult.Fail(
                    BridgeErrorCodes.WingetTimeout,
                    "La operación winget superó el tiempo límite y fue terminada.");
            }

            var dto = new WinGetMutationResult
            {
                PackageId = packageId,
                Succeeded = result.ExitCode == 0,
                ExitCode = result.ExitCode,
                Elevated = true,
                Detail = WinGetOutput.Detail(result),
            };
            return OperationResult.Ok(BridgeJson.ToJson(dto));
        }
        catch (WinGetException exception)
        {
            return OperationResult.Fail(exception.ErrorCode, exception.ErrorMessage);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // El hijo elevado no tiene logger ni expone detalles internos.
            return OperationResult.Fail(BridgeErrorCodes.OperationFailed, "La operación elevada falló.");
        }
    }
}
