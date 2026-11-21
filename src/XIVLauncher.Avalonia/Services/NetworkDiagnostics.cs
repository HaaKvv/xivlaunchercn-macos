using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Serilog;

namespace XIVLauncher.Services;

/// <summary>
///     网络诊断与登录连通性工具。
///     <para>针对国服登录域名（cas.sdo.com）在中国大陆部分网络被 DNS 劫持
///     （解析到 eo.dnse0.com 等劫持域、返回 HTTP 567 拦截页）的问题，
///     提供：劫持检测、DoH 真实 IP 解析、hosts 修复脚本生成。</para>
/// </summary>
public static class NetworkDiagnostics
{
    /// <summary>需要检测劫持的登录/服务域名</summary>
    private static readonly string[] CriticalDomains =
    [
        "cas.sdo.com",
        "n1.cas.sdo.com",
        "ff.dorado.sdo.com",
        "v3launcher.jijiagames.com"
    ];

    /// <summary>已知的劫持/污染特征</summary>
    private static readonly string[] HijackSignatures =
    [
        ".eo.dnse0.com",      // 中国移动广告/劫持域
        ".dnsv1.com",         // 腾讯云污染特征（部分场景）
        ".cachecn.com"
    ];

    public sealed record DiagnosisResult
    (
        string            Domain,
        string            LocalIp,
        string?           LocalHostName,
        string?           DoHResolution,
        int               HttpStatus,
        bool              IsHijacked,
        string            Detail
    );

    public sealed record DiagnosisReport
    (
        bool              LoginBlocked,
        List<DiagnosisResult> Results,
        string            Summary
    );

    /// <summary>
    ///     检测登录相关域名是否被劫持。
    /// </summary>
    public static async Task<DiagnosisReport> DiagnoseAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<DiagnosisResult>();

        foreach (var domain in CriticalDomains)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var local = ResolveLocal(domain);
            var dohIp = await ResolveViaDohAsync(domain, cancellationToken).ConfigureAwait(false);
            var status = await ProbeHttpStatusAsync(domain, cancellationToken).ConfigureAwait(false);

