using System.Text;
using XIVLauncher.Account.Cred;
using XIVLauncher.Account.Cred.Providers;
using Xunit;

namespace XIVLauncher.Test.Account;

/// <summary>
///     凭据加密（Argon2id + Aegis256）与 macOS Keychain 提供者验证
/// </summary>
public sealed class CredTests
{
    private const string TEST_ACCOUNT_ID = "xltest-" + nameof(CredTests);

    [Fact]
    public void EncryptionHelper_IsSupported()
    {
        Assert.True(EncryptionHelper.IsSupported(), "NSec Aegis256 应可在 macOS 上初始化");
    }

    [Fact]
    public void Encrypt_Decrypt_RoundTrips()
    {
        using var temp = new Common.RepositoryTests.TempDir();
        var salt = Convert.ToBase64String(EncryptionHelper.GetRandomBytes(16));

        var helper = new EncryptionHelper(Encoding.UTF8.GetBytes("test-password"), Convert.FromBase64String(salt));
        const string PLAINTEXT = "sdo-password-秘密数据";

        var encrypted = helper.EncryptString(PLAINTEXT);
        Assert.NotNull(encrypted);
        Assert.NotEqual(PLAINTEXT, encrypted);

        var decrypted = helper.DecryptString(encrypted!);
        Assert.Equal(PLAINTEXT, decrypted);
    }

    [Fact]
    public void Encrypt_IsNonDeterministic_WithRandomNonce()
    {
        using var temp = new Common.RepositoryTests.TempDir();
        var salt = Convert.ToBase64String(EncryptionHelper.GetRandomBytes(16));

        var helper = new EncryptionHelper(Encoding.UTF8.GetBytes("test-password"), Convert.FromBase64String(salt));
        const string PLAINTEXT = "same-input";

        var e1 = helper.EncryptString(PLAINTEXT);
        var e2 = helper.EncryptString(PLAINTEXT);

        Assert.NotEqual(e1, e2);
    }

    [Fact]
    public void Encrypt_WrongPassword_FailsToDecrypt()
    {
        using var temp = new Common.RepositoryTests.TempDir();
        var salt = Convert.ToBase64String(EncryptionHelper.GetRandomBytes(16));

        var helper1 = new EncryptionHelper(Encoding.UTF8.GetBytes("correct"), Convert.FromBase64String(salt));
        var helper2 = new EncryptionHelper(Encoding.UTF8.GetBytes("wrong"),   Convert.FromBase64String(salt));

        var encrypted = helper1.EncryptString("secret");

        // 错误密钥解密应抛出（Aegis256 认证失败）或返回垃圾数据，但不应静默返回原文
        Assert.ThrowsAny<Exception>(() => helper2.DecryptString(encrypted!));
    }

    [Fact]
    public void Decrypt_NullOrEmpty_Contract()
    {
        using var temp = new Common.RepositoryTests.TempDir();
        var salt = Convert.ToBase64String(EncryptionHelper.GetRandomBytes(16));

        var helper = new EncryptionHelper(Encoding.UTF8.GetBytes("pwd"), Convert.FromBase64String(salt));

        // 契约：EncryptionHelper 要求非 null 输入（上层负责 null 防护）
        Assert.ThrowsAny<Exception>(() => helper.DecryptString(null!));
    }

    [Fact]
    public async Task KeychainCred_Decrypt_NullOrEmpty_ReturnsAsIs()
    {
        if (!IsKeychainAvailable())
            return;

        using var temp = new Common.RepositoryTests.TempDir();
        var credData = new CredData("XIVLauncherCN.Test", Path.Combine(temp.Dir.FullName, "cred.json"));

        var provider = new KeychainCred(credData);
        try
        {
            // 上层契约：null/空输入原样返回，不抛异常、不触发钥匙串访问
            Assert.Null(await provider.Decrypt(null));
            Assert.Equal(string.Empty, await provider.Decrypt(string.Empty));
        }
        finally
        {
            await provider.Unregister();
        }
    }

    [Fact]
    public void GetRandomHexString_ReturnsExpectedLength()
    {
        var hex = EncryptionHelper.GetRandomHexString(32);

        Assert.Equal(64, hex.Length); // 32 bytes = 64 hex chars
        Assert.Matches("^[0-9a-f]+$", hex);
    }

    [Fact]
    public async Task KeychainCred_EncryptDecrypt_RoundTrips_IfSupported()
    {
        if (!IsKeychainAvailable())
            return; // 无钥匙串环境（CI/无 GUI 会话）跳过

        using var temp = new Common.RepositoryTests.TempDir();
        var credPath = Path.Combine(temp.Dir.FullName, "cred.json");
        var credData = new CredData("XIVLauncherCN.Test", credPath);

        var provider = new KeychainCred(credData);
        Assert.True(await provider.IsSupported());

        try
        {
            const string SECRET = "keychain-secret-123";
            var encrypted = await provider.Encrypt(SECRET);
            Assert.NotNull(encrypted);

            var decrypted = await provider.Decrypt(encrypted);
            Assert.Equal(SECRET, decrypted);
        }
        finally
        {
            await provider.Unregister();
        }
    }

    [Fact]
    public async Task KeychainCred_Unregister_IsIdempotent()
    {
        if (!IsKeychainAvailable())
            return;

        using var temp = new Common.RepositoryTests.TempDir();
        var credData = new CredData("XIVLauncherCN.Test", Path.Combine(temp.Dir.FullName, "cred.json"));

        var provider = new KeychainCred(credData);
        await provider.Unregister();
        await provider.Unregister(); // 不应抛异常（条目不存在时忽略）

        Assert.True(true);
    }

    private static bool IsKeychainAvailable()
    {
        try
        {
            using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/usr/bin/security",
                Arguments = "dump-keychain -d 2>/dev/null",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            });
            proc?.WaitForExit(3000);
            return proc?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
