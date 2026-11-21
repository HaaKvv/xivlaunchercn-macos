using System.Diagnostics;
using System.Text;
using Serilog;
using XIVLauncher.Account.Cred;

namespace XIVLauncher.Account.Cred.Providers;

/// <summary>
///     macOS 平台凭据提供者，基于系统钥匙串（Keychain，通过 <c>/usr/bin/security</c> 命令访问）。
///     <para>语义与 Windows 版「凭据管理器」完全一致：钥匙串中保存一个随机生成的加密密钥，
///     实际的凭据密文仍由 <see cref="EncryptionHelper"/>（Argon2id + Aegis256）加密后存储在配置文件中。</para>
/// </summary>
public sealed class KeychainCred : ICredProvider
{
    private readonly CredData cred;
    private          EncryptionHelper? encryptionHelper;

    public KeychainCred(CredData cred)
    {
        this.cred = cred;
    }

    public string GetName() => "钥匙串（Keychain）";

    public string GetDescription() => "使用 macOS 钥匙串安全地存储加密密钥";

    public async Task ClearCache() =>
        encryptionHelper = null;

    public async Task<string?> Decrypt(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        encryptionHelper ??= GetEncryptionHelper();
        return encryptionHelper.DecryptString(text);
    }

    public async Task<string?> Encrypt(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        encryptionHelper ??= GetEncryptionHelper();
        return encryptionHelper.EncryptString(text);
    }

    public EncryptionHelper GetEncryptionHelper() =>
        new(Encoding.UTF8.GetBytes(GetPassword()), Convert.FromBase64String(cred.LoginSalt));

    /// <summary>
    ///     从钥匙串读取（或首次生成并写入）本账号的加密密钥。
    /// </summary>
    public string GetPassword()
    {
        var serviceName = $"{cred.PackageName}-{cred.Account}";
        var accountName = cred.Account;

        // security 命令要求 -s (service) 与 -a (account) 共同定位条目
        var (exitCode, password) = RunSecurity("find-generic-password", "-s", serviceName, "-a", accountName, "-w");
        if (exitCode == 0 && !string.IsNullOrEmpty(password))
            return password;

        // 未找到或读取失败，删除可能的残留条目并重新生成
        RunSecurity("delete-generic-password", "-s", serviceName, "-a", accountName);

        var newPassword = EncryptionHelper.GetRandomHexString(128);
        var (addExit, addStderr) = RunSecurity(
            "add-generic-password",
            "-s", serviceName,
            "-a", accountName,
            "-w", newPassword,
            "-U");

        if (addExit != 0)
        {
            Log.Error("[KeychainCred] 写入钥匙串失败: {Stderr}", addStderr.Trim());
            throw new InvalidOperationException($"无法写入 macOS 钥匙串: {addStderr.Trim()}");
        }

        return newPassword;
    }

    public async Task<bool> IsSupported() =>
        true;

    public async Task Unregister() =>
        RunSecurity("delete-generic-password", "-s", $"{cred.PackageName}-{cred.Account}", "-a", cred.Account);

    /// <summary>
    ///     以参数列表方式执行 security 命令（避免字符串拼接带来的引号/特殊字符问题）。
    /// </summary>
    private static (int ExitCode, string Output) RunSecurity(string subcommand, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName               = "/usr/bin/security",
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true
            };

            psi.ArgumentList.Add(subcommand);
            foreach (var arg in args)
                psi.ArgumentList.Add(arg);

            using var proc = Process.Start(psi);
            if (proc == null)
                return (-1, string.Empty);

            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(10000);

            return (proc.ExitCode, (stdout + stderr).Trim());
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[KeychainCred] security 命令执行失败");
            return (-1, ex.Message);
        }
    }
}