            var hijacked = IsHijacked(local.Ip, local.HostName, dohIp, status);
            results.Add(new DiagnosisResult(domain, local.Ip, local.HostName, dohIp, status, hijacked, Describe(domain, local.Ip, local.HostName, dohIp, status, hijacked)));
        }

        var blocked = results.Any(r => r.IsHijacked);
        var summary = blocked
                          ? "检测到登录服务器域名被网络劫持（DNS 污染 / HTTP 拦截）。请使用下方修复脚本写入 hosts，或切换网络后重试登录。"
                          : "登录相关域名连接正常，可尝试登录。";

        return new DiagnosisReport(blocked, results, summary);
    }

    /// <summary>
    ///     生成本机 hosts 修复脚本（将劫持域名指向 DoH 解析出的真实 IP）。
    /// </summary>
    public static async Task<string> BuildHostsFixScriptAsync(CancellationToken cancellationToken = default)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("#!/bin/bash");
        sb.AppendLine("# XIVLauncherCN 登录域名修复脚本 (macOS)");
        sb.AppendLine("# 将劫持的登录域名指向真实 IP，绕过运营商 DNS 污染");
        sb.AppendLine("# 需要 sudo 权限执行:  sudo bash fix-hosts.sh");
        sb.AppendLine();
        sb.AppendLine("set -e");
        sb.AppendLine("HOSTS=/etc/hosts");
        sb.AppendLine("MARK_START=\"# >>> XIVLauncherCN fix >>>\"");
        sb.AppendLine("MARK_END=\"# <<< XIVLauncherCN fix <<<\"");
        sb.AppendLine();
        sb.AppendLine("# 移除旧条目");
        sb.AppendLine("sed -i '' '/# >>> XIVLauncherCN fix >>>/,/# <<< XIVLauncherCN fix <<</d' $HOSTS");
        sb.AppendLine("echo \"$MARK_START\" >> $HOSTS");

        foreach (var domain in CriticalDomains)
        {
            var ip = await ResolveViaDohAsync(domain, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(ip) || !IPAddress.TryParse(ip, out _))
                continue;

            sb.AppendLine($"echo '{ip} {domain}' >> $HOSTS");
            sb.AppendLine($"echo '{ip} n1.cas.sdo.com' >> $HOSTS");
        }

        sb.AppendLine("echo \"$MARK_END\" >> $HOSTS");
        sb.AppendLine("dscacheutil -flushcache; killall -HUP mDNSResponder 2>/dev/null || true");
        sb.AppendLine("echo \"✅ hosts 修复完成，请重启 XIVLauncherCN 后重试登录\"");
        return sb.ToString();
    }

    /// <summary>
    ///     综合判断是否被劫持。三重证据：
    ///     1. 本地解析的 CNAME 链落在已知劫持域（eo.dnse0.com 等）
    ///     2. HTTP 状态码 567（运营商拦截页）
    ///     3. DoH 解析出的真实 IP 与本地解析不一致且本地 IP 落在劫持特征 IP 段
    /// </summary>
    private static bool IsHijacked(string localIp, string? localHostName, string? dohIp, int httpStatus)
    {
        // 1. CNAME 链命中劫持域名
        if (!string.IsNullOrEmpty(localHostName) &&
            HijackSignatures.Any(sig => localHostName.Contains(sig, StringComparison.OrdinalIgnoreCase)))
            return true;

        // 2. HTTP 567 拦截页
        if (httpStatus == 567)
            return true;

        // 3. DoH IP 与本地 IP 不一致（本地被污染到劫持 IP 段）
        if (!string.IsNullOrEmpty(dohIp) &&
            IPAddress.TryParse(localIp, out var localAddr) &&
            IPAddress.TryParse(dohIp, out var dohAddr) &&
            !localAddr.Equals(dohAddr) &&
            IsSuspiciousIp(localAddr))
            return true;

        return false;
    }

    /// <summary>
    ///     中国移动劫持/污染常见的 IP 段特征（cn168/eo.dnse0 相关出口）。
    ///     命中即高概率为运营商污染。
    /// </summary>
    private static bool IsSuspiciousIp(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        if (bytes.Length != 4)
            return false;

        var octets = (bytes[0], bytes[1], bytes[2]);
        return octets switch
        {
            // 116.140.x.x / 42.177.x.x / 43.159.x.x 等移动劫持段
            (116, 140, _) => true,
            (42, 177, _) => true,
            (43, 159, _) => true,
            _ => false
        };
    }

    private static string Describe(string domain, string localIp, string? localHostName, string? dohIp, int httpStatus, bool hijacked)
    {
        if (hijacked)
            return $"⚠️ {domain} 疑似被劫持: 本地 {localIp}{(string.IsNullOrEmpty(localHostName) ? "" : $" ({localHostName})")}, HTTP {httpStatus}" +
                   (string.IsNullOrEmpty(dohIp) ? "" : $", DoH 真实 IP {dohIp}");

        return $"✅ {domain} 正常 (本地 {localIp}, HTTP {httpStatus})";
    }

    /// <summary>
    ///     本地解析结果：同时保留 A 记录 IP 与 CNAME 主机名（用于劫持特征匹配）。
    /// </summary>
    private static (string Ip, string? HostName) ResolveLocal(string domain)
    {
        try
        {
            var entry = Dns.GetHostEntry(domain);
            var ip = entry.AddressList.Length > 0
                         ? string.Join(", ", entry.AddressList.Take(2).Select(e => e.ToString()))
                         : "(无记录)";

            // HostName 可能是 CNAME 链末端（如 cas.sdo.com.eo.dnse0.com）
            return (ip, string.IsNullOrEmpty(entry.HostName) ? null : entry.HostName);
        }
        catch (Exception ex)
        {
            return ($"(解析失败: {ex.Message})", null);
        }
    }

    /// <summary>
    ///     通过 DNS-over-HTTPS（Cloudflare/阿里）解析真实 IP，绕过本地 DNS 污染。
    /// </summary>
    private static async Task<string?> ResolveViaDohAsync(string domain, CancellationToken cancellationToken)
    {
        // 优先阿里 DoH（国内可达），回退 Cloudflare
        string[] dohUrls =
        [
            $"https://223.5.5.5/resolve?name={domain}&type=A",
            $"https://cloudflare-dns.com/dns-query?name={domain}&type=A"
        ];

        foreach (var url in dohUrls)
        {
            try
            {
                using var handler = new HttpClientHandler();
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };

                if (url.Contains("cloudflare", StringComparison.OrdinalIgnoreCase))
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Add("Accept", "application/dns-json");
                    using var resp = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode)
                        continue;

                    var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken).ConfigureAwait(false);
                    if (doc == null)
                        continue;

                    foreach (var answer in doc.RootElement.GetProperty("Answer").EnumerateArray())
                    {
                        if (answer.GetProperty("type").GetInt32() == 1)
                            return answer.GetProperty("data").GetString();
                    }
                }
                else
                {
                    var text = await client.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(text);
                    foreach (var answer in doc.RootElement.GetProperty("Answer").EnumerateArray())
                    {
                        if (answer.GetProperty("type").GetInt32() == 1)
                            return answer.GetProperty("data").GetString();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[NetworkDiagnostics] DoH 解析 {Domain} 失败 ({Url})", domain, url);
            }
        }

        return null;
    }

    private static async Task<int> ProbeHttpStatusAsync(string domain, CancellationToken cancellationToken)
    {
        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };

            var url = domain.StartsWith("cas.", StringComparison.Ordinal)
                          ? $"https://{domain}/authen/getGuid.json?generateDynamicKey=1"
                          : $"https://{domain}/";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0");

            using var resp = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            return (int)resp.StatusCode;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[NetworkDiagnostics] HTTP 探测 {Domain} 失败", domain);
            return 0;
        }
    }
}
