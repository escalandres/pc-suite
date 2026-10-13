using System.Text.Json;
using PCSuite.Contracts;
using PCSuite.Core.Elevation;
using PCSuite.Core.LocalShare;
using PCSuite.Core.Operations;
using PCSuite.Core.Processes;
using PCSuite.Infrastructure.Developers;
using PCSuite.Infrastructure.Operations;
using PCSuite.Infrastructure.Processes;

namespace PCSuite.Infrastructure.LocalShare;

/// <summary>
/// Utilidades de firewall de Local Share: nombre de regla compartido,
/// diagnóstico de solo lectura (sin elevación) y la operación que corre
/// DENTRO del proceso elevado para crear la regla.
///
/// La regla es lo más estrecha posible: entrante, TCP, solo el puerto del
/// servicio y solo para PCSuite.exe. Delete+add con nombre fijo la hace
/// idempotente (cambiar de puerto no deja reglas huérfanas).
/// </summary>
public static class LocalShareFirewall
{
    /// <summary>Nombre fijo de la regla de firewall de Local Share.</summary>
    public const string RuleName = "PCSuite Local Share";

    /// <summary>Payload de elevated.localshare.firewallRule: { port }.</summary>
    public sealed record FirewallRulePayload
    {
        public int Port { get; set; }
    }

    /// <summary>Resultado controlado de la mutación de firewall (sin texto crudo del sistema).</summary>
    public sealed record FirewallRuleResult
    {
        public bool Succeeded { get; init; }
        public string Detail { get; init; } = "";
    }

    /// <summary>Diagnóstico: ver PCSuite.Core.LocalShare.LocalShareFirewallDiagnostic.</summary>

    // ------------------------------------------------------------------
    // Diagnóstico (sin elevación): netsh show rule
    // ------------------------------------------------------------------

