using PCSuite.Contracts;
using PCSuite.Core.Certificates;
using PCSuite.Core.Operations;
// Alias: PgpException de Core colisiona con Org.BouncyCastle en otros archivos.
using PcsSuitePgpException = PCSuite.Core.PGP.PgpException;

namespace PCSuite.Infrastructure.Operations;

/// <summary>Resultado de cert.x509.generate.</summary>
public sealed class X509GenerateResultDto
{
    public string CertificatePem { get; init; } = "";
    public string PrivateKeyPath { get; init; } = "";
    public string? PfxPath { get; init; }
    public string Fingerprint { get; init; } = "";
    public string SerialNumber { get; init; } = "";
    public string Subject { get; init; } = "";
    public string ValidFrom { get; init; } = "";
    public string ValidTo { get; init; } = "";
    public string Algorithm { get; init; } = "";
}

/// <summary>Payload de cert.x509.generate.</summary>
public sealed class X509GeneratePayload
{
    public string Algorithm { get; init; } = "rsa4096";
    public string CommonName { get; init; } = "";
    public string? Organization { get; init; }
    public string? OrganizationalUnit { get; init; }
    public string? Country { get; init; }
    public IReadOnlyList<string>? SubjectAltNames { get; init; }
    public int ValidityDays { get; init; } = 365;
    public string? PfxPassword { get; init; }
}

/// <summary>Resultado de cert.inspect.</summary>
public sealed class CertificateInspectResultDto
{
    public string Kind { get; init; } = "";
    public string KindDescription { get; init; } = "";
    public string? Fingerprint { get; init; }
    public IReadOnlyDictionary<string, string> Details { get; init; } =
        new Dictionary<string, string>();
}

/// <summary>Payload de cert.inspect.</summary>
public sealed class CertificateInspectPayload
{
    public string Path { get; init; } = "";
    public string? Password { get; init; }
}

/// <summary>Resultado de cert.convert.</summary>
public sealed class CertificateConvertResultDto
{
    public string OutputPath { get; init; } = "";
    public string OutputFormat { get; init; } = "";
}

/// <summary>Payload de cert.convert.</summary>
public sealed class CertificateConvertPayload
{
    public string InputPath { get; init; } = "";
    public string InputFormat { get; init; } = "";
    public string OutputFormat { get; init; } = "";
    public string? InputPassword { get; init; }
    public string? OutputPassword { get; init; }
}

/// <summary>Resultado de cert.pgp.signFile.</summary>
public sealed class PgpSignFileResultDto
{
    public string SignaturePath { get; init; } = "";
    public string Fingerprint { get; init; } = "";
}

/// <summary>Payload de cert.pgp.signFile.</summary>
public sealed class PgpSignFilePayload
{
    public string Fingerprint { get; init; } = "";
    public string InputPath { get; init; } = "";
    public string? Passphrase { get; init; }
}

/// <summary>Resultado de cert.csr.generate (Task 19.5).</summary>
public sealed class CsrGenerateResultDto
{
    /// <summary>CSR en PEM (-----BEGIN CERTIFICATE REQUEST-----).</summary>
    public string CsrPem { get; init; } = "";

    /// <summary>Ruta del archivo con la clave privada generada (CSR + clave se guardan por separado).</summary>
    public string PrivateKeyPath { get; init; } = "";

    /// <summary>Ruta del archivo .pem con el CSR (listo para enviar a la CA).</summary>
    public string CsrPath { get; init; } = "";
}

/// <summary>Payload de cert.csr.generate.</summary>
public sealed class CsrGeneratePayload
{
    public string CommonName { get; init; } = "";
    public string? Organization { get; init; }
    public string? Country { get; init; }
    /// <summary>rsa2048 | rsa4096 | ecdsa-p256 | ecdsa-p384 | ed25519.</summary>
    public string Algorithm { get; init; } = "rsa2048";
    public IReadOnlyList<string>? SubjectAltNames { get; init; }
}

/// <summary>Resultado de cert.pgp.verifyFile (Task 19.2, cierre del ciclo).</summary>
public sealed class PgpVerifyFileResultDto
{
    /// <summary>¿La firma verifica contra el archivo original? Una firma inválida
    /// es un RESULTADO legítimo (valid=false), no un error del puente.</summary>
    public bool Valid { get; init; }

    /// <summary>Motivo legible cuando Valid=false (sin clave del firmante, contenido alterado…).</summary>
    public string? Detail { get; init; }

    /// <summary>Identidad del firmante cuando la clave pública está en el keyring.</summary>
    public PgpSignerDto? Signer { get; init; }
}

/// <summary>Firma del archivo verificada.</summary>
public sealed class PgpSignerDto
{
    public string Fingerprint { get; init; } = "";
    public string UserId { get; init; } = "";
    public string Name { get; init; } = "";
    public string Email { get; init; } = "";
    public string Algorithm { get; init; } = "";
}

