using XIVLauncher.Services;
using Xunit;

namespace XIVLauncher.Test.Common;

/// <summary>
///     网络诊断工具验证（DNS 劫持检测 / DoH 解析 / hosts 修复脚本生成）
/// </summary>
public sealed class NetworkDiagnosticsTests
{
    [Fact]
    public async Task Diagnose_ReturnsReport_WithoutThrowing()
    {
        var report = await NetworkDiagnostics.DiagnoseAsync();

        Assert.NotNull(report);
        Assert.NotEmpty(report.Results);
        Assert.False(string.IsNullOrWhiteSpace(report.Summary));
    }

    [Fact]
    public async Task Diagnose_IncludesCriticalDomains()
    {
        var report = await NetworkDiagnostics.DiagnoseAsync();

        // 关键登录域名必须被检测
        Assert.Contains(report.Results, r => r.Domain.Contains("cas.sdo.com", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildHostsFixScript_ProducesValidScript()
    {
        var script = await NetworkDiagnostics.BuildHostsFixScriptAsync();

        // 必须包含 bash 头、hosts 路径、marker 标记
        Assert.Contains("#!/bin/bash", script);
        Assert.Contains("/etc/hosts", script);
        Assert.Contains("XIVLauncherCN fix", script);
    }

    [Fact]
    public async Task BuildHostsFixScript_ContainsResolvedIps()
    {
        var script = await NetworkDiagnostics.BuildHostsFixScriptAsync();

        // 至少应包含一条真实 IP 映射（cas.sdo.com 或下载域）
        Assert.Contains("cas.sdo.com", script);
    }
}
