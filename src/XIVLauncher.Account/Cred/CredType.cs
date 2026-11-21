namespace XIVLauncher.Account.Cred;

public enum CredType
{
    NoEncryption,
#if XL_MAC
    Keychain,
#else
    WindowsCredManager,
    WindowsHello,
#endif
}

public static class CredTypeExtension
{
    extension(CredType type)
    {
        public string GetDisplayName() =>
            type switch
            {
                CredType.NoEncryption       => "无加密",
#if XL_MAC
                CredType.Keychain           => "钥匙串（Keychain）",
#else
                CredType.WindowsCredManager => "系统凭据管理器",
                CredType.WindowsHello       => "Windows Hello",
#endif
                _                           => type.ToString()
            };
    }
}