/// <summary>Payload de cert.pgp.verifyFile.</summary>
public sealed class PgpVerifyFilePayload
{
    /// <summary>Archivo ORIGINAL firmado (la firma detached lo cubre).</summary>
    public string InputPath { get; init; } = "";

    /// <summary>Firma .sig (armoured) generada junto al original.</summary>
    public string SignaturePath { get; init; } = "";
}

// =====================================================================
// Operaciones del bridge
// =====================================================================

/// <summary>cert.x509.generate — certificado X.509 autofirmado (Task 19.1).</summary>
public sealed class CertX509GenerateOperation(ICertificateService service) : IOperation
{
    public string Id => OperationIds.CertX509Generate;
    public bool RequiresElevation => false;

    public async Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken ct)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out X509GeneratePayload payload)
            || payload is null || string.IsNullOrEmpty(payload.CommonName))
        {
            return OperationResult.Fail("error",
                "El payload debe ser { algorithm, commonName, ... }.");
        }

        try
        {
            var result = await service.GenerateX509Async(new X509GenerateRequest
            {
                Algorithm = payload.Algorithm,
                CommonName = payload.CommonName,
                Organization = payload.Organization,
                OrganizationalUnit = payload.OrganizationalUnit,
                Country = payload.Country,
                SubjectAltNames = payload.SubjectAltNames,
                ValidityDays = payload.ValidityDays,
                PfxPassword = payload.PfxPassword,
            }, ct).ConfigureAwait(false);

            var dto = new X509GenerateResultDto
            {
                CertificatePem = result.CertificatePem,
                PrivateKeyPath = result.PrivateKeyPath,
                PfxPath = result.PfxPath,
                Fingerprint = result.Fingerprint,
                SerialNumber = result.SerialNumber,
                Subject = result.Subject,
                ValidFrom = result.ValidFrom.ToString("yyyy-MM-dd"),
                ValidTo = result.ValidTo.ToString("yyyy-MM-dd"),
                Algorithm = result.Algorithm,
            };
            return OperationResult.Ok(BridgeJson.ToJson(dto));
        }
        catch (InvalidOperationException ex)
        {
            return OperationResult.Fail("x509_failed", ex.Message);
        }
    }
}

/// <summary>cert.inspect — inspección universal de certificados/claves (Task 19.3).</summary>
public sealed class CertInspectOperation(ICertificateService service) : IOperation
{
    public string Id => OperationIds.CertInspect;
    public bool RequiresElevation => false;

    public async Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken ct)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out CertificateInspectPayload payload)
            || payload is null || string.IsNullOrEmpty(payload.Path))
        {
            return OperationResult.Fail("error", "El payload debe ser { path }.");
        }

        try
        {
            var result = await service.InspectAsync(payload.Path, payload.Password, ct)
                .ConfigureAwait(false);

            var dto = new CertificateInspectResultDto
            {
                Kind = result.Kind.ToString().ToLowerInvariant(),
                KindDescription = result.KindDescription,
                Fingerprint = result.Fingerprint,
                Details = new Dictionary<string, string>(result.Details),
            };
            return OperationResult.Ok(BridgeJson.ToJson(dto));
        }
        catch (InvalidOperationException ex)
        {
            return OperationResult.Fail("inspect_failed", ex.Message);
        }
    }
}

/// <summary>cert.convert — conversión de formatos PEM↔DER (Task 19.4).</summary>
public sealed class CertConvertOperation(ICertificateService service) : IOperation
{
    public string Id => OperationIds.CertConvert;
    public bool RequiresElevation => false;

    public async Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken ct)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out CertificateConvertPayload payload)
            || payload is null || string.IsNullOrEmpty(payload.InputPath))
        {
            return OperationResult.Fail("error",
                "El payload debe ser { inputPath, inputFormat, outputFormat }.");
        }

        try
        {
            var result = await service.ConvertAsync(new CertificateConvertRequest
            {
                InputPath = payload.InputPath,
                InputFormat = payload.InputFormat,
                OutputFormat = payload.OutputFormat,
                InputPassword = payload.InputPassword,
                OutputPassword = payload.OutputPassword,
            }, ct).ConfigureAwait(false);

            return OperationResult.Ok(BridgeJson.ToJson(new CertificateConvertResultDto
            {
                OutputPath = result.OutputPath,
                OutputFormat = result.OutputFormat,
            }));
        }
        catch (InvalidOperationException ex)
        {
            return OperationResult.Fail("convert_failed", ex.Message);
        }
    }
}

/// <summary>cert.csr.generate — CSR PKCS#10 con SANs en extensionRequest (Task 19.5).</summary>
public sealed class CertCsrGenerateOperation(ICertificateService service) : IOperation
{
    public string Id => OperationIds.CertCsrGenerate;
    public bool RequiresElevation => false;

