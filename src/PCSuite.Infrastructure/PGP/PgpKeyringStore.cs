using System.Text.Json;
using System.Text.RegularExpressions;
using PCSuite.Contracts;
using PCSuite.Core.PGP;

namespace PCSuite.Infrastructure.PGP;

/// <summary>
/// Keyring PGP persistido (Task 009 / §6): índice JSON con los metadatos de
/// cada clave más el material armoured en archivos <c>&lt;fingerprint&gt;.pub.asc</c>
/// y <c>&lt;fingerprint&gt;.sec.asc</c> dentro de %APPDATA%\PCSuite\pgp.
/// - Escritura atómica (tmp + rename, patrón de ClamAvHistoryStore) para no
///   dejar claves a medias si la app se cierra en seco;
/// - Índice ausente/corrupto/bloqueado → lista vacía y lecturas → null
///   (nunca rompe la operación que lo consulta);
/// - Lecturas acotadas a 1 MiB (el material real pesa ≤ ~64 KB).
/// IO síncrona deliberada (ponytail): archivos pequeños bajo su propio lock.
/// Queda tolerable un índice actualizado sin su material (o al revés): la
/// operación que lo consulte falla con un error controlado, nunca a medias.
/// </summary>
public sealed class PgpKeyringStore
{
    /// <summary>Tope de lectura de material armoured (claves reales ≤ ~64 KB).</summary>
    private const int MaxKeyFileBytes = 1_048_576;

    private const string IndexFileName = "keyring.json";

    private const string PublicSuffix = ".pub.asc";

    private const string SecretSuffix = ".sec.asc";

    private static readonly Regex FingerprintPattern =
        new("^[0-9A-Fa-f]{40}$", RegexOptions.CultureInvariant);

    private readonly string _directory;
    private readonly object _sync = new();

    /// <summary>directory null → %APPDATA%\PCSuite\pgp; los tests inyectan un temporal.</summary>
    public PgpKeyringStore(string? directory = null)
    {
        _directory = directory ?? PCSuitePaths.PgpKeyringDirectory;
    }

    /// <summary>Metadatos del keyring; ausente/corrupto/ilegible → lista vacía.</summary>
    public IReadOnlyList<PgpKeyInfo> Load()
    {
        lock (_sync)
        {
            return LoadIndex();
        }
    }

    /// <summary>Inserta o actualiza la entrada (upsert case-insensitive por fingerprint).</summary>
    public void Upsert(PgpKeyInfo key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var fingerprint = ValidateAndNormalize(key.Fingerprint);
        key = key with { Fingerprint = fingerprint };

        lock (_sync)
        {
            var entries = LoadIndex();
            entries.RemoveAll(entry =>
                string.Equals(entry.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase));
            entries.Add(key);
            SaveIndex(entries);
        }
    }

    /// <summary>Bloque público de la clave; ausente/descomunal/ilegible → null.</summary>
    public string? ReadPublicKey(string fingerprint)
    {
        fingerprint = ValidateAndNormalize(fingerprint);

        lock (_sync)
        {
            return ReadKeyFile(Path.Combine(_directory, fingerprint + PublicSuffix));
        }
    }

    /// <summary>Bloque privado (armoured, con passphrase si la tiene); ausente → null.</summary>
    public string? ReadSecretKey(string fingerprint)
    {
        fingerprint = ValidateAndNormalize(fingerprint);

        lock (_sync)
        {
            return ReadKeyFile(Path.Combine(_directory, fingerprint + SecretSuffix));
        }
    }

    public void WritePublicKey(string fingerprint, string armored)
    {
        fingerprint = ValidateAndNormalize(fingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(armored);

        lock (_sync)
        {
            WriteKeyFile(Path.Combine(_directory, fingerprint + PublicSuffix), armored);
        }
    }

    public void WriteSecretKey(string fingerprint, string armored)
    {
        fingerprint = ValidateAndNormalize(fingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(armored);

        lock (_sync)
        {
            WriteKeyFile(Path.Combine(_directory, fingerprint + SecretSuffix), armored);
        }
    }

    private List<PgpKeyInfo> LoadIndex()
    {
        try
        {
            var path = Path.Combine(_directory, IndexFileName);
            if (!File.Exists(path))
            {
                return [];
            }

            return JsonSerializer.Deserialize<List<PgpKeyInfo>>(
                File.ReadAllText(path),
                BridgeJson.Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private void SaveIndex(List<PgpKeyInfo> entries)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, IndexFileName);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(entries, BridgeJson.Options));
        File.Move(temporary, path, overwrite: true);
    }

    private string? ReadKeyFile(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length > MaxKeyFileBytes)
            {
                return null;
            }

            return File.ReadAllText(path);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void WriteKeyFile(string path, string armored)
    {
        Directory.CreateDirectory(_directory);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, armored);
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>
    /// El fingerprint se usa como nombre de archivo: debe ser exactamente 40
    /// hexadecimales (defensa contra rutas hostiles — nunca se concatena
    /// entrada del frontend sin validar) y se normaliza a mayúsculas para que
    /// índice y archivos coincidan siempre.
    /// </summary>
    private static string ValidateAndNormalize(string fingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        if (!FingerprintPattern.IsMatch(fingerprint))
        {
            throw new ArgumentException("El fingerprint debe ser hexadecimal (40 dígitos).", nameof(fingerprint));
        }

        return fingerprint.ToUpperInvariant();
    }
}
