using System.Security.Cryptography;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Serilog;
using XIVLauncher.Account;
using XIVLauncher.Account.DeviceProfiles;
using XIVLauncher.Common.Constant;
using XIVLauncher.Common.Game;
using XIVLauncher.Settings;

namespace XIVLauncher.Views;

public partial class AccountWindow : Window
{
    private readonly AccountManager accounts;
    private readonly LauncherSettingsV3 settings;
    private readonly string[] areas;
    private XIVAccount? selected;
    private Bitmap? avatar;
    private bool changeAvatar;
    private bool busy;
    private bool refreshing;

    public AccountWindow(AccountManager accounts, LauncherSettingsV3 settings, IEnumerable<string> areas)
    {
        this.accounts = accounts;
        this.settings = settings;
        this.areas = areas.ToArray();
        InitializeComponent();
        AutoLoginBox.IsChecked = settings.AutoLoginToLastAccount;
        ProfileScope.SelectedIndex = 0;
        ReloadAccounts(accounts.CurrentAccount);
        Closing += (_, args) => args.Cancel = busy;
        Closed += (_, _) => SetAvatar(null);
    }

    private void ReloadAccounts(XIVAccount? selection)
    {
        refreshing = true;
        AccountList.ItemsSource = accounts.Accounts.ToArray();
        AccountList.SelectedItem = selection;
        refreshing = false;
        LoadAccount(selection);
    }

