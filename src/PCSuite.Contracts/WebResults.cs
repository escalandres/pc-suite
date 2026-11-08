namespace PCSuite.Contracts;

/// <summary>DTOs de las operaciones web.* (Task 040).</summary>

public sealed class SuspiciousCharResult
{
    public string Char { get; init; } = string.Empty;
    public string Script { get; init; } = string.Empty;
}

public sealed class UrlInspectionResult
{
    public string Host { get; init; } = string.Empty;
    public List<string> Scripts { get; init; } = [];
    public List<SuspiciousCharResult> Suspicious { get; init; } = [];
    public bool Punycode { get; init; }
    public bool MixedScripts { get; init; }
    public string Verdict { get; init; } = string.Empty;
    public List<string> TyposquatCandidates { get; init; } = [];
}

public sealed class UrlCleanResultDto
{
    public string OriginalUrl { get; init; } = string.Empty;
    public string CleanedUrl { get; init; } = string.Empty;
    public List<string> Removed { get; init; } = [];
}

public sealed class UrlCompareResultDto
{
    public string UrlA { get; init; } = string.Empty;
    public string UrlB { get; init; } = string.Empty;
    public List<string> Differences { get; init; } = [];
}

public sealed class TlsInspectionResult
{
    public string Host { get; init; } = string.Empty;
    public string? Issuer { get; init; }
    public string? Subject { get; init; }
    public string? ValidFrom { get; init; }
    public string? ValidTo { get; init; }
    public List<string> SubjectAlternativeNames { get; init; } = [];
    public bool Expired { get; init; }
    public bool ExpiresSoon { get; init; }
    public string Verdict { get; init; } = string.Empty;
    public string? Detail { get; init; }
}

public sealed class HttpHeadersInspectionResult
{
    public string Url { get; init; } = string.Empty;
    public string? Hsts { get; init; }
    public string? ContentSecurityPolicy { get; init; }
    public string? XFrameOptions { get; init; }
    public string? XContentTypeOptions { get; init; }
    public List<string> Cookies { get; init; } = [];
    public int StatusCode { get; init; }
    public string Verdict { get; init; } = string.Empty;
    public string? Detail { get; init; }
}

public sealed class UrlExpansionResult
{
    public string OriginalUrl { get; init; } = string.Empty;
    public string FinalUrl { get; init; } = string.Empty;
    public List<string> Chain { get; init; } = [];
    public bool Shortened { get; init; }
    public string? Detail { get; init; }
}

public sealed class DnsRecordResult
{
    public string Type { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public int Ttl { get; init; }
}

public sealed class DnsInspectionResult
{
    public string Host { get; init; } = string.Empty;
    public List<DnsRecordResult> Records { get; init; } = [];
    public string? Detail { get; init; }
}

public sealed class EmailInspectionResult
{
    public string Email { get; init; } = string.Empty;
    public bool FormatValid { get; init; }
    public string? Domain { get; init; }
    public List<string> MxRecords { get; init; } = [];
    public string? Spf { get; init; }
    public string? Dmarc { get; init; }
    public string? DkimSelector { get; init; }
    public string Verdict { get; init; } = string.Empty;
    public string? Detail { get; init; }
}