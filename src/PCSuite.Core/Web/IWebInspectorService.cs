namespace PCSuite.Core.Web;

/// <summary>
/// Fachada de inspección web (Task 040): homógrafos, punycode, typosquatting,
/// limpieza de tracking, comparación de URLs, DNS, TLS, cabeceras HTTP,
/// expansión de enlaces y validación de correo. PCSuite orquesta; las
/// comprobaciones locales no tocan la red; las de red son explícitas y con
/// timeout/cancelación.
/// </summary>
public interface IWebInspectorService
{
    Task<UrlInspection> InspectUrlAsync(string url, CancellationToken cancellationToken = default);

    UrlCleanResult CleanTracking(string url);

    UrlCompareResult CompareUrls(string urlA, string urlB);

    Task<TlsInspection> CheckTlsAsync(string host, CancellationToken cancellationToken = default);

    Task<HttpHeadersInspection> CheckHeadersAsync(string url, CancellationToken cancellationToken = default);

    Task<UrlExpansion> ExpandUrlAsync(string url, CancellationToken cancellationToken = default);

    Task<DnsInspection> ResolveDnsAsync(string host, string[] types, CancellationToken cancellationToken = default);

    Task<EmailInspection> CheckEmailAsync(string email, CancellationToken cancellationToken = default);
}