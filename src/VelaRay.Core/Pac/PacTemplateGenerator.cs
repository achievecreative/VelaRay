using System.Globalization;
using System.Net;
using System.Text.Json;

namespace VelaRay.Core.Pac;

/// <summary>
/// Portable PAC rule generator. It deliberately matches HOSTS only (not HTTPS URL paths),
/// so the same rules work with Edge/Chrome and WinINET PAC implementations.
/// All user-provided values are normalized and serialized as JavaScript string literals.
/// </summary>
public static class PacTemplateGenerator
{
    public const string ProxyByDefault = "ProxyByDefault";
    public const string DirectByDefault = "DirectByDefault";
    public const string ChinaSitesDirect = "ChinaSitesDirect";
    public static readonly string[] Templates = [ProxyByDefault, DirectByDefault, ChinaSitesDirect];

    // This is a small OFFLINE example, not a comprehensive/automatically updated China IP/domain list.
    private static readonly string[] ChinaSites =
    [
        "baidu.com", "qq.com", "weixin.qq.com", "taobao.com", "tmall.com",
        "jd.com", "bilibili.com", "zhihu.com", "163.com", "sina.com.cn",
        "alipay.com", "douyin.com"
    ];

    public static IReadOnlyList<string> ParseWebsites(string? lines)
    {
        var output = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Process comments before splitting on ',' and ';'. A comment may contain
        // separators and URLs without accidentally becoming another website rule.
        foreach (var line in (lines ?? "").Split(['\r', '\n'], StringSplitOptions.None))
        {
            var content = StripWebsiteComment(line).Trim();
            foreach (var entry in content.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var raw = entry.Trim();
                if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    if (!Uri.TryCreate(raw, UriKind.Absolute, out var url) ||
                        (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps) ||
                        string.IsNullOrWhiteSpace(url.Host))
                        throw new ArgumentException("无效网站地址: " + entry);
                    raw = url.Host;
                }
                else if (raw.Contains('/') || raw.Contains('\\') || raw.Contains('@') || raw.Contains(':') || raw.Contains('?'))
                    throw new ArgumentException("只支持域名、*.域名、HTTP(S) URL 或 IP，不支持路径匹配: " + entry);

                if (raw.StartsWith("*.", StringComparison.Ordinal)) raw = raw[2..];
                if (raw.StartsWith('.')) raw = raw[1..];
                raw = raw.TrimEnd('.').ToLowerInvariant();
                if (IPAddress.TryParse(raw.Trim('[', ']'), out var ip))
                {
                    raw = ip.ToString().ToLowerInvariant();
                }
                else
                {
                    try { raw = new IdnMapping().GetAscii(raw).ToLowerInvariant(); }
                    catch (ArgumentException) { throw new ArgumentException("域名格式不正确: " + entry); }
                    if (raw.Length is < 1 or > 253 || raw.Split('.').Any(part =>
                        part.Length is < 1 or > 63 || part[0] == '-' || part[^1] == '-' ||
                        part.Any(ch => !(ch is >= 'a' and <= 'z' or >= '0' and <= '9' or '-'))))
                        throw new ArgumentException("域名格式不正确: " + entry);
                }
                if (seen.Add(raw)) output.Add(raw);
            }
        }
        if (output.Count > 2000) throw new ArgumentException("PAC 网站规则不能超过 2000 条。");
        return output;
    }

    /// <summary>
    /// Lines beginning with // (allowing indentation) are comments. Also accepts an
    /// inline comment preceded by whitespace, e.g. "example.org // work".
    /// Do not treat the // in an HTTP(S) URL as a comment delimiter.
    /// The older # comment syntax is retained for backward compatibility.
    /// </summary>
    private static string StripWebsiteComment(string line)
    {
        var leading = line.TrimStart();
        if (leading.StartsWith("//", StringComparison.Ordinal) ||
            leading.StartsWith("#", StringComparison.Ordinal))
            return "";

        for (var index = 1; index + 1 < line.Length; index++)
        {
            if (line[index] == '/' && line[index + 1] == '/' &&
                char.IsWhiteSpace(line[index - 1]))
                return line[..index];
        }
        return line;
    }

    /// <summary>Websites explicitly configured in the managed PAC proxy list.
    /// Excludes hosts already covered by explicit PAC direct rules (which take priority).
    /// This never attempts to interpret arbitrary custom JavaScript PAC files.</summary>
    public static IReadOnlyList<string> EffectiveProxyWebsites(string? directWebsites, string? proxyWebsites)
    {
        var direct = ParseWebsites(directWebsites);
        return ParseWebsites(proxyWebsites)
            .Where(proxy => !direct.Any(rule => proxy.Equals(rule, StringComparison.OrdinalIgnoreCase) ||
                proxy.EndsWith("." + rule, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    public static string Generate(string template, string? directWebsites, string? proxyWebsites, int httpPort)
    {
        if (!Templates.Contains(template, StringComparer.Ordinal))
            throw new ArgumentException("未知 PAC 模板: " + template);
        if (httpPort is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(httpPort));
        var direct = ParseWebsites(directWebsites).ToList();
        var proxy = ParseWebsites(proxyWebsites).ToList();
        // Explicit proxy rules may override the optional offline China-sites template,
        // but explicit direct rules always take precedence over explicit proxy rules.
        var templateDirectJs = JsonSerializer.Serialize(template == ChinaSitesDirect ? ChinaSites : Array.Empty<string>());
        var directJs = JsonSerializer.Serialize(direct);
        var proxyJs = JsonSerializer.Serialize(proxy);
        var defaultRoute = template == DirectByDefault ? "DIRECT" : $"PROXY 127.0.0.1:{httpPort}";
        // Custom DIRECT precedes custom PROXY. Always protect loopback first to prevent PAC recursion.
        return $$"""
            // Generated by VelaRay. Edit the website lists in the app, not this generated file.
            // Matching is case insensitive: google.com matches google.com,
            // www.google.com and any deeper subdomain, but not notgoogle.com
            // or google.com.evil.com. Domain-boundary suffix check is intentional.
            // Priority: loopback > explicit DIRECT > explicit PROXY > template DIRECT > private/local > default.
            var velaRayDirectSites = {{directJs}};
            var velaRayProxySites = {{proxyJs}};
            var velaRayTemplateDirectSites = {{templateDirectJs}};
            function velarayMatches(host, domains) {
              for (var i = 0; i < domains.length; i++) {
                var name = domains[i];
                if (host === name || (host.length > name.length &&
                    host.substring(host.length - name.length - 1) === '.' + name)) return true;
              }
              return false;
            }
            function FindProxyForURL(url, host) {
              host = (host || '').toLowerCase().replace(/\.$/, '');
              if (host === 'localhost' || host === '127.0.0.1' || host === '::1' ||
                  shExpMatch(host, '127.*')) return 'DIRECT';
              if (velarayMatches(host, velaRayDirectSites)) return 'DIRECT';
              if (velarayMatches(host, velaRayProxySites)) return 'PROXY 127.0.0.1:{{httpPort}}';
              if (velarayMatches(host, velaRayTemplateDirectSites)) return 'DIRECT';
              if (isPlainHostName(host) || shExpMatch(host, '*.local') ||
                  shExpMatch(host, '10.*') || shExpMatch(host, '192.168.*') ||
                  shExpMatch(host, '169.254.*') ||
                  shExpMatch(host, '172.16.*') || shExpMatch(host, '172.17.*') ||
                  shExpMatch(host, '172.18.*') || shExpMatch(host, '172.19.*') ||
                  shExpMatch(host, '172.2?.*') || shExpMatch(host, '172.30.*') ||
                  shExpMatch(host, '172.31.*')) return 'DIRECT';
              return '{{defaultRoute}}';
            }
            """;
    }
}
