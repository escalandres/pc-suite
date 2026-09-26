using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PCSuite.Contracts;
using PCSuite.Core.Processes;
using PCSuite.Core.SSH;
using PCSuite.Infrastructure.Developers;

namespace PCSuite.Infrastructure.SSH;

/// <summary>
/// Implementación de <see cref="ISshKeyService"/> (Task 009 / §5.1–5.2):
/// - La generación delega en el ssh-keygen maduro de Windows con argv fijo
///   construido aquí a partir de valores validados (allow-list de algoritmos,
///   bits por algoritmo, rutas absolutas, nombres de archivo controlados):
///   sin shell y jamás argumentos arbitrarios desde el frontend. Si ssh-keygen
///   no existe → error controlado NOT_INSTALLED.
/// - Listado, inspección, fingerprint e importación son parseo .NET puro
///   (SshKeyParser) sobre lecturas acotadas a 1 MiB; los archivos no
///   reconocidos se omiten y la importación nunca sobrescribe (escritura
///   atómica tmp + move sin reemplazo).
/// - Nada pide elevación: todo escribe en directorios de usuario (~/.ssh).
/// - El passphrase viaja solo como valor de argv de ssh-keygen (visible para
///   procesos del mismo usuario, que ya pueden leer ~/.ssh) y JAMÁS se registra
///   en logs ni en mensajes de error (se redacta por si acaso).
/// - Fallos controlados: SshException (código + mensaje apto para la UI) o
///   outcomes estructurados; nunca stack traces.
/// </summary>
public sealed class SshKeyService : ISshKeyService
{
    /// <summary>Tope de lectura: las claves reales pesan ≤ ~64 KB (RSA 8192 privado).</summary>
    private const int MaxKeyFileBytes = 1_048_576;

    private const int MaxCommentLength = 512;

    private const string InvalidKeyMessage = "El archivo no contiene una clave OpenSSH válida.";

    private static readonly TimeSpan GenerationTimeout = TimeSpan.FromSeconds(120);

    private static readonly Regex SafeFileName = new(
        "^[A-Za-z0-9._-]{1,100}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly ExecutableLocator _locator;
    private readonly IProcessRunner _processRunner;
    private readonly ILogger<SshKeyService>? _logger;
    private readonly string _homeDirectory;
    private readonly IReadOnlyList<string> _keygenDirectories;

    /// <param name="locator">Resolutor de la PATH (inyectable en tests).</param>
    /// <param name="processRunner">Única puerta de procesos (ssh-keygen, explorer).</param>
    /// <param name="logger">Opcional; nunca recibe argv ni passphrase.</param>
    /// <param name="homeDirectory">Base para ~/.ssh; en tests se inyecta una carpeta temporal.</param>
    /// <param name="keygenDirectories">Directorios conocidos de ssh-keygen además de la PATH.</param>
    public SshKeyService(
        ExecutableLocator locator,
        IProcessRunner processRunner,
        ILogger<SshKeyService>? logger = null,
        string? homeDirectory = null,
        IReadOnlyList<string>? keygenDirectories = null)
    {
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _logger = logger;
        _homeDirectory = homeDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _keygenDirectories = keygenDirectories ?? DefaultKeygenDirectories();
    }

    public Task<SshDetectStatus> DetectAsync(CancellationToken cancellationToken = default)
    {
        var path = FindSshKeygen();
        return Task.FromResult(new SshDetectStatus(path is not null, path));
    }

    public Task<IReadOnlyList<SshKeyInfo>> ListAsync(string? directory, CancellationToken cancellationToken = default)
    {
        var target = ResolveAndValidateDirectory(directory);
        if (!Directory.Exists(target))
        {
            return Task.FromResult<IReadOnlyList<SshKeyInfo>>([]);
        }

        var keys = new List<SshKeyInfo>();
        var indexByFingerprint = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(target)
                     .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var bytes = ReadBounded(file);
            if (bytes is null)
            {
                continue; // permisos o E/S: se omite el archivo y se sigue listando
            }

            var parsed = ParseKeyBytes(bytes);
            if (parsed is null)
            {
                continue;
            }

            // Ponytail: los contenedores PEM armados ("BEGIN OPENSSH PRIVATE
            // KEY", habituales al copiar claves desde Linux) no se listan; en
            // Windows .ssh conviven .pub + contenedor binario, que sí cubren.
            // .pub => hermano sin sufijo (par privado); contenedor binario => es
            // la clave privada; texto sin .pub => solo pública conocida.
            var isPublicKeyFile = file.EndsWith(".pub", StringComparison.OrdinalIgnoreCase);
            var hasPrivate = isPublicKeyFile
                ? File.Exists(file[..^".pub".Length])
                : IsPrivateKeyContainer(bytes);
            var info = ToInfo(parsed, file, hasPrivate);

            if (indexByFingerprint.TryGetValue(parsed.Fingerprint, out var existingIndex))
            {
                // El .pub y su contenedor privado son la MISMA clave: una sola
                // entrada; se prefiere la que trae comentario (la .pub) y se
                // une el indicador de clave privada.
                var existing = keys[existingIndex];
                var mergedHasPrivate = existing.HasPrivate || hasPrivate;
                var preferNew = existing.Comment.Length == 0 && parsed.Comment.Length > 0;
                keys[existingIndex] = preferNew
                    ? info with { HasPrivate = mergedHasPrivate }
                    : existing with { HasPrivate = mergedHasPrivate };
                continue;
            }

            indexByFingerprint[parsed.Fingerprint] = keys.Count;
            keys.Add(info);
        }

        return Task.FromResult<IReadOnlyList<SshKeyInfo>>(keys);
    }