    public async Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken ct)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out CsrGeneratePayload payload)
            || payload is null || string.IsNullOrEmpty(payload.CommonName))
        {
            return OperationResult.Fail("error",
                "El payload debe ser { commonName, algorithm?, organization?, country?, subjectAltNames? }.");
        }

        try
        {
            var result = await service.GenerateCsrAsync(new CsrGenerateRequest
            {
                CommonName = payload.CommonName,
                Organization = payload.Organization,
                Country = payload.Country,
                Algorithm = payload.Algorithm,
                SubjectAltNames = payload.SubjectAltNames,
            }, ct).ConfigureAwait(false);

            return OperationResult.Ok(BridgeJson.ToJson(new CsrGenerateResultDto
            {
                CsrPem = result.CsrPem,
                PrivateKeyPath = result.PrivateKeyPath,
                CsrPath = InferCsrPath(result),
            }));
        }
        catch (InvalidOperationException ex)
        {
            return OperationResult.Fail("csr_failed", ex.Message);
        }
    }

    /// <summary>La clave privada y el CSR se guardan con el mismo timestamp.</summary>
    private static string InferCsrPath(CsrGenerateResult result)
    {
        var keyName = Path.GetFileName(result.PrivateKeyPath);
        if (keyName.StartsWith("csr-private-key-", StringComparison.Ordinal))
        {
            var timestamp = keyName["csr-private-key-".Length..].Replace(".pem", "");
            return Path.Combine(
                Path.GetDirectoryName(result.PrivateKeyPath) ?? "",
                $"csr-{timestamp}.pem");
        }
        return "";
    }
}

/// <summary>
/// cert.pgp.verifyFile — verifica una firma detached (.sig) contra el archivo
/// ORIGINAL (Task 19.2, cierre del ciclo: crear → exportar → firmar →
/// importar → verificar). Una firma inválida es un RESULTADO con valid=false
/// (mismo contrato que pgp.verify, §6): jamás INTERNAL_ERROR ni stack traces.
/// </summary>
public sealed class CertPgpVerifyFileOperation(ICertificateService service) : IOperation
{
    public string Id => OperationIds.CertPgpVerifyFile;
    public bool RequiresElevation => false;

    public async Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken ct)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out PgpVerifyFilePayload payload)
            || payload is null || string.IsNullOrEmpty(payload.InputPath)
            || string.IsNullOrEmpty(payload.SignaturePath))
        {
            return OperationResult.Fail("error",
                "El payload debe ser { inputPath, signaturePath }.");
        }

        try
        {
            var outcome = await service.VerifyFileWithPgpAsync(
                payload.InputPath, payload.SignaturePath, ct).ConfigureAwait(false);

            return OperationResult.Ok(BridgeJson.ToJson(new PgpVerifyFileResultDto
            {
                Valid = outcome.Valid,
                Detail = outcome.Detail,
                Signer = outcome.Signer is null ? null : new PgpSignerDto
                {
                    Fingerprint = outcome.Signer.Fingerprint,
                    UserId = outcome.Signer.UserId,
                    Name = outcome.Signer.Name,
                    Email = outcome.Signer.Email,
                    Algorithm = outcome.Signer.Algorithm,
                },
            }));
        }
        catch (PcsSuitePgpException exception)
        {
            return OperationResult.Fail(exception.ErrorCode, exception.ErrorMessage);
        }
        catch (InvalidOperationException ex)
        {
            return OperationResult.Fail("verify_failed", ex.Message);
        }
    }
}

/// <summary>cert.pgp.signFile — firmar archivo con clave PGP (Task 19.2 / 17.6).</summary>
public sealed class CertPgpSignFileOperation(ICertificateService service) : IOperation
{
    public string Id => OperationIds.CertPgpSignFile;
    public bool RequiresElevation => false;

    public async Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken ct)
    {
        if (!ToolMapper.TryParsePayload(context.Payload, out PgpSignFilePayload payload)
            || payload is null || string.IsNullOrEmpty(payload.Fingerprint)
            || string.IsNullOrEmpty(payload.InputPath))
        {
            return OperationResult.Fail("error",
                "El payload debe ser { fingerprint, inputPath, passphrase? }.");
        }

        try
        {
            var result = await service.SignFileWithPgpAsync(
                payload.Fingerprint, payload.InputPath, payload.Passphrase, ct)
                .ConfigureAwait(false);

            return OperationResult.Ok(BridgeJson.ToJson(new PgpSignFileResultDto
            {
                SignaturePath = result.SignaturePath,
                Fingerprint = result.Fingerprint,
            }));
        }
        catch (PcsSuitePgpException exception)
        {
            // Fallo controlado del dominio PGP (clave ausente, passphrase
            // incorrecta…): código + mensaje aptos para la UI, jamás stack
            // trace ni el passphrase.
            return OperationResult.Fail(exception.ErrorCode, exception.ErrorMessage);
        }
        catch (InvalidOperationException ex)
        {
            return OperationResult.Fail("sign_failed", ex.Message);
        }
    }
}