    private void OnAccountChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (!refreshing)
            LoadAccount(AccountList.SelectedItem as XIVAccount);
    }

    private void LoadAccount(XIVAccount? account)
    {
        selected = account;
        UsernameBox.Text = account?.UserName;
        UsernameBox.IsReadOnly = account != null;
        DisplayNameBox.Text = account?.UserDefinedName;
        PasswordBox.Text = string.Empty;
        QuickLoginBox.IsChecked = account?.QuickLoginEnabled ?? true;
        var savedArea = account?.AreaName;
        AreaBox.ItemsSource = areas.Append(savedArea).OfType<string>().Where(x => x.Length > 0).Distinct().ToArray();
        AreaBox.SelectedItem = string.IsNullOrEmpty(savedArea) ? areas.FirstOrDefault() : savedArea;
        UseButton.IsEnabled = account is { IsWeGame: false };
        ClearButton.IsEnabled = DeleteButton.IsEnabled = account != null;
        PasswordBox.IsEnabled = account?.IsWeGame != true;
        changeAvatar = false;
        SetAvatar(null);
        if (account != null && File.Exists(AvatarPath(account)))
        {
            try { SetAvatar(new Bitmap(AvatarPath(account))); }
            catch (Exception ex) { Log.Warning(ex, "[AccountWindow] 读取头像失败"); }
        }
        ReloadProfiles();
    }

    private void OnNewAccount(object? sender, RoutedEventArgs args)
    {
        AccountList.SelectedItem = null;
        LoadAccount(null);
        UsernameBox.Focus();
    }

    private async void OnSaveAccount(object? sender, RoutedEventArgs args) => await RunAsync(SaveAccountAsync);

    private async Task SaveAccountAsync()
    {
        var account = selected;
        if (account == null)
        {
            var username = UsernameBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(username))
                throw new InvalidOperationException("请输入盛趣账号。");
            if (accounts.FindAccount(username, XIVAccountType.Sdo) != null)
                throw new InvalidOperationException("此账号已保存，请在列表中选择。");
            account = new XIVAccount { SdoLoginAccount = username, AccountType = XIVAccountType.Sdo };
            account.GenerateID();
        }

        string? encryptedPassword = null;
        if (!account.IsWeGame && !string.IsNullOrEmpty(PasswordBox.Text))
        {
            var password = PasswordBox.Text;
            encryptedPassword = await Task.Run(() => accounts.Encrypt(password));
            if (string.IsNullOrEmpty(encryptedPassword))
                throw new InvalidOperationException("无法加密保存密码，请检查钥匙串。");
        }

        account.UserDefinedName = DisplayNameBox.Text?.Trim() ?? string.Empty;
        account.AreaName = AreaBox.SelectedItem as string ?? string.Empty;
        account.QuickLoginEnabled = QuickLoginBox.IsChecked == true;
        if (!account.QuickLoginEnabled)
            account.SdoQuickLoginSecret = account.WeGameQuickLoginSecret = null;
        if (encryptedPassword != null)
            account.SdoPassword = encryptedPassword;
        if (selected == null)
            accounts.AddAccount(account);
        else
            accounts.Save(account);
        if (changeAvatar)
            SaveAvatar(account);

        SaveAutoLogin();
        ReloadAccounts(account);
        Notice.Text = "账号已保存。";
    }

    private async void OnUseAccount(object? sender, RoutedEventArgs args) => await RunAsync(async () =>
    {
        if (selected == null)
            return;
        await SaveAccountAsync();
        accounts.CurrentAccount = selected;
        Notice.Text = $"已切换到 {selected!.DisplayName}。";
    });

    private async void OnDeleteAccount(object? sender, RoutedEventArgs args) => await RunAsync(async () =>
    {
        if (selected is not { } account || !await ConfirmDialog.AskAsync(this, "删除账号",
            $"删除本地账号「{account.DisplayName}」及其保存的凭据？", "删除"))
            return;
        RemoveAvatar(account);
        accounts.RemoveAccount(account);
        ReloadAccounts(accounts.CurrentAccount);
        Notice.Text = "已删除本地账号。";
    });

    private async void OnClearCredentials(object? sender, RoutedEventArgs args) => await RunAsync(async () =>
    {
        if (selected is not { } account || !await ConfirmDialog.AskAsync(this, "清除凭据",
            $"清除「{account.DisplayName}」的保存密码和快速登录凭据？", "清除"))
            return;
        account.SdoPassword = account.SdoQuickLoginSecret = account.WeGameQuickLoginSecret = null;
        accounts.Save(account);
        PasswordBox.Text = string.Empty;
        Notice.Text = "已清除凭据。";
    });

    private async void OnChooseAvatar(object? sender, RoutedEventArgs args) => await RunAsync(async () =>
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择账号头像",
            FileTypeFilter = [new FilePickerFileType("图片") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp"] }]
        });
        if (files.Count == 0)
            return;
        await using var stream = await files[0].OpenReadAsync();
        if (!stream.CanSeek || stream.Length > 10 * 1024 * 1024)
            throw new InvalidOperationException("请选择 10 MB 以内的本地图片。");
        using var original = new Bitmap(stream);
        SetAvatar(original.CreateScaledBitmap(new Avalonia.PixelSize(
            Math.Max(1, original.PixelSize.Width * 160 / Math.Max(original.PixelSize.Width, original.PixelSize.Height)),
            Math.Max(1, original.PixelSize.Height * 160 / Math.Max(original.PixelSize.Width, original.PixelSize.Height)))));
        changeAvatar = true;
    });

    private void OnRemoveAvatar(object? sender, RoutedEventArgs args)
    {
        SetAvatar(null);
        changeAvatar = true;
    }

    private void SetAvatar(Bitmap? value)
    {
        var old = avatar;
        Avatar.Source = avatar = value;
        old?.Dispose();
    }

    private static string AvatarPath(XIVAccount value) => Path.Combine(Paths.RoamingPath, "profileIcons",
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ID))) + ".png");

    private static void RemoveAvatar(XIVAccount account)
    {
        var path = AvatarPath(account);
        if (File.Exists(path))
            File.Delete(path);
    }

    private void SaveAvatar(XIVAccount account)
    {
        if (avatar == null)
        {
            RemoveAvatar(account);
            return;
        }

        var path = AvatarPath(account);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        try
        {
            avatar.Save(temporary, PngBitmapEncoderOptions.Default);
            File.Move(temporary, path, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    private bool IsSharedProfile => ProfileScope.SelectedIndex != 1;

    private void OnProfileScopeChanged(object? sender, SelectionChangedEventArgs args) => ReloadProfiles();

    private void ReloadProfiles(DeviceProfilePreset? prefer = null)
    {
        if (PresetBox == null)
            return;
        var shared = accounts.GetSharedDeviceProfilePreset();
        UseSeparateProfile.IsVisible = ProfileRotation.IsVisible = !IsSharedProfile;
        UseSeparateProfile.IsChecked = selected?.DeviceProfileDynamicEnabled == true;
        RotateProfile.IsChecked = selected?.IsDeviceProfileRotation == true;
        RotationDays.Value = selected?.DeviceProfileRotationDays ?? AccountManager.DEFAULT_DEVICE_PROFILE_ROTATION_DAYS;
        var presets = accounts.GetDeviceProfilePresets();
        PresetBox.ItemsSource = presets;
        var presetId = prefer?.Id ?? (IsSharedProfile ? shared.Id : selected?.DeviceProfilePresetId);
        PresetBox.SelectedItem = presets.FirstOrDefault(preset => preset.Id == presetId) ?? shared;
    }

    private void OnPresetChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (PresetBox.SelectedItem is not DeviceProfilePreset preset)
            return;
        PresetRemarkBox.Text = preset.Remark;
        FillSnapshot(preset.ToSnapshot());
    }

    private void FillSnapshot(DeviceProfileSnapshot snapshot)
    {
        DeviceIdBox.Text = snapshot.DeviceId;
        MacBox.Text = snapshot.MacAddress;
        HostBox.Text = snapshot.HostName;
    }

    private void OnGenerateProfile(object? sender, RoutedEventArgs args) => FillSnapshot(FakeMachineInfo.CreateSnapshot());
    private async void OnReadMachine(object? sender, RoutedEventArgs args) => await RunAsync(() =>
    {
        FillSnapshot(RealMachineInfo.CreateSnapshot());
        return Task.CompletedTask;
    });

    private async void OnApplyProfile(object? sender, RoutedEventArgs args) => await RunAsync(() =>
    {
        var account = IsSharedProfile ? null : selected ?? throw new InvalidOperationException("请先选择已保存的账号。");
        var separate = UseSeparateProfile.IsChecked == true;
        if (account == null || separate)
        {
            var snapshot = ReadSnapshot();
            var preset = PresetBox.SelectedItem as DeviceProfilePreset;
            if (preset == null || !preset.Matches(snapshot) || preset.Remark != PresetRemarkBox.Text?.Trim())
                preset = accounts.CreateDeviceProfilePreset(snapshot, DateTimeOffset.UtcNow.UtcTicks, PresetRemarkBox.Text);
            if (account == null)
                accounts.SaveSharedDeviceProfileSelection(preset);
            else
                accounts.SaveDeviceProfileSelection(account, preset);
        }
        if (account != null)
            accounts.UpdateDeviceProfileSettings(account, separate, RotateProfile.IsChecked == true, (int)(RotationDays.Value ?? 7));
        ReloadProfiles();
        Notice.Text = "设备档案已应用。";
        return Task.CompletedTask;
    });

    private async void OnSavePreset(object? sender, RoutedEventArgs args) => await RunAsync(() =>
    {
        var preset = accounts.CreateDeviceProfilePreset(ReadSnapshot(), DateTimeOffset.UtcNow.UtcTicks, PresetRemarkBox.Text);
        ReloadProfiles(preset);
        Notice.Text = "已保存设备预设。";
        return Task.CompletedTask;
    });

    private async void OnDeletePreset(object? sender, RoutedEventArgs args) => await RunAsync(async () =>
    {
        if (PresetBox.SelectedItem is DeviceProfilePreset preset &&
            await ConfirmDialog.AskAsync(this, "删除设备预设", "删除所选预设？引用它的账号将改用替代预设。", "删除"))
        {
            accounts.DeleteDeviceProfilePreset(preset.Id);
            ReloadProfiles();
            Notice.Text = "已删除设备预设。";
        }
    });

    private DeviceProfileSnapshot ReadSnapshot()
    {
        var deviceId = DeviceIdBox.Text?.Trim();
        var host = HostBox.Text?.Trim();
        var mac = MacBox.Text?.Trim().Replace(":", "").Replace("-", "");
        if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(host) ||
            mac?.Length != 12 || !mac.All(Uri.IsHexDigit))
            throw new InvalidOperationException("请输入设备 ID、主机名和有效的 MAC 地址。");
        return new DeviceProfileSnapshot
        {
            DeviceId = deviceId, HostName = host,
            MacAddress = string.Join("-", Enumerable.Range(0, 6).Select(i => mac.Substring(i * 2, 2).ToUpperInvariant()))
        };
    }

    private void SaveAutoLogin()
    {
        settings.AutoLoginToLastAccount = AutoLoginBox.IsChecked == true;
        settings.Save();
    }

    private void OnDone(object? sender, RoutedEventArgs args)
    {
        SaveAutoLogin();
        Close();
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (busy)
            return;
        busy = true;
        var content = (Control)Content!;
        content.IsEnabled = false;
        try { await action(); }
        catch (Exception ex)
        {
            Log.Warning("[AccountWindow] 操作失败: {ErrorType}", ex.GetType().Name);
            Notice.Text = ex.Message;
        }
        finally
        {
            content.IsEnabled = true;
            busy = false;
        }
    }
}
