using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
#if !XL_MAC
using System.Runtime.InteropServices;
using System.Security.Principal;
#endif
using Serilog;
using SharpCompress.Archives;
using SharpCompress.Common;
using XIVLauncher.Common.Constant;

namespace XIVLauncher.Common.Util;

public static class PlatformHelpers
{
    private static readonly IPEndPoint DefaultLoopbackEndpoint = new(IPAddress.Loopback, 0);

#if XL_MAC
    /// <summary>
    ///     macOS 下将指定进程带回前台。
    ///     Wine 运行的 Windows 进程在 macOS 上以原生窗口形式呈现，
    ///     通过 <c>open</c> 命令激活其宿主应用即可。
    /// </summary>
    public static void BringProcessForeground(int pid)
    {
        try
        {
            var process = Process.GetProcessById(pid);
            // macOS 上没有 MainWindowHandle 的概念，改用系统 open 命令激活对应应用。
            var appName = process.ProcessName.Contains("ffxiv", StringComparison.OrdinalIgnoreCase)
                              ? "Final Fantasy XIV"
                              : process.ProcessName;
            var psi = new ProcessStartInfo("/usr/bin/open")
            {
                // 应用名可能含空格，使用参数数组避免拼接转义问题
                UseShellExecute = false,
                CreateNoWindow  = true
            };
            psi.ArgumentList.Add("-a");
            psi.ArgumentList.Add(appName);
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[PlatformHelper] 尝试将 {pid} 进程带往前台失败", pid);
        }
    }
#else
    public static void BringProcessForeground(int pid)
    {
        const int SW_RESTORE = 9;

        try
        {
            var process = Process.GetProcessById(pid);
            var hWnd    = process.MainWindowHandle;
            if (hWnd == nint.Zero)
                return;

            if (IsIconic(hWnd))
                ShowWindow(hWnd, SW_RESTORE);

            SetForegroundWindow(hWnd);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[PlatformHelper] 尝试将 {pid} 进程带往前台失败", pid);
        }

        return;

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(nint hWnd);

        [DllImport("user32.dll")]
        static extern bool ShowWindow(nint hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        static extern bool IsIconic(nint hWnd);
    }
#endif

    public static string GetTempFileName() =>
        Path.Combine(Path.GetTempPath(), "xivlauncher_" + Guid.NewGuid());

    public static void DeleteAndRecreateDirectory(DirectoryInfo dir)
    {
        if (dir.Exists)
            dir.Delete(true);

        dir.Create();
    }

    public static void CopyFilesRecursively(DirectoryInfo source, DirectoryInfo target)
    {
        foreach (var dir in source.GetDirectories())
            CopyFilesRecursively(dir, target.CreateSubdirectory(dir.Name));

        foreach (var file in source.GetFiles())
            file.CopyTo(Path.Combine(target.FullName, file.Name));
    }

#if XL_MAC
    /// <summary>
    ///     macOS 上“提权”即检查当前用户是否为 root（或具备管理员权限）。
    /// </summary>
    public static bool IsElevated() =>
        UnixGetUid() == 0;

    public static bool IsWindowsErrorCancelled(Exception exception) =>
        false;

    private static int UnixGetUid()
    {
        try
        {
            var psi = new ProcessStartInfo("/usr/bin/id")
            {
                Arguments       = "-u",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow  = true
            };
            using var proc = Process.Start(psi);
            var output = proc?.StandardOutput.ReadToEnd();
            proc?.WaitForExit(1000);
            return int.TryParse(output?.Trim(), out var uid) ? uid : -1;
        }
        catch
        {
            return -1;
        }
    }
#else
    public static bool IsElevated() =>
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    public static bool IsWindowsErrorCancelled(Win32Exception exception) =>
        exception.NativeErrorCode == 1223;
#endif

    public static void Unzip7ZAsset(string path, string output)
    {
        Log.Information("[DUPDATE] 正在解压 7z 包...");

        // 显式构造调用参数，避免 Windows 风格引号在 macOS 上传参错误
        try
        {
            var unzipPath = FindSevenZipExecutable();
            if (string.IsNullOrEmpty(unzipPath))
            {
                Log.Information("[DUPDATE] 未找到系统 7z 可执行文件，使用托管解压。");
            }
            else
            {
                var psi = new ProcessStartInfo
                {
                    FileName        = unzipPath,
                    Arguments       = $"x -y -bso0 -bsp0 -o\"{output}\" \"{path}\"",
                    UseShellExecute = false,
                    CreateNoWindow  = true
                };

                var proc = Process.Start(psi);

                if (proc != null)
                {
                    proc.WaitForExit();

                    if (proc.ExitCode == 0)
                    {
                        Log.Information("[DUPDATE] 7z 解压完成。");
                        return;
                    }

                    Log.Warning("[DUPDATE] 系统 7z 解压失败，退出码 {ExitCode}，回退到托管解压。", proc.ExitCode);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[DUPDATE] 系统 7z 不可用，回退到托管解压。");
        }

        using (var archive = ArchiveFactory.OpenArchive(path))
            archive.WriteToDirectory(output, new ExtractionOptions { ExtractFullPath = true, Overwrite = true });
        Log.Information("[DUPDATE] 托管解压完成。");
    }

#if XL_MAC
    /// <summary>
    ///     在常见位置查找 7z 可执行文件（Homebrew 默认安装路径）。
    ///     找不到时返回 null，由调用方回退到托管解压。
    /// </summary>
    private static string? FindSevenZipExecutable()
    {
        string[] candidates =
        [
            "/opt/homebrew/bin/7z",
            "/opt/homebrew/bin/7zz",
            "/usr/local/bin/7z",
            "/usr/local/bin/7zz",
            "/usr/bin/7z",
            Path.Combine(Paths.ResourcesPath, "7zr")
        ];

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
#else
    private static string FindSevenZipExecutable() =>
        Path.Combine(Paths.ResourcesPath, "7zr.exe");
#endif

    public static int GetAvailablePort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        socket.Bind(DefaultLoopbackEndpoint);
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

#if XL_MAC
    /// <summary>
    ///     macOS 下通过 <c>statfs</c> 获取磁盘可用空间（等效 Windows 的 GetDiskFreeSpaceEx）。
    /// </summary>
    public static long GetDiskFreeSpace(DirectoryInfo info)
    {
        if (info == null)
            throw new ArgumentNullException(nameof(info));

        try
        {
            var psi = new ProcessStartInfo("/bin/df")
            {
                Arguments       = $"-k {info.Root.FullName}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow  = true
            };

            using var proc = Process.Start(psi);
            if (proc == null)
                throw new IOException("无法执行 df 命令");

            // df -k 输出: Filesystem 1024-blocks Used Available Capacity iused ifree %iused Mounted on
            var lines = proc.StandardOutput.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
            proc.WaitForExit(5000);

            if (lines.Length < 2)
                throw new IOException("df 输出格式异常");

            var fields = lines[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 4)
                throw new IOException("df 输出字段不足");

            var availableKilobytes = long.Parse(fields[3]);
            return availableKilobytes * 1024;
        }
        catch (Exception ex) when (ex is not IOException)
        {
            throw new IOException("获取磁盘空间失败", ex);
        }
    }
#else
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetDiskFreeSpaceEx
    (
        string    lpDirectoryName,
        out ulong lpFreeBytesAvailable,
        out ulong lpTotalNumberOfBytes,
        out ulong lpTotalNumberOfFreeBytes
    );

    public static long GetDiskFreeSpace(DirectoryInfo info)
    {
        if (info == null)
            throw new ArgumentNullException(nameof(info));

        ulong dummy = 0;

        if (!GetDiskFreeSpaceEx(info.Root.FullName, out var freeSpace, out dummy, out dummy))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        return (long)freeSpace;
    }
#endif

    public static string GetVersion()
    {
        var assembly  = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var attribute = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(assembly, typeof(AssemblyInformationalVersionAttribute))!;
        var name      = (AssemblyProductAttribute)Attribute.GetCustomAttribute(assembly,              typeof(AssemblyProductAttribute))!;
        Console.WriteLine(name?.Product + " v" + attribute?.InformationalVersion);
        return name?.Product + " v" + attribute?.InformationalVersion;
    }
}
