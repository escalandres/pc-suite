namespace PCSuite.Core.Web;

/// <summary>
/// Resultado de la inspección local de una URL (homógrafos, punycode,
/// scripts mixtos, typosquatting).
/// </summary>
public sealed record UrlInspection(
    string Host,
    IReadOnlyList<string> Scripts,
    IReadOnlyList<SuspiciousChar> Suspicious,
    bool Punycode,
    bool MixedScripts,
    string Verdict,
    IReadOnlyList<string> TyposquatCandidates);

public sealed record SuspiciousChar(string Char, string Script);

/// <summary>Resultado de la limpieza de parámetros de tracking de una URL.</summary>
public sealed record UrlCleanResult(string OriginalUrl, string CleanedUrl, IReadOnlyList<string> Removed);

/// <summary>Resultado de comparar dos URLs.</summary>
public sealed record UrlCompareResult(
    string UrlA,
    string UrlB,
    IReadOnlyList<string> Differences);

/// <summary>Resultado de la inspección TLS de un host.</summary>
public sealed record TlsInspection(
    string Host,
    string? Issuer,
    string? Subject,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidTo,
    IReadOnlyList<string> SubjectAlternativeNames,
    bool Expired,
    bool ExpiresSoon,
    string Verdict,
    string? Detail);

/// <summary>Resultado de la inspección de cabeceras HTTP de una URL.</summary>
public sealed record HttpHeadersInspection(
    string Url,
    string? Hsts,
    string? ContentSecurityPolicy,
    string? XFrameOptions,
    string? XContentTypeOptions,
    IReadOnlyList<string> Cookies,
    int StatusCode,
    string Verdict,
    string? Detail);

/// <summary>Resultado de expandir una URL (cadena de redirecciones).</summary>
public sealed record UrlExpansion(
    string OriginalUrl,
    string FinalUrl,
    IReadOnlyList<string> Chain,
    bool Shortened,
    string? Detail);

/// <summary>Un registro DNS.</summary>
public sealed record DnsRecord(string Type, string Value, int Ttl);

/// <summary>Resultado de una consulta DNS.</summary>
public sealed record DnsInspection(
    string Host,
    IReadOnlyList<DnsRecord> Records,
    string? Detail);

/// <summary>Resultado de la validación de un correo electrónico.</summary>
public sealed record EmailInspection(
    string Email,
    bool FormatValid,
    string? Domain,
    IReadOnlyList<string> MxRecords,
    string? Spf,
    string? Dmarc,
    string? DkimSelector,
    string Verdict,
    string? Detail);