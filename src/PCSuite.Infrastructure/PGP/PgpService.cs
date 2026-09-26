using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Bcpg.OpenPgp;
using Org.BouncyCastle.Bcpg.Sig;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using PCSuite.Contracts;
using PCSuite.Core.PGP;
// El alias evita la ambigüedad con Org.BouncyCastle.Bcpg.OpenPgp.PgpException.
using PgpException = PCSuite.Core.PGP.PgpException;

namespace PCSuite.Infrastructure.PGP;

/// <summary>
/// Operaciones pgp.* (Task 009 / §6): generación e inspección de claves 100%
/// BouncyCastle en proceso — nunca gpg (ausente en Windows) ni procesos
/// externos. El material vive en <see cref="PgpKeyringStore"/> (índice JSON +
/// bloques armoured) y las operaciones pesadas corren fuera del hilo del
/// puente. Sin ILogger a propósito: todo lo que pudiese loguearse (contraseña,
/// texto plano, clave privada) está prohibido, así que los errores son
/// mensajes estáticos en español que jamás reflejan la entrada del usuario.
/// </summary>
public sealed class PgpService : IPgpService
{
    /// <summary>Tope de importación armoured (claves reales ≤ ~64 KB).</summary>
    private const int MaxArmoredChars = 1_048_576;

    /// <summary>Texto descifrado: 1 MiB de texto son hasta 4 MiB en UTF-8.</summary>
    private const long MaxPlainBytes = 4_194_304;

    private const int BufferSize = 65_536;

    private const int MaxNameLength = 256;

    private const int MaxEmailLength = 256;

    private const int MaxPassphraseLength = 1024;

    private const int MaxExpirationDays = 3650;

    private const int DefaultRsaBits = 3072;

    private const long SecondsPerDay = 86_400;

    // Mensajes estáticos: nunca interpolan entradas del usuario.
    private const string InvalidNameMessage =
        "El nombre no es válido (1-256 caracteres, sin '<', '>' ni controles).";
    private const string InvalidEmailMessage = "El email no es válido (ejemplo: ana@dominio.com).";
    private const string InvalidAlgorithmMessage = "El algoritmo debe ser 'ed25519' o 'rsa'.";
    private const string InvalidBitsMessage =
        "Los bits de RSA deben ser 2048, 3072 o 4096; ed25519 no admite bits.";
    private const string InvalidExpirationMessage =
        "La caducidad debe estar entre 1 y 3650 días (o vacía para no caducar).";
    private const string InvalidPassphraseMessage = "La contraseña no puede superar los 1024 caracteres.";
    private const string EmptyKeyMessage = "Pega una clave PGP en formato armoured (-----BEGIN PGP ...).";
    private const string KeyTooLargeMessage = "La clave supera el tamaño máximo de 1 MiB.";
    private const string MalformedKeyMessage = "El texto no es un anillo de claves PGP válido.";
    private const string FingerprintRequiredMessage = "Indica el fingerprint de la clave.";
    private const string FingerprintNotFoundMessage = "No hay ninguna clave con ese fingerprint en el keyring.";
    private const string AmbiguousFingerprintMessage = "El fingerprint es ambiguo: añade más dígitos.";
    private const string NoPublicMaterialMessage = "Esa clave no tiene material público en el keyring.";
    private const string NoPrivateMaterialMessage = "Esa clave no tiene copia privada en el keyring (solo pública).";

    // Criptografía (9.6): mensajes estáticos que jamás reflejan texto plano,
    // contraseñas, destinatarios ni fragmentos del mensaje cifrado.
    private const string TextTooLargeMessage = "El texto supera el tamaño máximo de 1 MiB.";
    private const string RecipientsRequiredMessage = "Indica al menos un destinatario (fingerprint o email).";
    private const string RecipientNotFoundMessage = "Ningún destinatario coincide con una clave del keyring.";
    private const string RecipientCannotEncryptMessage =
        "Esa clave no tiene subclave de cifrado: no puede recibir mensajes.";
    private const string MessageRequiredMessage =
        "Pega un mensaje PGP en formato armoured (-----BEGIN PGP MESSAGE-----).";
    private const string MessageTooLargeMessage = "El mensaje supera el tamaño máximo de 1 MiB.";
    private const string MalformedPgpMessage = "El texto no es un mensaje PGP compatible.";
    private const string NoPrivateKeysMessage = "El keyring no tiene claves privadas para descifrar.";
    private const string NotEncryptedForKeyMessage =
        "El mensaje no está cifrado para ninguna clave privada de este keyring.";
    private const string WrongPassphraseMessage = "La contraseña es incorrecta o la clave está dañada.";
    private const string CorruptedPgpMessage = "El mensaje está corrupto (la verificación de integridad ha fallado).";
    private const string CannotSignMessage = "No se pudo crear la firma con esa clave.";
    private const string EmptySignatureMessage =
        "Pega una firma PGP en formato armoured (-----BEGIN PGP SIGNATURE-----).";
    private const string InvalidSignatureMessage = "El texto no es una firma PGP válida.";
    private const string SignerNotInKeyringMessage = "La clave firmante no está en el keyring.";
    private const string SignatureMismatchMessage = "La firma no corresponde al texto indicado.";

