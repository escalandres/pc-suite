// PCSuite.Elevated — proceso elevado de vida corta (Task 005).
//
// Reglas de seguridad de este ejecutable:
// - Nunca expone un shell ni ejecución arbitraria: solo las operaciones
//   registradas abajo (lista blanca compartida con el padre).
// - Recibe únicamente [nombre del pipe] [nonce]; cualquier otra combinación
//   sale con código 2 sin tocar nada.
// - Atiende UNA conexión IPC (identidad de cliente + nonce) y termina. El
//   nonce jamás se escribe en log ni en mensajes de error.

using PCSuite.Infrastructure.ClamAV;
using PCSuite.Infrastructure.Elevation;
using PCSuite.Infrastructure.LocalShare;
using PCSuite.Infrastructure.WinGet;
using PCSuite.Infrastructure.Wsl;

namespace PCSuite.Elevated;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length != 2)
        {
            return 2;
        }

        if (!IsSafeToken(args[0], maxLength: 128) || !IsSafeToken(args[1], maxLength: 128))
        {
            return 2;
        }

        var pipeName = args[0];
        var nonce = args[1];

        var registry = new ElevatedOperationRegistry();
        registry.Register(new AdminInfoElevatedOperation());

        // Mutaciones winget (Task 006): cada kind su id allow-listed. El
        // packageId del payload se REVALIDA en WinGetElevatedOperation y los
        // argv se reconstruyen fijos: jamás ejecución arbitraria.
        registry.Register(new WinGetElevatedOperation(WinGetMutationKind.Install));
        registry.Register(new WinGetElevatedOperation(WinGetMutationKind.Upgrade));
        registry.Register(new WinGetElevatedOperation(WinGetMutationKind.UpgradeAll));
        registry.Register(new WinGetElevatedOperation(WinGetMutationKind.Uninstall));

        // wsl --update (Task 007): payload ignorado y argv fijo --update.
        registry.Register(new WslElevatedOperation());

        // freshclam (Task 008): payload ignorado, argv vacío fijo y carpeta de
        // trabajo = instalación de ClamAV. Nunca un shell.
        registry.Register(new ClamAvElevatedOperation());

        // Regla de firewall de Local Share (Task 016.6): payload solo porta
        // el puerto (revalidado), regla y programa derivados del helper.
        registry.Register(new LocalShareFirewallElevatedOperation());

        try
        {
            var server = new ElevatedIpcServer(registry);
            var response = await server.ServeAsync(
                pipeName,
                nonce,
                Environment.UserName,
                CancellationToken.None);

            // 0: operación exitosa; 1: control o ejecución fallida (siempre
            // con respuesta estructurada enviada al padre antes de salir).
            return response.Success ? 0 : 1;
        }
        catch
        {
            // Último recurso: sin detalles por consola (WinExe no tiene una y
            // el padre ya recibió — o dará por perdido— su respuesta).
            return 1;
        }
    }

    /// <summary>
    /// Entrada arbitraria de argv: se admite solo un token ASCII corto seguro
    /// (letras, dígitos y separadores de nombre). No valida la semántica — la
    /// autenticación real es identidad + nonce en el propio IPC.
    /// </summary>
    private static bool IsSafeToken(string value, int maxLength) =>
        !string.IsNullOrEmpty(value)
        && value.Length <= maxLength
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '+' or '/' or '=');
}
