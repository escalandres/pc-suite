using System.Net;
using System.Net.Mail;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using PCSuite.Contracts;
using PCSuite.Core.Web;

namespace PCSuite.Infrastructure.Web;

/// <summary>
/// Implementación de <see cref="IWebInspectorService"/> (Task 040).
/// Las comprobaciones locales (homógrafos, punycode, typosquatting, tracking,
/// comparación) no tocan la red. Las de red (TLS, cabeceras, redirecciones,
/// DNS, correo) son explícitas y con timeout/cancelación.
/// </summary>
public sealed class WebInspectorService : IWebInspectorService
{
    private static readonly HttpClient Http = new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
    })
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    private static readonly string[] TrackingParams = new[]
    {
        "utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content",
        "fbclid", "gclid", "msclkid", "mc_eid", "igshid", "ref", "ref_src",
    };

    private static readonly string[] KnownBrands = new[]
    {
        "google", "facebook", "paypal", "amazon", "microsoft", "apple",
        "netflix", "instagram", "whatsapp", "twitter", "linkedin", "github",
        "dropbox", "spotify", "bank", "outlook", "office",
    };

    private static readonly string[] Shorteners = new[]
    {
        "bit.ly", "tinyurl.com", "t.co", "goo.gl", "ow.ly", "is.gd", "buff.ly",
    };

    public Task<UrlInspection> InspectUrlAsync(string url, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL vacía.", nameof(url));

        string host;
        try
        {
            var uri = new Uri(url.Trim());
            host = uri.Host;
        }
        catch
        {
            return Task.FromResult(new UrlInspection(
                string.Empty, Array.Empty<string>(), Array.Empty<SuspiciousChar>(),
                false, false, "invalid", Array.Empty<string>()));
        }

        // scriptOf replica la lógica del frontend.
        static string scriptOf(char ch)
        {
            int code = ch;
            if ((code >= 0x0041 && code <= 0x005A) || (code >= 0x0061 && code <= 0x007A) ||
                (code >= 0x00C0 && code <= 0x024F) || (code >= 0x1E00 && code <= 0x1EFF)) return "Latin";
            if (code >= 0x0400 && code <= 0x04FF) return "Cyrillic";
            if (code >= 0x0370 && code <= 0x03FF) return "Greek";
            if (code >= 0x0530 && code <= 0x058F) return "Armenian";
            if (code >= 0x0590 && code <= 0x05FF) return "Hebrew";
            if (code >= 0x0600 && code <= 0x06FF) return "Arabic";
            if (code >= 0xAC00 && code <= 0xD7AF) return "Hangul";
            if (code >= 0x4E00 && code <= 0x9FFF) return "CJK";
            if (code >= 0x0030 && code <= 0x0039) return "Digit";
            if (code <= 0x007F) return "ASCII";
            return "Other";
        }

        var chars = host.ToCharArray();
        var scriptCounts = new Dictionary<string, int>();
        foreach (var ch in chars)
        {
            var sc = scriptOf(ch);
            scriptCounts[sc] = scriptCounts.TryGetValue(sc, out var c) ? c + 1 : 1;
        }
        var scripts = scriptCounts.Keys.ToArray();
        var meaningful = scripts.Where(s => s != "ASCII" && s != "Digit").ToArray();
        var dominant = meaningful.Length > 0
            ? meaningful.OrderByDescending(s => scriptCounts[s]).First()
            : "ASCII";

        var suspicious = chars
            .Select(ch => new SuspiciousChar(ch.ToString(), scriptOf(ch)))
            .Where(c => c.Script != "ASCII" && c.Script != "Digit" && c.Script != dominant)
            .ToArray();

        var mixedScripts = meaningful.Length > 1;
        var punycode = host.StartsWith("xn--", StringComparison.OrdinalIgnoreCase) ||
                       host.Contains(".xn--", StringComparison.OrdinalIgnoreCase);

        // typosquatting
        var hostLabel = host.Split('.')[0].ToLowerInvariant();
        var typos = new List<string>();
        foreach (var brand in KnownBrands)
        {
            if (hostLabel == brand) continue;
            var d = Levenshtein(hostLabel, brand);
            if (d > 0 && d <= 2)
                typos.Add(brand);
        }

        string verdict = "safe";
        if (mixedScripts || punycode) verdict = "suspicious";
        if (typos.Count > 0) verdict = "suspicious";
        if (host.Length == 0) verdict = "invalid";

        return Task.FromResult(new UrlInspection(
            host, scripts, suspicious, punycode, mixedScripts, verdict, typos));
    }

    public UrlCleanResult CleanTracking(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return new UrlCleanResult(url ?? string.Empty, url ?? string.Empty, Array.Empty<string>());

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return new UrlCleanResult(url, url, Array.Empty<string>());

        var query = uri.Query.TrimStart('?');
        if (string.IsNullOrEmpty(query))
            return new UrlCleanResult(url, url, Array.Empty<string>());

        var pairs = query.Split('&', StringSplitOptions.RemoveEmptyEntries);
        var kept = new List<string>();
        var removed = new List<string>();
        foreach (var pair in pairs)
        {
            var key = pair.Split('=', 2)[0];
            if (TrackingParams.Contains(key, StringComparer.OrdinalIgnoreCase))
                removed.Add(key);
            else
                kept.Add(pair);
        }

        var builder = new UriBuilder(uri) { Query = string.Join("&", kept) };
        return new UrlCleanResult(url, builder.Uri.ToString(), removed);
    }

    public UrlCompareResult CompareUrls(string urlA, string urlB)
    {
        var diffs = new List<string>();
        if (!Uri.TryCreate(urlA?.Trim() ?? "", UriKind.Absolute, out var a) ||
            !Uri.TryCreate(urlB?.Trim() ?? "", UriKind.Absolute, out var b))
        {
            diffs.Add("URL inválida");
            return new UrlCompareResult(urlA ?? "", urlB ?? "", diffs);
        }

        if (!string.Equals(a.Scheme, b.Scheme, StringComparison.OrdinalIgnoreCase))
            diffs.Add($"Esquema: {a.Scheme} vs {b.Scheme}");
        if (!string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase))
            diffs.Add($"Host: {a.Host} vs {b.Host}");
        if (!string.Equals(a.AbsolutePath, b.AbsolutePath, StringComparison.Ordinal))
            diffs.Add($"Ruta: {a.AbsolutePath} vs {b.AbsolutePath}");
        if (!string.Equals(a.Query, b.Query, StringComparison.Ordinal))
            diffs.Add($"Query: {a.Query} vs {b.Query}");
        if (!string.Equals(a.Fragment, b.Fragment, StringComparison.Ordinal))
            diffs.Add($"Fragmento: {a.Fragment} vs {b.Fragment}");

        if (diffs.Count == 0) diffs.Add("Las URLs son idénticas.");
        return new UrlCompareResult(urlA, urlB, diffs);
    }

    public async Task<TlsInspection> CheckTlsAsync(string host, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host))
            return new TlsInspection(string.Empty, null, null, null, null, Array.Empty<string>(), false, false, "invalid", "Host vacío.");

        host = host.Trim();
        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(host, 443, cancellationToken).ConfigureAwait(false);
            using var ssl = new SslStream(tcp.GetStream(), false, (_, _, _, _) => true);
            var sslOptions = new SslClientAuthenticationOptions
            {
                TargetHost = host,
                RemoteCertificateValidationCallback = (_, _, _, _) => true,
            };
            await ssl.AuthenticateAsClientAsync(sslOptions, cancellationToken).ConfigureAwait(false);

            var cert = ssl.RemoteCertificate as X509Certificate2
                       ?? (ssl.RemoteCertificate != null ? new X509Certificate2(ssl.RemoteCertificate) : null);

            if (cert == null)
                return new TlsInspection(host, null, null, null, null, Array.Empty<string>(), false, false, "invalid", "Sin certificado.");

            var san = new List<string>();
            foreach (var ext in cert.Extensions)
            {
                if (ext is X509SubjectAlternativeNameExtension sa)
                {
                    foreach (var name in sa.EnumerateDnsNames()) san.Add(name);
                }
            }

            var now = DateTimeOffset.UtcNow;
            var expired = now > cert.NotAfter;
            var expiresSoon = !expired && (cert.NotAfter - now).TotalDays <= 30;
            string verdict = expired ? "suspicious" : (expiresSoon ? "suspicious" : "safe");
            if (cert.NotAfter < now) verdict = "suspicious";

            return new TlsInspection(
                host, cert.Issuer, cert.Subject,
                new DateTimeOffset(cert.NotBefore, TimeSpan.Zero),
                new DateTimeOffset(cert.NotAfter, TimeSpan.Zero),
                san, expired, expiresSoon, verdict, null);
        }
        catch (Exception ex)
        {
            return new TlsInspection(host, null, null, null, null, Array.Empty<string>(), false, false, "invalid", ex.Message);
        }
    }

    public async Task<HttpHeadersInspection> CheckHeadersAsync(string url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            return new HttpHeadersInspection(string.Empty, null, null, null, null, Array.Empty<string>(), 0, "invalid", "URL vacía.");

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url.Trim());
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            var hsts = resp.Headers.TryGetValues("Strict-Transport-Security", out var h) ? string.Join("; ", h) : null;
            var csp = resp.Headers.TryGetValues("Content-Security-Policy", out var c) ? string.Join("; ", c) : null;
            var xfo = resp.Headers.TryGetValues("X-Frame-Options", out var x) ? string.Join("; ", x) : null;
            var xcto = resp.Headers.TryGetValues("X-Content-Type-Options", out var t) ? string.Join("; ", t) : null;

            var cookies = new List<string>();
            if (resp.Headers.TryGetValues("Set-Cookie", out var sc))
                cookies.AddRange(sc);

            int score = 0;
            if (hsts != null) score++;
            if (csp != null) score++;
            if (xfo != null) score++;
            if (xcto != null) score++;

            string verdict = score >= 3 ? "safe" : (score >= 1 ? "suspicious" : "suspicious");
            return new HttpHeadersInspection(url, hsts, csp, xfo, xcto, cookies, (int)resp.StatusCode, verdict, null);
        }
        catch (Exception ex)
        {
            return new HttpHeadersInspection(url, null, null, null, null, Array.Empty<string>(), 0, "invalid", ex.Message);
        }
    }

    public async Task<UrlExpansion> ExpandUrlAsync(string url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            return new UrlExpansion(string.Empty, string.Empty, Array.Empty<string>(), false, "URL vacía.");

        var chain = new List<string> { url };
        var current = url.Trim();
        var shortened = Shorteners.Any(s => new Uri(current).Host.Contains(s, StringComparison.OrdinalIgnoreCase));

        try
        {
            for (int i = 0; i < 10; i++)
            {
                using var req = new HttpRequestMessage(HttpMethod.Head, current);
                using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if ((int)resp.StatusCode is >= 300 and < 400 && resp.Headers.Location != null)
                {
                    var next = resp.Headers.Location.IsAbsoluteUri
                        ? resp.Headers.Location.ToString()
                        : new Uri(new Uri(current), resp.Headers.Location).ToString();
                    chain.Add(next);
                    current = next;
                }
                else
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            return new UrlExpansion(url, current, chain, shortened, ex.Message);
        }

        return new UrlExpansion(url, current, chain, shortened || chain.Count > 1, null);
    }

    public async Task<DnsInspection> ResolveDnsAsync(string host, string[] types, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host))
            return new DnsInspection(string.Empty, Array.Empty<DnsRecord>(), "Host vacío.");

        host = host.Trim();
        var requested = (types == null || types.Length == 0)
            ? new[] { "A", "AAAA" }
            : types;

        var records = new List<DnsRecord>();
        foreach (var t in requested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var rr = await QueryDnsAsync(host, t.ToUpperInvariant(), cancellationToken).ConfigureAwait(false);
                records.AddRange(rr);
            }
            catch (Exception ex)
            {
                records.Add(new DnsRecord(t, $"error: {ex.Message}", 0));
            }
        }

        // Fallback A/AAAA con System.Net.Dns si la consulta UDP no devolvió nada.
        if (records.Count == 0 && (requested.Contains("A") || requested.Contains("AAAA")))
        {
            try
            {
                var entry = await Dns.GetHostEntryAsync(host, cancellationToken).ConfigureAwait(false);
                foreach (var ip in entry.AddressList)
                    records.Add(new DnsRecord(ip.AddressFamily == AddressFamily.InterNetwork ? "A" : "AAAA", ip.ToString(), 0));
            }
            catch { }
        }

        return new DnsInspection(host, records, null);
    }

    public async Task<EmailInspection> CheckEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            return new EmailInspection(string.Empty, false, null, Array.Empty<string>(), null, null, null, "invalid", "Correo vacío.");

        email = email.Trim();
        string? domain = null;
        bool formatValid;
        try
        {
            var addr = new MailAddress(email);
            formatValid = true;
            domain = addr.Host;
        }
        catch
        {
            formatValid = false;
        }

        if (!formatValid || string.IsNullOrEmpty(domain))
            return new EmailInspection(email, false, domain, Array.Empty<string>(), null, null, null, "invalid", "Formato inválido.");

        var mx = new List<string>();
        try
        {
            var r = await QueryDnsAsync(domain, "MX", cancellationToken).ConfigureAwait(false);
            mx.AddRange(r.Select(x => x.Value));
        }
        catch { }

        string? spf = null, dmarc = null, dkim = null;
        try
        {
            var txt = await QueryDnsAsync(domain, "TXT", cancellationToken).ConfigureAwait(false);
            spf = txt.Select(x => x.Value).FirstOrDefault(v => v.Contains("v=spf1", StringComparison.OrdinalIgnoreCase));
        }
        catch { }
        try
        {
            var txt = await QueryDnsAsync("_dmarc." + domain, "TXT", cancellationToken).ConfigureAwait(false);
            dmarc = txt.Select(x => x.Value).FirstOrDefault(v => v.Contains("v=DMARC1", StringComparison.OrdinalIgnoreCase));
        }
        catch { }
        try
        {
            var txt = await QueryDnsAsync("default._domainkey." + domain, "TXT", cancellationToken).ConfigureAwait(false);
            dkim = txt.Select(x => x.Value).FirstOrDefault();
        }
        catch { }

        string verdict = (mx.Count > 0 && spf != null) ? "safe" : "suspicious";
        return new EmailInspection(email, true, domain, mx, spf, dmarc, dkim, verdict, null);
    }

    // ---------- DNS query (UDP) ----------

    private static async Task<IReadOnlyList<DnsRecord>> QueryDnsAsync(string host, string type, CancellationToken ct)
    {
        var results = new List<DnsRecord>();
        ushort qtype = type switch
        {
            "A" => 1,
            "NS" => 2,
            "CNAME" => 5,
            "MX" => 15,
            "TXT" => 16,
            "AAAA" => 28,
            _ => 1,
        };

        var query = BuildDnsQuery(host, qtype);
        using var udp = new UdpClient();
        udp.Client.ReceiveTimeout = 5000;
        var endpoint = new IPEndPoint(IPAddress.Parse("8.8.8.8"), 53);
        await udp.SendAsync(query, query.Length, endpoint).ConfigureAwait(false);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        var received = await udp.ReceiveAsync(cts.Token).ConfigureAwait(false);
        var data = received.Buffer;

        // parse header
        if (data.Length < 12) return results;
        int ancount = (data[6] << 8) | data[7];
        int offset = 12;
        // skip question
        // read qname
        offset = SkipName(data, offset);
        offset += 4; // qtype + qclass

        for (int i = 0; i < ancount && offset < data.Length; i++)
        {
            offset = SkipName(data, offset);
            if (offset + 10 > data.Length) break;
            int rtype = (data[offset] << 8) | data[offset + 1];
            offset += 2; // type
            offset += 2; // class
            int ttl = (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
            offset += 4;
            int rdlen = (data[offset] << 8) | data[offset + 1];
            offset += 2;
            int rdataStart = offset;

            string rtypeName = rtype switch
            {
                1 => "A",
                2 => "NS",
                5 => "CNAME",
                15 => "MX",
                16 => "TXT",
                28 => "AAAA",
                _ => rtype.ToString(),
            };

            if (rtype == 1 && rdlen == 4)
            {
                results.Add(new DnsRecord("A", $"{data[offset]}.{data[offset + 1]}.{data[offset + 2]}.{data[offset + 3]}", ttl));
            }
            else if (rtype == 28 && rdlen == 16)
            {
                var ip = new IPAddress(data.AsSpan(offset, 16));
                results.Add(new DnsRecord("AAAA", ip.ToString(), ttl));
            }
            else if (rtype == 5 || rtype == 2)
            {
                int o = offset;
                var name = ReadName(data, ref o);
                results.Add(new DnsRecord(rtypeName, name, ttl));
            }
            else if (rtype == 15 && rdlen >= 3)
            {
                int pref = (data[offset] << 8) | data[offset + 1];
                int o = offset + 2;
                var name = ReadName(data, ref o);
                results.Add(new DnsRecord("MX", $"{pref} {name}", ttl));
            }
            else if (rtype == 16)
            {
                int o = offset;
                var sb = new StringBuilder();
                int end = offset + rdlen;
                while (o < end)
                {
                    int len = data[o++];
                    if (len == 0 || o + len > end) break;
                    sb.Append(Encoding.UTF8.GetString(data, o, len));
                    o += len;
                }
                results.Add(new DnsRecord("TXT", sb.ToString(), ttl));
            }

            offset = rdataStart + rdlen;
        }

        // Filter by requested type if not "ALL"
        if (type != "ALL")
            results = results.Where(r => r.Type == type).ToList();

        return results;
    }

    private static byte[] BuildDnsQuery(string host, ushort qtype)
    {
        using var ms = new MemoryStream();
        // header
        ms.WriteByte(0x12); ms.WriteByte(0x34); // id
        ms.WriteByte(0x01); ms.WriteByte(0x00); // flags
        ms.WriteByte(0x00); ms.WriteByte(0x01); // qdcount
        ms.WriteByte(0x00); ms.WriteByte(0x00);
        ms.WriteByte(0x00); ms.WriteByte(0x00);
        ms.WriteByte(0x00); ms.WriteByte(0x00);
        foreach (var label in host.TrimEnd('.').Split('.'))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            ms.WriteByte((byte)bytes.Length);
            ms.Write(bytes, 0, bytes.Length);
        }
        ms.WriteByte(0);
        // qtype
        ms.WriteByte((byte)(qtype >> 8));
        ms.WriteByte((byte)(qtype & 0xFF));
        // qclass IN
        ms.WriteByte(0); ms.WriteByte(1);
        return ms.ToArray();
    }

    private static int SkipName(byte[] data, int offset)
    {
        while (offset < data.Length)
        {
            int len = data[offset];
            if (len == 0) { offset++; break; }
            if ((len & 0xC0) == 0xC0) { offset += 2; break; }
            offset += 1 + len;
        }
        return offset;
    }

    private static string ReadName(byte[] data, ref int offset)
    {
        var sb = new StringBuilder();
        int jumped = 0;
        int pos = offset;
        bool first = true;
        while (true)
        {
            if (pos >= data.Length) break;
            int len = data[pos];
            if (len == 0) { pos++; break; }
            if ((len & 0xC0) == 0xC0)
            {
                int ptr = ((len & 0x3F) << 8) | data[pos + 1];
                if (jumped == 0) offset = pos + 2;
                pos = ptr;
                jumped++;
                if (jumped > 10) break;
                continue;
            }
            if (!first) sb.Append('.');
            first = false;
            sb.Append(Encoding.ASCII.GetString(data, pos + 1, len));
            pos += 1 + len;
        }
        if (jumped == 0) offset = pos;
        return sb.ToString();
    }

    private static int Levenshtein(string a, string b)
    {
        if (a == b) return 0;
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;
        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }
        return prev[b.Length];
    }
}