    private static readonly Regex EmailPattern =
        new(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.CultureInvariant);

    private static readonly Regex UserIdPattern =
        new(@"^(.*?)\s*<([^<>]*)>\s*$", RegexOptions.CultureInvariant);

    private readonly PgpKeyringStore _store;

    public PgpService(PgpKeyringStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    // ------------------------------------------------------------------
    // Claves: generar / listar / importar / exportar (9.5)
    // ------------------------------------------------------------------

    /// <summary>Genera primaria + subclave de cifrado y las persiste en el keyring.</summary>
    public Task<PgpKeyInfo> GenerateKeyAsync(
        PgpGenerateKeyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Crypto + IO fuera del hilo del puente (ponytail: Task.Run directo).
        return Task.Run(() => GenerateKey(request, cancellationToken), cancellationToken);
    }

    /// <summary>Claves indexadas, más reciente primero; keyring vacío → lista vacía.</summary>
    public Task<IReadOnlyList<PgpKeyInfo>> ListKeysAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<PgpKeyInfo>>(
            () => _store.Load().OrderByDescending(static key => key.CreatedAt).ToList(),
            cancellationToken);

    /// <summary>Importa un anillo armoured (privado o público) sin tocar claves distintas.</summary>
    public Task<PgpKeyInfo> ImportKeyAsync(
        PgpImportKeyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.Run(() => ImportKey(request, cancellationToken), cancellationToken);
    }

    /// <summary>Exporta el bloque armoured: prefijo de fingerprint, público o privado.</summary>
    public Task<string> ExportKeyAsync(
        PgpExportKeyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.Run(() => ExportKey(request, cancellationToken), cancellationToken);
    }

    // 9.6: cifrado/descifrado/firma/verificación con BouncyCastle en proceso.
    public Task<string> EncryptAsync(PgpEncryptRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.Run(() => Encrypt(request, cancellationToken), cancellationToken);
    }

    public Task<string> DecryptAsync(PgpDecryptRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.Run(() => Decrypt(request, cancellationToken), cancellationToken);
    }

    public Task<string> SignAsync(PgpSignRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.Run(() => Sign(request, cancellationToken), cancellationToken);
    }

    public Task<PgpVerifyOutcome> VerifyAsync(
        PgpVerifyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.Run(() => Verify(request, cancellationToken), cancellationToken);
    }

    // ------------------------------------------------------------------
    // Generación
    // ------------------------------------------------------------------

    private PgpKeyInfo GenerateKey(PgpGenerateKeyRequest request, CancellationToken cancellationToken)
    {
        var name = (request.Name ?? string.Empty).Trim();
        var email = (request.Email ?? string.Empty).Trim();
        ValidateIdentity(name, email);

        var algorithm = NormalizeAlgorithm(request.Algorithm);
        if (algorithm != PgpAlgorithms.Ed25519 && algorithm != PgpAlgorithms.Rsa)
        {
            throw new PgpException(BridgeErrorCodes.PgpFailed, InvalidAlgorithmMessage);
        }

        var bits = ValidateBits(algorithm, request.Bits);
        var expirationDays = ValidateExpiration(request.ExpirationDays);
        var passphrase = ValidatePassphrase(request.Passphrase);

        cancellationToken.ThrowIfCancellationRequested();

        var random = new SecureRandom();
        var now = DateTime.UtcNow;
        var userId = name + " <" + email + ">";
        var expirationSeconds = expirationDays is null ? 0L : expirationDays.Value * SecondsPerDay;

        PgpKeyPair masterPair;
        PgpKeyPair subPair;
        if (algorithm == PgpAlgorithms.Rsa)
        {
            var rsaBits = bits ?? DefaultRsaBits;
            masterPair = new PgpKeyPair(PublicKeyAlgorithmTag.RsaGeneral, GenerateRsaKeyPair(rsaBits, random), now);
            subPair = new PgpKeyPair(PublicKeyAlgorithmTag.RsaGeneral, GenerateRsaKeyPair(rsaBits, random), now);
        }
        else
        {
            masterPair = new PgpKeyPair(PublicKeyAlgorithmTag.EdDsa_Legacy, GenerateEd25519KeyPair(random), now);
            // La subclave de cifrado es ECDH sobre Curve25519 (tag 18); el tag
            // X25519 (25) no está soportado por el generador de anillos de BC.
            subPair = new PgpKeyPair(PublicKeyAlgorithmTag.ECDH, GenerateX25519KeyPair(random), now);
        }

        var generator = new PgpKeyRingGenerator(
            PgpSignature.DefaultCertification, // autofirma genérica (tipo 16)
            masterPair,
            userId,
            SymmetricKeyAlgorithmTag.Aes256,
            HashAlgorithmTag.Sha256,
            // BC 2.7.0 no admite passphrase nula (NRE): sin contraseña se usa la
            // cadena vacía, misma confidencialidad que una clave sin proteger.
            // useSha1 solo fija S2K usage 254 (formato estándar de gpg); la
            // autofirma sigue con SHA-256 (asertado en tests). (ponytail)
            (passphrase ?? string.Empty).ToCharArray(),
            useSha1: true,
            BuildKeyPackets(
                (int)(PgpKeyFlags.CanSign | PgpKeyFlags.CanCertify),
                expirationSeconds),
            unhashedPackets: null,
            random);

        generator.AddSubKey(
            subPair,
            BuildKeyPackets(
                (int)(PgpKeyFlags.CanEncryptCommunications | PgpKeyFlags.CanEncryptStorage),
                expirationSeconds),
            unhashedPackets: null,
            HashAlgorithmTag.Sha256);

        cancellationToken.ThrowIfCancellationRequested();

        var publicRing = generator.GeneratePublicKeyRing();
        var secretRing = generator.GenerateSecretKeyRing();
        var info = ToInfo(publicRing, hasPrivate: true);

        // Material primero, índice al final: una interrupción deja un archivo
        // huérfano (inofensivo), nunca un índice que apunte a nada.
        _store.WritePublicKey(info.Fingerprint, Armor(publicRing.GetEncoded()));
        _store.WriteSecretKey(info.Fingerprint, Armor(secretRing.GetEncoded()));
        _store.Upsert(info);
        return info;
    }

    private static AsymmetricCipherKeyPair GenerateEd25519KeyPair(SecureRandom random)
    {
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(random));
        return generator.GenerateKeyPair();
    }

