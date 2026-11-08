using System.Text.Json;
using PCSuite.Contracts;
using PCSuite.Core.Operations;
using PCSuite.Core.Web;
using PCSuite.Infrastructure.Web;

namespace PCSuite.Infrastructure.Operations;

/// <summary>
/// Operaciones web.* del puente (Task 040). Traducen las excepciones del
/// servicio a errores controlados y mapean dominio → DTO camelCase.
/// </summary>
public abstract class WebOperation : IOperation
{
    protected WebOperation(IWebInspectorService service) =>
        Service = service ?? throw new ArgumentNullException(nameof(service));

    protected IWebInspectorService Service { get; }

    public abstract string Id { get; }

    public virtual bool RequiresElevation => false;

    public async Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken cancellationToken)
    {
        try
        {
            var data = await ExecuteCoreAsync(context, cancellationToken).ConfigureAwait(false);
            return OperationResult.Ok(data);
        }
        catch (Exception ex)
        {
            return OperationResult.Fail("error", ex.Message);
        }
    }

    protected abstract Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken cancellationToken);

    protected static string GetString(OperationContext context, string key) => WebPayloadHelpers.GetString(context, key);

    protected static string[] GetStringArray(OperationContext context, string key) => WebPayloadHelpers.GetStringArray(context, key);

    protected static JsonElement? ToInspection(UrlInspection r) =>
        BridgeJson.ToJson(new UrlInspectionResult
        {
            Host = r.Host,
            Scripts = r.Scripts.ToList(),
            Suspicious = r.Suspicious.Select(s => new SuspiciousCharResult { Char = s.Char, Script = s.Script }).ToList(),
            Punycode = r.Punycode,
            MixedScripts = r.MixedScripts,
            Verdict = r.Verdict,
            TyposquatCandidates = r.TyposquatCandidates.ToList(),
        });

    protected static JsonElement? ToClean(UrlCleanResult r) =>
        BridgeJson.ToJson(new UrlCleanResultDto { OriginalUrl = r.OriginalUrl, CleanedUrl = r.CleanedUrl, Removed = r.Removed.ToList() });

    protected static JsonElement? ToCompare(UrlCompareResult r) =>
        BridgeJson.ToJson(new UrlCompareResultDto { UrlA = r.UrlA, UrlB = r.UrlB, Differences = r.Differences.ToList() });

    protected static JsonElement? ToTls(TlsInspection r) =>
        BridgeJson.ToJson(new TlsInspectionResult
        {
            Host = r.Host,
            Issuer = r.Issuer,
            Subject = r.Subject,
            ValidFrom = r.ValidFrom?.ToString("o"),
            ValidTo = r.ValidTo?.ToString("o"),
            SubjectAlternativeNames = r.SubjectAlternativeNames.ToList(),
            Expired = r.Expired,
            ExpiresSoon = r.ExpiresSoon,
            Verdict = r.Verdict,
            Detail = r.Detail,
        });

    protected static JsonElement? ToHeaders(HttpHeadersInspection r) =>
        BridgeJson.ToJson(new HttpHeadersInspectionResult
        {
            Url = r.Url,
            Hsts = r.Hsts,
            ContentSecurityPolicy = r.ContentSecurityPolicy,
            XFrameOptions = r.XFrameOptions,
            XContentTypeOptions = r.XContentTypeOptions,
            Cookies = r.Cookies.ToList(),
            StatusCode = r.StatusCode,
            Verdict = r.Verdict,
            Detail = r.Detail,
        });

    protected static JsonElement? ToExpansion(UrlExpansion r) =>
        BridgeJson.ToJson(new UrlExpansionResult
        {
            OriginalUrl = r.OriginalUrl,
            FinalUrl = r.FinalUrl,
            Chain = r.Chain.ToList(),
            Shortened = r.Shortened,
            Detail = r.Detail,
        });

    protected static JsonElement? ToDns(DnsInspection r) =>
        BridgeJson.ToJson(new DnsInspectionResult
        {
            Host = r.Host,
            Records = r.Records.Select(x => new DnsRecordResult { Type = x.Type, Value = x.Value, Ttl = x.Ttl }).ToList(),
            Detail = r.Detail,
        });

    protected static JsonElement? ToEmail(EmailInspection r) =>
        BridgeJson.ToJson(new EmailInspectionResult
        {
            Email = r.Email,
            FormatValid = r.FormatValid,
            Domain = r.Domain,
            MxRecords = r.MxRecords.ToList(),
            Spf = r.Spf,
            Dmarc = r.Dmarc,
            DkimSelector = r.DkimSelector,
            Verdict = r.Verdict,
            Detail = r.Detail,
        });
}