    public Task<SshKeyInfo> InspectAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new SshException(BridgeErrorCodes.SshFailed, "Indica la ruta de un archivo de clave.");
        }

        var fullPath = ValidateAbsolutePath(path);
        if (!File.Exists(fullPath))
        {
            throw new SshException(BridgeErrorCodes.SshFailed, "El archivo no existe.");
        }

        var bytes = ReadBounded(fullPath);
        var parsed = bytes is null ? null : ParseKeyBytes(bytes);
        if (parsed is null)
        {
            throw new SshException(
                BridgeErrorCodes.SshFailed,
                bytes is null ? "No se pudo leer el archivo (permisos o errores de E/S)." : InvalidKeyMessage);
        }

        var isPublicKeyFile = fullPath.EndsWith(".pub", StringComparison.OrdinalIgnoreCase);
        var hasPrivate = isPublicKeyFile
            ? File.Exists(fullPath[..^".pub".Length])
            : IsPrivateKeyContainer(bytes!); // contenedor privado => true; texto público => false
        return Task.FromResult(ToInfo(parsed, fullPath, hasPrivate));
    }

    public async Task<SshGenerateOutcome> GenerateAsync(
        SshGenerateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var algorithm = (request.Algorithm ?? string.Empty).Trim().ToLowerInvariant();
        if (algorithm is not (SshAlgorithms.Ed25519 or SshAlgorithms.Rsa or SshAlgorithms.Ecdsa))
        {
            throw new SshException(BridgeErrorCodes.SshFailed, "El algoritmo debe ser ed25519, rsa o ecdsa.");
        }

        var bits = ValidateBits(algorithm, request.Bits);
        var comment = ValidateComment(request.Comment);
        var passphrase = request.Passphrase ?? string.Empty;
        if (passphrase.Contains('\0'))
        {
            throw new SshException(BridgeErrorCodes.SshFailed, "El passphrase no es válido.");
        }

        var directory = ResolveAndValidateDirectory(request.Directory);
        var privateKeyPath = Path.Combine(directory, $"id_{algorithm}");
        var publicKeyPath = privateKeyPath + ".pub";

        if (File.Exists(privateKeyPath) || File.Exists(publicKeyPath))
        {
            // Nunca se sobrescribe una clave existente (ni privada ni .pub).
            return new SshGenerateOutcome(
                false,
                null,
                null,
                null,
                $"Ya existe la clave {Path.GetFileName(privateKeyPath)} en {directory}. " +
                "No se sobrescriben claves existentes: bórrala o renómbrala primero.");
        }

        var keygen = FindSshKeygen();
        if (keygen is null)
        {
            throw new SshException(
                BridgeErrorCodes.NotInstalled,
                "ssh-keygen no está instalado. Activa la característica 'Cliente OpenSSH' de Windows " +
                "(Configuración → Aplicaciones → Aplicaciones opcionales).");
        }

        EnsureDirectory(directory);

        var arguments = BuildGenerateArguments(algorithm, bits, comment, privateKeyPath, passphrase);
        ProcessResult result;
        try
        {
            result = await _processRunner.ExecuteAsync(
                new ProcessRequest
                {
                    FileName = keygen,
                    Arguments = arguments,
                    Timeout = GenerationTimeout,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ProcessTimeoutException)
        {
            throw new SshException(
                BridgeErrorCodes.SshTimeout,
                "ssh-keygen superó el tiempo límite. Inténtalo de nuevo.");
        }
        catch (Exception exception)
        {
            // Sin argv en el log: pueden contener el passphrase.
            _logger?.LogWarning(exception, "No se pudo ejecutar ssh-keygen.");
            throw new SshException(BridgeErrorCodes.SshFailed, "No se pudo ejecutar ssh-keygen.");
        }

        if (result.ExitCode != 0)
        {
            return new SshGenerateOutcome(
                false,
                null,
                null,
                null,
                DescribeFailure(result, passphrase));
        }

        SshKeyInfo? info = null;
        var publicBytes = File.Exists(publicKeyPath) ? ReadBounded(publicKeyPath) : null;
        var parsedPublic = publicBytes is null ? null : ParseKeyBytes(publicBytes);
        if (parsedPublic is not null)
        {
            info = ToInfo(parsedPublic, publicKeyPath, hasPrivate: File.Exists(privateKeyPath));
        }

        return new SshGenerateOutcome(true, privateKeyPath, publicKeyPath, info, null);
    }

    public Task<SshImportOutcome> ImportPublicKeyAsync(
        SshImportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var parsed = SshKeyParser.TryParsePublicKeyLine(request.Key);
        if (parsed is null)
        {
            throw new SshException(
                BridgeErrorCodes.SshFailed,
                "La clave pública no es válida. Pega una línea tipo «ssh-ed25519 AAAA…» con su comentario opcional.");
        }

        var fileName = string.IsNullOrWhiteSpace(request.FileName)
            ? BuildDefaultImportName(parsed.Fingerprint)
            : request.FileName.Trim();
        if (fileName is "." or ".." || !SafeFileName.IsMatch(fileName))
        {
            throw new SshException(
                BridgeErrorCodes.SshFailed,
                "El nombre del archivo solo admite letras, números, punto, guion y guion bajo (máximo 100 caracteres).");
        }

        if (!fileName.EndsWith(".pub", StringComparison.OrdinalIgnoreCase))
        {
            fileName += ".pub";
        }

        var directory = ResolveAndValidateDirectory(request.Directory);
        EnsureDirectory(directory);

        var target = Path.Combine(directory, fileName);
        if (File.Exists(target))
        {
            throw new SshException(
                BridgeErrorCodes.SshFailed,
                $"Ya existe {fileName} en {directory}. No se sobrescriben archivos de clave.");
        }

        WriteAtomic(target, parsed.PublicKey + "\n");

        var hasPrivate = File.Exists(target[..^".pub".Length]);
        return Task.FromResult(new SshImportOutcome(target, ToInfo(parsed, target, hasPrivate)));
    }

    public async Task<string> OpenSshDirectoryAsync(CancellationToken cancellationToken = default)
    {
        var directory = DefaultSshDirectory;
        EnsureDirectory(directory);

        try
        {
            // Interactive: sin redirección (explorer hereda los handles de
            // tubería y bloquearía la lectura) ni timeout (no debe matarse al
            // shell de Windows). argv fijo: solo la ruta interna .ssh.
            await _processRunner.ExecuteAsync(
                new ProcessRequest
                {
                    FileName = "explorer.exe",
                    Arguments = [directory],
                    Interactive = true,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "No se pudo abrir la carpeta .ssh.");
            throw new SshException(BridgeErrorCodes.SshFailed, "No se pudo abrir la carpeta .ssh en el Explorador.");
        }

        return directory;
    }

    // ------------------------------------------------------------------
    // Validaciones (mensajes aptos para la UI, sin stack traces)
    // ------------------------------------------------------------------

    private static int? ValidateBits(string algorithm, int? requested)
    {
        if (algorithm == SshAlgorithms.Ed25519)
        {
            if (requested is not null)
            {
                throw new SshException(
                    BridgeErrorCodes.SshFailed,
                    "Ed25519 no admite tamaño de clave (usa rsa o ecdsa si necesitas elegir bits).");
            }

            return null;
        }

        if (algorithm == SshAlgorithms.Rsa)
        {
            var value = requested ?? 3072;
            if (value is not (2048 or 3072 or 4096))
            {
                throw new SshException(BridgeErrorCodes.SshFailed, "RSA admite 2048, 3072 o 4096 bits.");
            }

            return value;
        }

        var ecdsaBits = requested ?? 256;
        if (ecdsaBits is not (256 or 384 or 521))
        {
            throw new SshException(BridgeErrorCodes.SshFailed, "ECDSA admite 256, 384 o 521 bits.");
        }

        return ecdsaBits;
    }

    private static string ValidateComment(string? comment)
    {
        var value = comment ?? string.Empty;
        if (value.Length > MaxCommentLength)
        {
            throw new SshException(
                BridgeErrorCodes.SshFailed,
                $"El comentario es demasiado largo (máximo {MaxCommentLength} caracteres).");
        }

        if (value.Contains('\0') || value.Contains('\n') || value.Contains('\r'))
        {
            throw new SshException(BridgeErrorCodes.SshFailed, "El comentario no admite saltos de línea ni caracteres nulos.");
        }

        return value;
    }

    private string ResolveAndValidateDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return DefaultSshDirectory;
        }

        return ValidateAbsolutePath(directory);
    }

    private static string ValidateAbsolutePath(string path)
    {
        if (path.Contains('\0') || !Path.IsPathFullyQualified(path))
        {
            throw new SshException(
                BridgeErrorCodes.SshFailed,
                "La ruta debe ser absoluta (p. ej. C:\\Users\\tu\\.ssh).");
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new SshException(BridgeErrorCodes.SshFailed, "La ruta no es válida.");
        }
    }

    private void EnsureDirectory(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(exception, "No se pudo crear la carpeta {Directory}.", directory);
            throw new SshException(BridgeErrorCodes.SshFailed, $"No se pudo crear la carpeta {directory}.");
        }
    }

    // ------------------------------------------------------------------
    // ssh-keygen (argv fijo)
    // ------------------------------------------------------------------

    private string? FindSshKeygen()
    {
        // Solo .exe: los shims .cmd/.bat de la PATH no se pueden lanzar con
        // CreateProcess (UseShellExecute=false), así que se ignoran.
        var fromPath = _locator.Find("ssh-keygen");
        if (fromPath is not null && fromPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return fromPath;
        }

        foreach (var directory in _keygenDirectories)
        {
            var candidate = Path.Combine(directory, "ssh-keygen.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// argv fijo: cada valor validado viaja como elemento de lista (sin shell).
    /// ssh-keygen consume el token siguiente como valor de -C/-f/-N, así que un
    /// comentario que empiece por "-" no puede inyectar opciones.
    /// </summary>
    private static IReadOnlyList<string> BuildGenerateArguments(
        string algorithm,
        int? bits,
        string comment,
        string privateKeyPath,
        string passphrase)
    {
        var arguments = new List<string> { "-q", "-t", algorithm };
        if (bits is not null)
        {
            arguments.Add("-b");
            arguments.Add(bits.Value.ToString(CultureInfo.InvariantCulture));
        }

        arguments.Add("-C");
        arguments.Add(comment);
        arguments.Add("-f");
        arguments.Add(privateKeyPath);
        arguments.Add("-N");
        arguments.Add(passphrase);
        return arguments;
    }

    private static string DescribeFailure(ProcessResult result, string passphrase)
    {
        var detail = FirstMeaningfulLine(result.StandardError);
        if (detail.Length == 0)
        {
            detail = FirstMeaningfulLine(result.StandardOutput);
        }

        var summary = detail.Length == 0
            ? $"ssh-keygen terminó con código {result.ExitCode}."
            : $"ssh-keygen terminó con código {result.ExitCode}: {detail}";

        // Redacción defensiva: ssh-keygen no ecoa el passphrase, pero ningún
        // mensaje de la UI puede contenerlo por si un build distinto lo hace.
        return passphrase.Length > 0 && summary.Contains(passphrase, StringComparison.Ordinal)
            ? summary.Replace(passphrase, "****", StringComparison.Ordinal)
            : summary;
    }

    private static string FirstMeaningfulLine(string output)
    {
        foreach (var raw in output.Split('\n'))
        {
            var line = new string(raw.Trim().Where(character => !char.IsControl(character)).ToArray());
            if (line.Length == 0)
            {
                continue;
            }

            return line.Length > 200 ? line[..200] + "…" : line;
        }

        return string.Empty;
    }

    // ------------------------------------------------------------------
    // Utilidades de archivos (lecturas acotadas y escritura atómica)
    // ------------------------------------------------------------------

    private string DefaultSshDirectory => Path.Combine(_homeDirectory, ".ssh");

    private static IReadOnlyList<string> DefaultKeygenDirectories() =>
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "OpenSSH"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OpenSSH", "bin"),
    ];

    private static bool IsPrivateKeyContainer(byte[] bytes) =>
        bytes.AsSpan().StartsWith(SshKeyParser.OpenSshPrivateKeyMagic);

    private static SshParsedKey? ParseKeyBytes(byte[] bytes) =>
        IsPrivateKeyContainer(bytes)
            ? SshKeyParser.TryParseFromPrivateKeyFile(bytes)
            : SshKeyParser.TryParsePublicKeyLine(FirstLine(bytes));

    private static string FirstLine(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        var newline = text.IndexOf('\n');
        return newline >= 0 ? text[..newline] : text;
    }

    private static byte[]? ReadBounded(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var length = (int)Math.Min(stream.Length, MaxKeyFileBytes);
            var buffer = new byte[length];
            stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            return buffer;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void WriteAtomic(string target, string contents)
    {
        var tempPath = $"{target}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(tempPath, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            // File.Move sin sobrescribe: si otro proceso ganó la carrera, lanza
            // y la importación falla controladamente en vez de pisar la clave.
            File.Move(tempPath, target);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryDelete(tempPath);
            throw new SshException(BridgeErrorCodes.SshFailed, $"No se pudo escribir {target}.");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Basura temporal huérfana: se ignora.
        }
    }

    private static string BuildDefaultImportName(string fingerprint)
    {
        const string prefix = "SHA256:";
        var suffix = fingerprint.StartsWith(prefix, StringComparison.Ordinal) ? fingerprint[prefix.Length..] : fingerprint;
        var safe = new string(suffix
            .Take(12)
            .Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-')
            .ToArray());
        return "imported-" + safe;
    }

    private static SshKeyInfo ToInfo(SshParsedKey parsed, string path, bool hasPrivate) =>
        new(
            parsed.Type,
            parsed.Algorithm,
            parsed.Bits,
            parsed.Fingerprint,
            parsed.Comment,
            SshKeyParser.FormatOpenSsh,
            path,
            hasPrivate,
            parsed.PublicKey);
}