    private static AsymmetricCipherKeyPair GenerateX25519KeyPair(SecureRandom random)
    {
        var generator = new X25519KeyPairGenerator();
        generator.Init(new X25519KeyGenerationParameters(random));
        return generator.GenerateKeyPair();
    }

    private static AsymmetricCipherKeyPair GenerateRsaKeyPair(int bits, SecureRandom random)
    {
        var generator = new RsaKeyPairGenerator();
        generator.Init(new RsaKeyGenerationParameters(BigInteger.ValueOf(65537), random, bits, 25));
        return generator.GenerateKeyPair();
    }

    private static PgpSignatureSubpacketVector BuildKeyPackets(int flags, long expirationSeconds)
    {
        var packets = new List<SignatureSubpacket>
        {
            new KeyFlags(critical: false, flags),
        };

        if (expirationSeconds > 0)
        {
            packets.Add(new KeyExpirationTime(critical: false, expirationSeconds));
        }

        return PgpSignatureSubpacketVector.FromSubpackets(packets.ToArray());
    }

    // ------------------------------------------------------------------
    // Importación / exportación
    // ------------------------------------------------------------------

    private PgpKeyInfo ImportKey(PgpImportKeyRequest request, CancellationToken cancellationToken)
    {
        var armored = request.ArmoredKey ?? string.Empty;
        if (armored.Length == 0)
        {
            throw new PgpException(BridgeErrorCodes.PgpFailed, EmptyKeyMessage);
        }

        if (armored.Length > MaxArmoredChars)
        {
            throw new PgpException(BridgeErrorCodes.PgpFailed, KeyTooLargeMessage);
        }

        if (!armored.Contains("-----BEGIN PGP", StringComparison.Ordinal))
        {
            throw new PgpException(BridgeErrorCodes.PgpFailed, MalformedKeyMessage);
        }

        cancellationToken.ThrowIfCancellationRequested();

        PgpSecretKeyRing? secretRing = null;
        PgpPublicKeyRing? publicRing = null;
        try
        {
            foreach (var packet in ReadArmoredObjects(armored))
            {
                if (packet is PgpSecretKeyRing foundSecret)
                {
                    secretRing = foundSecret;
                    break;
                }

                if (packet is PgpPublicKeyRing foundPublic)
                {
                    publicRing = foundPublic;
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Datos corruptos o truncados: error estático, nunca el detalle de
            // BC (podría reflejar el contenido que el usuario pegó).
            throw new PgpException(BridgeErrorCodes.PgpFailed, MalformedKeyMessage);
        }

        if (secretRing is not null)
        {
            return ImportSecretRing(secretRing);
        }

        if (publicRing is not null)
        {
            return ImportPublicRing(publicRing);
        }

        throw new PgpException(BridgeErrorCodes.PgpFailed, MalformedKeyMessage);
    }

    private PgpKeyInfo ImportSecretRing(PgpSecretKeyRing secretRing)
    {
        // El anillo público se deriva de las claves secretas (sin descifrar
        // material privado) y se guarda junto a la copia privada.
        using var publicPackets = new MemoryStream();
        foreach (var publicKey in secretRing.GetPublicKeys())
        {
            var encoded = publicKey.GetEncoded();
            publicPackets.Write(encoded, 0, encoded.Length);
        }

        var publicRing = new PgpPublicKeyRing(publicPackets.ToArray());
        var info = ToInfo(publicRing, hasPrivate: true);

        _store.WritePublicKey(info.Fingerprint, Armor(publicRing.GetEncoded()));
        _store.WriteSecretKey(info.Fingerprint, Armor(secretRing.GetEncoded()));
        _store.Upsert(info);
        return info;
    }

    private PgpKeyInfo ImportPublicRing(PgpPublicKeyRing publicRing)
    {
        var fingerprint = Convert.ToHexString(publicRing.GetPublicKey().GetFingerprint());
        var info = ToInfo(publicRing, _store.ReadSecretKey(fingerprint) is not null);

        _store.WritePublicKey(info.Fingerprint, Armor(publicRing.GetEncoded()));
        _store.Upsert(info);
        return info;
    }

    private string ExportKey(PgpExportKeyRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var fingerprint = ResolveFingerprint(request.Fingerprint).Fingerprint;
        if (request.IncludePrivate)
        {
            return _store.ReadSecretKey(fingerprint)
                ?? throw new PgpException(BridgeErrorCodes.PgpFailed, NoPrivateMaterialMessage);
        }

        return _store.ReadPublicKey(fingerprint)
            ?? throw new PgpException(BridgeErrorCodes.PgpFailed, NoPublicMaterialMessage);
    }

    // ------------------------------------------------------------------
    // Criptografía: cifrar / descifrar / firmar / verificar (9.6)
    // ------------------------------------------------------------------

    private string Encrypt(PgpEncryptRequest request, CancellationToken cancellationToken)
    {
        var text = request.Text ?? string.Empty;
        if (text.Length > MaxArmoredChars)
        {
            throw new PgpException(BridgeErrorCodes.PgpFailed, TextTooLargeMessage);
        }

        var recipients = request.Recipients ?? Array.Empty<string>();
        if (recipients.Count == 0)
        {
            throw new PgpException(BridgeErrorCodes.PgpFailed, RecipientsRequiredMessage);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var keys = _store.Load();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var encryptionKeys = new List<PgpPublicKey>();
        foreach (var recipient in recipients)
        {
            var info = ResolveRecipient(keys, recipient);
            if (!seen.Add(info.Fingerprint))
            {
                continue; // destinatario repetido: una sola clave en el sobre
            }

            if (!info.CanEncrypt)
            {
                throw new PgpException(BridgeErrorCodes.PgpFailed, RecipientCannotEncryptMessage);
            }

            var armored = _store.ReadPublicKey(info.Fingerprint)
                ?? throw new PgpException(BridgeErrorCodes.PgpFailed, FingerprintNotFoundMessage);
            encryptionKeys.Add(FindEncryptionKey(ReadPublicRing(armored)));
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Sin compresión previa (ponytail): el literal va dentro de un sobre
        // AES-256 con paquete MDC de integridad, como gpg --encrypt --armor.
        using var output = new MemoryStream();
        using (var armored = new ArmoredOutputStream(output, addVersionHeader: false))
        {
            var generator = new PgpEncryptedDataGenerator(
                SymmetricKeyAlgorithmTag.Aes256,
                withIntegrityPacket: true,
                new SecureRandom());
            foreach (var key in encryptionKeys)
            {
                generator.AddMethod(key);
            }

            // Solo la variante Open(stream, byte[]) produce un sobre legible:
            // Open(stream, long) de BC 2.7.0 trunca el stream ("Premature end
            // of stream") — verificado con una sonda contra el ensamblado.
            var cipherChunk = new byte[BufferSize];
            using (var encrypted = generator.Open(armored, cipherChunk))
            {
                // Cada stream anidado usa su propio buffer (quedan retenidos).
                var literalChunk = new byte[BufferSize];
                var literal = new PgpLiteralDataGenerator();
                using (var literalStream = literal.Open(
                    encrypted, 'b', "message.txt", DateTime.UtcNow, literalChunk))
                {
                    var plain = Encoding.UTF8.GetBytes(text);
                    literalStream.Write(plain, 0, plain.Length);
                }
            }
        }

        return Encoding.UTF8.GetString(output.ToArray());
    }

    private string Decrypt(PgpDecryptRequest request, CancellationToken cancellationToken)
    {
        var armoredMessage = request.ArmoredMessage ?? string.Empty;
        if (armoredMessage.Length == 0)
        {
            throw new PgpException(BridgeErrorCodes.PgpFailed, MessageRequiredMessage);
        }

        if (armoredMessage.Length > MaxArmoredChars)
        {
            throw new PgpException(BridgeErrorCodes.PgpFailed, MessageTooLargeMessage);
        }

        var passphrase = ValidatePassphrase(request.Passphrase);
        cancellationToken.ThrowIfCancellationRequested();

        var privateKeys = _store.Load().Where(key => key.HasPrivate).ToList();
        if (privateKeys.Count == 0)
        {
            throw new PgpException(BridgeErrorCodes.PgpFailed, NoPrivateKeysMessage);
        }

        PgpEncryptedDataList? encryptedList;
        try
        {
            using var armored = new ArmoredInputStream(new MemoryStream(Encoding.UTF8.GetBytes(armoredMessage)));
            var factory = new PgpObjectFactory(armored);
            encryptedList = factory.NextPgpObject() as PgpEncryptedDataList;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Armoured ilegible/truncado: mensaje estático — el detalle de BC
            // podría reflejar el contenido que el usuario pegó.
            throw new PgpException(BridgeErrorCodes.PgpFailed, MalformedPgpMessage);
        }

        if (encryptedList is null)
        {
            throw new PgpException(BridgeErrorCodes.PgpFailed, MalformedPgpMessage);
        }

        for (var i = 0; i < encryptedList.Count; i++)
        {
            // Solo el sobre con clave pública (PKESK) que produce Encrypt: los
            // sobres de passphrase pura no son de nuestro flujo (ponytail).
            if (encryptedList[i] is not PgpPublicKeyEncryptedData recipient)
            {
                continue;
            }

            var secretKey = FindSecretKey(privateKeys, recipient.KeyId);
            if (secretKey is null)
            {
                continue;
            }

            PgpPrivateKey privateKey;
            try
            {
                privateKey = secretKey.ExtractPrivateKey((passphrase ?? string.Empty).ToCharArray());
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // BC solo falla aquí con la contraseña incorrecta o clave dañada.
                throw new PgpException(BridgeErrorCodes.PgpFailed, WrongPassphraseMessage);
            }

            byte[] plain;
            try
            {
                using var clear = recipient.GetDataStream(privateKey);
                plain = ReadLiteralData(clear);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (PgpException)
            {
                throw; // mensajes propios (tamaño, esquema no soportado)
            }
            catch (Exception)
            {
                throw new PgpException(BridgeErrorCodes.PgpFailed, CorruptedPgpMessage);
            }

            if (recipient.IsIntegrityProtected() && !recipient.Verify())
            {
                throw new PgpException(BridgeErrorCodes.PgpFailed, CorruptedPgpMessage);
            }

            return Encoding.UTF8.GetString(plain);
        }

        throw new PgpException(BridgeErrorCodes.PgpFailed, NotEncryptedForKeyMessage);
    }

    private string Sign(PgpSignRequest request, CancellationToken cancellationToken)
    {
        var text = request.Text ?? string.Empty;
        if (text.Length > MaxArmoredChars)
        {
            throw new PgpException(BridgeErrorCodes.PgpFailed, TextTooLargeMessage);
        }

        var fingerprint = ResolveFingerprint(request.Fingerprint).Fingerprint;
        var passphrase = ValidatePassphrase(request.Passphrase);
        cancellationToken.ThrowIfCancellationRequested();

        var secretArmored = _store.ReadSecretKey(fingerprint)
            ?? throw new PgpException(BridgeErrorCodes.PgpFailed, NoPrivateMaterialMessage);

        // La clave primaria del anillo es la de firma (la subclave solo cifra).
        var secretKey = ReadSecretRing(secretArmored).GetSecretKey();

        PgpPrivateKey privateKey;
        try
        {
            privateKey = secretKey.ExtractPrivateKey((passphrase ?? string.Empty).ToCharArray());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new PgpException(BridgeErrorCodes.PgpFailed, WrongPassphraseMessage);
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // Firma binaria separada (type 0x00) sobre los bytes UTF-8
            // exactos: la misma convención que Verify, sin normalizar EOL.
            var generator = new PgpSignatureGenerator(secretKey.PublicKey.Algorithm, HashAlgorithmTag.Sha256);
            generator.InitSign(PgpSignature.BinaryDocument, privateKey);
            var plain = Encoding.UTF8.GetBytes(text);
            generator.Update(plain, 0, plain.Length);
            var signature = generator.Generate();

            using var output = new MemoryStream();
            using (var armored = new ArmoredOutputStream(output, addVersionHeader: false))
            {
                signature.Encode(armored);
            }

            return Encoding.UTF8.GetString(output.ToArray());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Clave importada sin capacidad de firma u otro fallo de BC:
            throw new PgpException(BridgeErrorCodes.PgpFailed, CannotSignMessage);
        }
    }

    private PgpVerifyOutcome Verify(PgpVerifyRequest request, CancellationToken cancellationToken)
    {
        var text = request.Text ?? string.Empty;
        if (text.Length > MaxArmoredChars)
        {
            throw new PgpException(BridgeErrorCodes.PgpFailed, TextTooLargeMessage);
        }

        var signature = request.Signature ?? string.Empty;
        if (signature.Length == 0)
        {
            return new PgpVerifyOutcome(false, EmptySignatureMessage, null);
        }

        if (signature.Length > MaxArmoredChars)
        {
            throw new PgpException(BridgeErrorCodes.PgpFailed, MessageTooLargeMessage);
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Una firma ilegible es un resultado legítimo con Valid = false (§6),
        // no un error del puente.
        PgpSignature? detached = null;
        try
        {
            using var armored = new ArmoredInputStream(new MemoryStream(Encoding.UTF8.GetBytes(signature)));
            var factory = new PgpObjectFactory(armored);
            if (factory.NextPgpObject() is PgpSignatureList list && list.Count > 0)
            {
                detached = list[0];
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Armoured corrupto: se traduce a Valid = false más abajo.
        }

        if (detached is null)
        {
            return new PgpVerifyOutcome(false, InvalidSignatureMessage, null);
        }

        var keyId = detached.KeyId.ToString("X16", CultureInfo.InvariantCulture);
        var info = _store.Load().FirstOrDefault(key => key.KeyId.Equals(keyId, StringComparison.OrdinalIgnoreCase));
        var verifyKey = info is null
            ? null
            : FindPublicKeyById(_store.ReadPublicKey(info.Fingerprint), detached.KeyId);
        if (verifyKey is null)
        {
            return new PgpVerifyOutcome(false, SignerNotInKeyringMessage, null);
        }

        bool valid;
        try
        {
            detached.InitVerify(verifyKey);
            var plain = Encoding.UTF8.GetBytes(text);
            detached.Update(plain, 0, plain.Length);
            valid = detached.Verify();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Algoritmo de firma no soportable → no se pudo verificar.
            return new PgpVerifyOutcome(false, InvalidSignatureMessage, null);
        }

        return valid
            ? new PgpVerifyOutcome(true, null, info)
            : new PgpVerifyOutcome(false, SignatureMismatchMessage, info);
    }

    private static PgpPublicKey FindEncryptionKey(PgpPublicKeyRing ring)
    {
        foreach (var key in ring.GetPublicKeys())
        {
            if (key.IsEncryptionKey)
            {
                return key;
            }
        }

        throw new PgpException(BridgeErrorCodes.PgpFailed, RecipientCannotEncryptMessage);
    }

    /// <summary>
    /// Resolución exacta por prefijo de fingerprint (compartida por export,
    /// sign y verify): vacío / desconocido / ambiguo → error controlado.
    /// </summary>
    private PgpKeyInfo ResolveFingerprint(string? fingerprint)
    {
        var needle = (fingerprint ?? string.Empty).Trim();
        if (needle.Length == 0)
        {
            throw new PgpException(BridgeErrorCodes.PgpFailed, FingerprintRequiredMessage);
        }

        var matches = _store
            .Load()
            .Where(key => key.Fingerprint.StartsWith(needle, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count == 0)
        {
            throw new PgpException(BridgeErrorCodes.PgpFailed, FingerprintNotFoundMessage);
        }

        if (matches.Count > 1)
        {
            throw new PgpException(BridgeErrorCodes.PgpFailed, AmbiguousFingerprintMessage);
        }

        return matches[0];
    }

    /// <summary>
    /// Destinatario: contiene '@' → email exacto (OrdinalIgnoreCase); en
    /// cambio, prefijo de fingerprint. Cero → no encontrado; varios → ambiguo.
    /// </summary>
    private static PgpKeyInfo ResolveRecipient(IReadOnlyList<PgpKeyInfo> keys, string? recipient)
    {
        var needle = (recipient ?? string.Empty).Trim();
        if (needle.Length == 0)
        {
            throw new PgpException(BridgeErrorCodes.PgpFailed, RecipientNotFoundMessage);
        }

        List<PgpKeyInfo> matches;
        if (needle.Contains('@'))
        {
            matches = keys
                .Where(key => key.Email.Equals(needle, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        else
        {
            matches = keys
                .Where(key => key.Fingerprint.StartsWith(needle, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (matches.Count == 0)
        {
            throw new PgpException(BridgeErrorCodes.PgpFailed, RecipientNotFoundMessage);
        }

        if (matches.Count > 1)
        {
            // Mensaje de fingerprint: también sirve cuando el email repetido
            // apunta a varias claves (la solución es igual: más dígitos).
            throw new PgpException(BridgeErrorCodes.PgpFailed, AmbiguousFingerprintMessage);
        }

        return matches[0];
    }

    private PgpSecretKey? FindSecretKey(IReadOnlyList<PgpKeyInfo> privateKeys, long keyId)
    {
        foreach (var info in privateKeys)
        {
            var armored = _store.ReadSecretKey(info.Fingerprint);
            if (armored is null)
            {
                continue;
            }

            PgpSecretKeyRing ring;
            try
            {
                ring = ReadSecretRing(armored);
            }
            catch (PgpException)
            {
                continue; // archivo local ilegible: se prueba con el resto (ponytail)
            }

            foreach (var key in ring.GetSecretKeys())
            {
                if (key.KeyId == keyId)
                {
                    return key;
                }
            }
        }

        return null;
    }

    private static PgpPublicKey? FindPublicKeyById(string? armored, long keyId)
    {
        if (armored is null)
        {
            return null;
        }

        try
        {
            foreach (var packet in ReadArmoredObjects(armored))
            {
                if (packet is not PgpPublicKeyRing ring)
                {
                    continue;
                }

                foreach (var key in ring.GetPublicKeys())
                {
                    if (key.KeyId == keyId)
                    {
                        return key;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return null; // archivo local ilegible → firmante desconocido
        }

        return null;
    }

    private static PgpPublicKeyRing ReadPublicRing(string armored)
    {
        try
        {
            foreach (var packet in ReadArmoredObjects(armored))
            {
                if (packet is PgpPublicKeyRing ring)
                {
                    return ring;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new PgpException(BridgeErrorCodes.PgpFailed, MalformedKeyMessage);
        }

        throw new PgpException(BridgeErrorCodes.PgpFailed, MalformedKeyMessage);
    }

    private static PgpSecretKeyRing ReadSecretRing(string armored)
    {
        try
        {
            foreach (var packet in ReadArmoredObjects(armored))
            {
                if (packet is PgpSecretKeyRing ring)
                {
                    return ring;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Datos corruptos: error estático, nunca el detalle de BC.
            throw new PgpException(BridgeErrorCodes.PgpFailed, MalformedKeyMessage);
        }

        throw new PgpException(BridgeErrorCodes.PgpFailed, MalformedKeyMessage);
    }

    private static byte[] ReadLiteralData(Stream clear)
    {
        var factory = new PgpObjectFactory(clear);
        var obj = factory.NextPgpObject();

        if (obj is PgpCompressedData compressed)
        {
            // gpg comprime por defecto dentro del sobre cifrado.
            using var inner = compressed.GetDataStream();
            factory = new PgpObjectFactory(inner);
            obj = factory.NextPgpObject();
        }

        // Firmas en línea y otros esquemas: fuera de alcance (ponytail) —
        // el flujo pgp.* produce firmas separadas, nunca incrustadas.
        if (obj is not PgpLiteralData literal)
        {
            throw new PgpException(BridgeErrorCodes.PgpFailed, MalformedPgpMessage);
        }

        using var input = literal.GetInputStream();
        using var buffer = new MemoryStream();
        var chunk = new byte[BufferSize];
        long total = 0;
        int read;
        while ((read = input.Read(chunk, 0, chunk.Length)) > 0)
        {
            total += read;
            if (total > MaxPlainBytes)
            {
                throw new PgpException(BridgeErrorCodes.PgpFailed, MessageTooLargeMessage);
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    // ------------------------------------------------------------------
    // Utilidades BC
    // ------------------------------------------------------------------

    private static IReadOnlyList<PgpObject> ReadArmoredObjects(string armored)
    {
        using var stream = new ArmoredInputStream(new MemoryStream(Encoding.UTF8.GetBytes(armored)));
        var factory = new PgpObjectFactory(stream);
        return factory.AllPgpObjects().ToList();
    }

    private static string Armor(byte[] packets)
    {
        using var buffer = new MemoryStream();
        using (var armored = new ArmoredOutputStream(buffer, addVersionHeader: false))
        {
            armored.Write(packets, 0, packets.Length);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static PgpKeyInfo ToInfo(PgpPublicKeyRing ring, bool hasPrivate)
    {
        var master = ring.GetPublicKey();

        var userId = string.Empty;
        foreach (var candidate in master.GetUserIds())
        {
            userId = candidate;
            break;
        }

        var (name, email) = SplitUserId(userId);
        int? bits = IsRsa(master.Algorithm) ? master.BitStrength : null;
        var created = ToUtc(master.CreationTime);
        var validSeconds = master.GetValidSeconds();
        DateTimeOffset? expires = validSeconds > 0 ? created.AddSeconds(validSeconds) : null;

        var canEncrypt = false;
        foreach (var key in ring.GetPublicKeys())
        {
            if (key.IsEncryptionKey)
            {
                canEncrypt = true;
                break;
            }
        }

        return new PgpKeyInfo(
            Convert.ToHexString(master.GetFingerprint()),
            master.KeyId.ToString("X16", CultureInfo.InvariantCulture),
            userId,
            name,
            email,
            MapAlgorithm(master.Algorithm),
            bits,
            created,
            expires,
            hasPrivate,
            canEncrypt);
    }

    private static (string Name, string Email) SplitUserId(string userId)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return (string.Empty, string.Empty);
        }

        var match = UserIdPattern.Match(userId);
        if (!match.Success)
        {
            return (userId, string.Empty);
        }

        return (match.Groups[1].Value.Trim(), match.Groups[2].Value.Trim());
    }

    private static string MapAlgorithm(PublicKeyAlgorithmTag algorithm) => algorithm switch
    {
        PublicKeyAlgorithmTag.RsaGeneral
            or PublicKeyAlgorithmTag.RsaEncrypt
            or PublicKeyAlgorithmTag.RsaSign => PgpAlgorithms.Rsa,
        PublicKeyAlgorithmTag.EdDsa_Legacy or PublicKeyAlgorithmTag.Ed25519 => PgpAlgorithms.Ed25519,
        PublicKeyAlgorithmTag.Ed448 => "ed448",
        PublicKeyAlgorithmTag.ECDH => "ecdh",
        PublicKeyAlgorithmTag.ECDsa => "ecdsa",
        PublicKeyAlgorithmTag.Dsa => "dsa",
        PublicKeyAlgorithmTag.ElGamalEncrypt or PublicKeyAlgorithmTag.ElGamalGeneral => "elgamal",
        PublicKeyAlgorithmTag.X25519 => "x25519",
        PublicKeyAlgorithmTag.X448 => "x448",
        PublicKeyAlgorithmTag.DiffieHellman => "dh",
        _ => algorithm.ToString().ToLowerInvariant(),
    };

    private static bool IsRsa(PublicKeyAlgorithmTag algorithm) =>
        algorithm is PublicKeyAlgorithmTag.RsaGeneral
            or PublicKeyAlgorithmTag.RsaEncrypt
            or PublicKeyAlgorithmTag.RsaSign;

    private static DateTimeOffset ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => new DateTimeOffset(value),
        DateTimeKind.Local => new DateTimeOffset(value).ToUniversalTime(),
        _ => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)),
    };

    // ------------------------------------------------------------------
    // Validación de entrada (mensajes estáticos, jamás la entrada)
    // ------------------------------------------------------------------

    private static void ValidateIdentity(string name, string email)
    {
        if (name.Length == 0 || name.Length > MaxNameLength || HasForbiddenNameChar(name))
        {
            throw new PgpException(BridgeErrorCodes.PgpFailed, InvalidNameMessage);
        }

        if (email.Length > MaxEmailLength || !EmailPattern.IsMatch(email))
        {
            throw new PgpException(BridgeErrorCodes.PgpFailed, InvalidEmailMessage);
        }
    }

    private static bool HasForbiddenNameChar(string value)
    {
        foreach (var character in value)
        {
            if (character is '<' or '>' || char.IsControl(character))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeAlgorithm(string? algorithm)
    {
        var normalized = (algorithm ?? string.Empty).Trim().ToLowerInvariant();
        return normalized.Length == 0 ? PgpAlgorithms.Ed25519 : normalized;
    }

    private static int? ValidateBits(string algorithm, int? bits)
    {
        if (algorithm == PgpAlgorithms.Ed25519)
        {
            if (bits is not null)
            {
                throw new PgpException(BridgeErrorCodes.PgpFailed, InvalidBitsMessage);
            }

            return null;
        }

        var resolved = bits ?? DefaultRsaBits;
        if (!PgpAlgorithms.RsaBits.Contains(resolved))
        {
            throw new PgpException(BridgeErrorCodes.PgpFailed, InvalidBitsMessage);
        }

        return resolved;
    }

    private static int? ValidateExpiration(int? expirationDays)
    {
        if (expirationDays is null)
        {
            return null;
        }

        if (expirationDays < 1 || expirationDays > MaxExpirationDays)
        {
            throw new PgpException(BridgeErrorCodes.PgpFailed, InvalidExpirationMessage);
        }

        return expirationDays;
    }

    private static string? ValidatePassphrase(string? passphrase)
    {
        if (string.IsNullOrEmpty(passphrase))
        {
            return null;
        }

        if (passphrase.Length > MaxPassphraseLength || passphrase.Contains('\0'))
        {
            throw new PgpException(BridgeErrorCodes.PgpFailed, InvalidPassphraseMessage);
        }

        return passphrase;
    }
}