public sealed class WebInspectUrlOperation : WebOperation
{
    public WebInspectUrlOperation(IWebInspectorService service) : base(service) { }
    public override string Id => OperationIds.WebInspectUrl;
    protected override async Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken ct)
    {
        var url = GetString(context, "url");
        return ToInspection(await Service.InspectUrlAsync(url, ct).ConfigureAwait(false));
    }
}

public sealed class WebCleanTrackingOperation : WebOperation
{
    public WebCleanTrackingOperation(IWebInspectorService service) : base(service) { }
    public override string Id => OperationIds.WebCleanTracking;
    protected override Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken ct)
    {
        var url = GetString(context, "url");
        return Task.FromResult(ToClean(Service.CleanTracking(url)));
    }
}

public sealed class WebCompareUrlsOperation : WebOperation
{
    public WebCompareUrlsOperation(IWebInspectorService service) : base(service) { }
    public override string Id => OperationIds.WebCompareUrls;
    protected override Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken ct)
    {
        var a = GetString(context, "urlA");
        var b = GetString(context, "urlB");
        return Task.FromResult(ToCompare(Service.CompareUrls(a, b)));
    }
}

public sealed class WebCheckTlsOperation : WebOperation
{
    public WebCheckTlsOperation(IWebInspectorService service) : base(service) { }
    public override string Id => OperationIds.WebCheckTls;
    protected override async Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken ct)
    {
        var host = GetString(context, "host");
        return ToTls(await Service.CheckTlsAsync(host, ct).ConfigureAwait(false));
    }
}

public sealed class WebCheckHeadersOperation : WebOperation
{
    public WebCheckHeadersOperation(IWebInspectorService service) : base(service) { }
    public override string Id => OperationIds.WebCheckHeaders;
    protected override async Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken ct)
    {
        var url = GetString(context, "url");
        return ToHeaders(await Service.CheckHeadersAsync(url, ct).ConfigureAwait(false));
    }
}

public sealed class WebExpandUrlOperation : WebOperation
{
    public WebExpandUrlOperation(IWebInspectorService service) : base(service) { }
    public override string Id => OperationIds.WebExpandUrl;
    protected override async Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken ct)
    {
        var url = GetString(context, "url");
        return ToExpansion(await Service.ExpandUrlAsync(url, ct).ConfigureAwait(false));
    }
}

public sealed class WebResolveDnsOperation : WebOperation
{
    public WebResolveDnsOperation(IWebInspectorService service) : base(service) { }
    public override string Id => OperationIds.WebResolveDns;
    protected override async Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken ct)
    {
        var host = GetString(context, "host");
        var types = GetStringArray(context, "types");
        return ToDns(await Service.ResolveDnsAsync(host, types, ct).ConfigureAwait(false));
    }
}

public sealed class WebCheckEmailOperation : WebOperation
{
    public WebCheckEmailOperation(IWebInspectorService service) : base(service) { }
    public override string Id => OperationIds.WebCheckEmail;
    protected override async Task<JsonElement?> ExecuteCoreAsync(OperationContext context, CancellationToken ct)
    {
        var email = GetString(context, "email");
        return ToEmail(await Service.CheckEmailAsync(email, ct).ConfigureAwait(false));
    }
}

internal static class WebPayloadHelpers
{
    public static string GetString(OperationContext context, string key)
    {
        if (context.Payload.HasValue && context.Payload.Value.TryGetProperty(key, out var p))
            return p.GetString() ?? string.Empty;
        return string.Empty;
    }

    public static string[] GetStringArray(OperationContext context, string key)
    {
        if (context.Payload.HasValue && context.Payload.Value.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.Array)
        {
            return p.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToArray();
        }
        return Array.Empty<string>();
    }
}
// expose helpers via WebOperation base? We'll add protected methods instead.