    /// <summary>
    /// Comprueba si existe la regla de Local Share. netsh devuelve 1 cuando
    /// ninguna regla coincide (independiente del idioma), por lo que el
    /// código de salida es la señal fiable; el texto solo se usa para
    /// comprobar si el puerto actual figura en la regla.
    /// </summary>
    public static async Task<LocalShareFirewallDiagnostic> DiagnoseAsync(
        IProcessRunner processRunner,
        int currentPort,
        CancellationToken ct = default)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                return LocalShareFirewallDiagnostic.Unknown;
            }

            var result = await processRunner.ExecuteAsync(
                new ProcessRequest
                {
                    FileName = "netsh",
                    Arguments = ["advfirewall", "firewall", "show", "rule", $"name={RuleName}"],
                    Timeout = TimeSpan.FromSeconds(15),
                },
                ct).ConfigureAwait(false);

            if (result.ExitCode != 0)
            {
                // "No rules match the specified criteria" (o equivalente localizado).
                return new LocalShareFirewallDiagnostic(
                    RuleExists: false, PortMatches: false, DiscoveryPortMatches: false);
            }

            // Matching por TOKENS, no por substring: los números son
            // independientes del idioma, pero "15354" no debe contar como
            // "5354". Cada puerto cubierto aparece como token en la línea
            // "LocalPort:" (o su traducción) de su regla.
            var transferOk = OutputMentionsPort(result.StandardOutput, currentPort);
            var discoveryOk = OutputMentionsPort(
                result.StandardOutput, DiscoveryService.MulticastPort);
            return new LocalShareFirewallDiagnostic(
                RuleExists: true, PortMatches: transferOk, DiscoveryPortMatches: discoveryOk);
        }
        catch
        {
            // Diagnóstico best-effort: nunca rompe la UI.
            return LocalShareFirewallDiagnostic.Unknown;
        }
    }

    /// <summary>¿El output de netsh menciona el puerto como token exacto?</summary>
    private static bool OutputMentionsPort(string output, int port)
    {
        var token = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (var line in output.Split('\r', '\n'))
        {
            foreach (var part in line.Split(
                (char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (part == token)
                {
                    return true;
                }
            }
        }

        return false;
    }
}

/// <summary>
/// Operación <see cref="ElevatedOperationIds.LocalShareFirewallRule"/> que
/// corre DENTRO del proceso elevado. Seguridad en profundidad:
/// - El ID validó el padre contra ElevatedOperationIds.Allowed antes del UAC
///   y ElevatedIpcServer lo revalida al servir;
/// - el payload SOLO aporta el puerto, revalidado como entero 1-65535;
/// - nombre de regla, protocolo, dirección y perfil son constantes;
/// - la ruta del programa se deriva del propio helper (junto a PCSuite.exe),
///   jamás del payload: no existe ruta a inyección de comandos arbitrarios.
/// </summary>
public sealed class LocalShareFirewallElevatedOperation : IElevatedOperation
{
    private static readonly TimeSpan MutationTimeout = TimeSpan.FromSeconds(30);

    private readonly ExecutableLocator _locator;
    private readonly IProcessRunner _processRunner;
    private readonly string? _programPath;

    public LocalShareFirewallElevatedOperation(
        ExecutableLocator? locator = null,
        IProcessRunner? processRunner = null,
        string? programPath = null)
    {
        _locator = locator ?? new ExecutableLocator();
        _processRunner = processRunner ?? new ProcessRunner();
        _programPath = programPath;
    }

    public string Id => ElevatedOperationIds.LocalShareFirewallRule;

    public async Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
        {
            return OperationResult.Fail(BridgeErrorCodes.NotSupported, "Solo disponible en Windows.");
        }

        if (!ToolMapper.TryParsePayload(context.Payload, out LocalShareFirewall.FirewallRulePayload payload)
            || payload is null)
        {
            return OperationResult.Fail(
                BridgeErrorCodes.MalformedMessage,
                "El payload debe ser { port }.");
        }

        // Revalidación DENTRO del proceso elevado: el puerto es lo único
        // que viaja en el payload y se usa como texto en argv.
        if (payload.Port is < 1 or > 65535)
        {
            return OperationResult.Fail(
                BridgeErrorCodes.MalformedMessage,
                "Puerto inválido.");
        }

        var netsh = _locator.Find("netsh");
        if (netsh is null)
        {
            return OperationResult.Fail(
                BridgeErrorCodes.NotInstalled,
                "No se encontró netsh.exe en la PATH.");
        }

        // La ruta del programa se deriva del helper (desplegado junto a la
        // app), nunca del payload. En pruebas se inyecta explícitamente.
        var program = _programPath
            ?? Path.Combine(AppContext.BaseDirectory, "PCSuite.exe");
        if (!File.Exists(program))
        {
            return OperationResult.Fail(
                BridgeErrorCodes.OperationFailed,
                "No se encontró el ejecutable de PCSuite junto al helper elevado.");
        }

        var port = payload.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);

        try
        {
            // 1) Idempotencia: borrar TODAS las reglas previas con este
            //    nombre (TCP y UDP) para que un cambio de puerto no deje
            //    reglas huérfanas.
            await _processRunner.ExecuteAsync(
                new ProcessRequest
                {
                    FileName = netsh,
                    Arguments =
                    [
                        "advfirewall", "firewall", "delete", "rule",
                        $"name={LocalShareFirewall.RuleName}",
                    ],
                    Timeout = MutationTimeout,
                },
                ct).ConfigureAwait(false);

            // 2) Regla de TRANSFERENCIA: entrante, TCP, solo el puerto,
            //    solo PCSuite.exe.
            var addTcp = await _processRunner.ExecuteAsync(
                new ProcessRequest
                {
                    FileName = netsh,
                    Arguments =
                    [
                        "advfirewall", "firewall", "add", "rule",
                        $"name={LocalShareFirewall.RuleName}",
                        "dir=in",
                        "action=allow",
                        $"program={program}",
                        "protocol=TCP",
                        $"localport={port}",
                        "enable=yes",
                    ],
                    Timeout = MutationTimeout,
                },
                ct).ConfigureAwait(false);

            // 3) Regla de DISCOVERY: entrante, UDP multicast 5354, mismo
            //    nombre. Sin esta, el discovery por multicast no ve NINGÚN
            //    equipo aunque la transferencia funcione.
            var addUdp = await _processRunner.ExecuteAsync(
                new ProcessRequest
                {
                    FileName = netsh,
                    Arguments =
                    [
                        "advfirewall", "firewall", "add", "rule",
                        $"name={LocalShareFirewall.RuleName}",
                        "dir=in",
                        "action=allow",
                        $"program={program}",
                        "protocol=UDP",
                        $"localport={DiscoveryService.MulticastPort}",
                        "enable=yes",
                    ],
                    Timeout = MutationTimeout,
                },
                ct).ConfigureAwait(false);

            var dto = new LocalShareFirewall.FirewallRuleResult
            {
                Succeeded = addTcp.ExitCode == 0 && addUdp.ExitCode == 0,
                Detail = addTcp.ExitCode == 0 && addUdp.ExitCode == 0
                    ? "Reglas creadas (transferencia + detección)."
                    : "El firewall rechazó una regla (¿directiva de grupo?).",
            };
            return OperationResult.Ok(BridgeJson.ToJson(dto));
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
