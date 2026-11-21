using System.ComponentModel;
using System.Diagnostics;
#if !XL_MAC
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
#endif
using Exception = System.Exception;

namespace XIVLauncher.Common.Util;

public static class FileLockDetector
{
    public static List<FileLockingProcess> GetLockingProcesses(IEnumerable<FileInfo> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var filePaths = files.Select(file => file.FullName).ToArray();
        if (filePaths.Length == 0)
            return [];

#if XL_MAC
        return GetLockingProcessesMac(filePaths);
#else
        var sessionKey = new StringBuilder(CCH_RM_SESSION_KEY + 1);
        ThrowOnFailure(RmStartSession(out var sessionHandle, 0, sessionKey));

        try
        {
            ThrowOnFailure
            (
                RmRegisterResources
                (
                    sessionHandle,
                    filePaths.Length,
                    filePaths,
                    0,
                    Array.Empty<RmUniqueProcess>(),
                    0,
                    Array.Empty<string>()
                )
            );

            return GetLockingProcesses(sessionHandle);
        }
        finally
        {
            RmEndSession(sessionHandle);
        }
#endif
    }

#if XL_MAC
    /// <summary>
    ///     macOS 下通过 <c>lsof</c> 命令检测占用指定文件的进程。
    ///     <para>等效于 Windows 的 Restart Manager（rstrtmgr.dll）。</para>
    /// </summary>
    private static List<FileLockingProcess> GetLockingProcessesMac(string[] filePaths)
    {
        try
        {
            var psi = new ProcessStartInfo("/usr/sbin/lsof")
            {
                Arguments       = "-F pcn " + string.Join(" ", filePaths.Select(QuotePath)),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow  = true
            };

            using var proc = Process.Start(psi);
            if (proc == null)
                return [];

            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(5000);

            var result = new List<FileLockingProcess>();
            string? pid = null;
            string? name = null;

            foreach (var line in output.Split('\n'))
            {
                if (line.Length < 2)
                    continue;

                var field = line[0];
                var value = line[1..];

                switch (field)
                {
                    case 'p':
                        pid = value;
                        break;
                    case 'c':
                        name = value;
                        if (pid != null && int.TryParse(pid, out var processId))
                        {
                            result.Add(new FileLockingProcess(processId, name ?? pid, pid));
                        }
                        pid = null;
                        name = null;
                        break;
                    // 'n'（文件名）、'f'（fd）等字段不参与进程识别，忽略
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            Log(ex);
            return [];
        }
    }

    private static string QuotePath(string path) =>
        "'" + path.Replace("'", "'\\''") + "'";

    private static void Log(Exception ex) =>
        Serilog.Log.Debug(ex, "[FileLockDetector] lsof 检测失败");
#else
    [DllImport("rstrtmgr", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out int dwSessionHandle, int sessionFlags, StringBuilder strSessionKey);

    [DllImport("rstrtmgr")]
    private static extern int RmEndSession(int dwSessionHandle);

    [DllImport("rstrtmgr")]
    private static extern int RmGetList(int dwSessionHandle, out int nProcInfoNeeded, ref int nProcInfo, [In] [Out] RmProcessInfo[] rgAffectedApps, out uint dwRebootReasons);

    [DllImport("rstrtmgr", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources
    (
        int               dwSessionHandle,
        int               nFiles,
        string[]          rgsFileNames,
        int               nApplications,
        RmUniqueProcess[] rgApplications,
        int               nServices,
        string[]          rgsServiceNames
    );

    private const int CCH_RM_SESSION_KEY  = RM_SESSION_KEY_LEN * 2;
    private const int CCH_RM_MAX_APP_NAME = 255;
    private const int CCH_RM_MAX_SVC_NAME = 63;
    private const int ERROR_MORE_DATA     = 234;
    private const int RM_SESSION_KEY_LEN  = 16;

    private static List<FileLockingProcess> GetLockingProcesses(int sessionHandle)
    {
        var count = 0;
        var infos = Array.Empty<RmProcessInfo>();
        var err   = 0;

        for (var i = 0; i < 16; i++)
        {
            err = RmGetList(sessionHandle, out var needed, ref count, infos, out _);

            switch (err)
            {
                case 0:
                    return infos.Take(count).Select(info => info.ToFileLockingProcess()).ToList();

                case ERROR_MORE_DATA:
                    infos = new RmProcessInfo[count = needed];
                    break;

                default:
                    ThrowOnFailure(err);
                    break;
            }
        }

        ThrowOnFailure(err);
        throw new InvalidOperationException();
    }

    private static void ThrowOnFailure(int err)
    {
        if (err != 0)
            throw new Win32Exception(err);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RmUniqueProcess
    {
        public int      dwProcessId;
        public FILETIME ProcessStartTime;
    }

    private enum RmAppType
    {
        RmUnknownApp  = 0,
        RmMainWindow  = 1,
        RmOtherWindow = 2,
        RmService     = 3,
        RmExplorer    = 4,
        RmConsole     = 5,
        RmCritical    = 1000
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RmProcessInfo
    {
        public RmUniqueProcess UniqueProcess;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_APP_NAME + 1)]
        public string AppName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_SVC_NAME + 1)]
        public string serviceShortName;

        public RmAppType applicationType;
        public int       appStatus;
        public int       tsSessionId;

        [MarshalAs(UnmanagedType.Bool)]
        public bool restartable;

        public FileLockingProcess ToFileLockingProcess() =>
            new(UniqueProcess.dwProcessId, AppName, UniqueProcess.ProcessStartTime);
    }
#endif
}

/// <summary>
///     描述占用某个文件的进程。
///     <para>Windows 版附带进程启动时间用于精确匹配；macOS 版以进程 ID 字符串代替（无 FILETIME 概念）。</para>
/// </summary>
public sealed class FileLockingProcess
(
    int    processID,
    string appName,
#if !XL_MAC
    FILETIME processStartTime
#else
    string processStartTime
#endif
)
{
    public int ProcessID { get; } = processID;

    public string AppName { get; } = appName;

    public Process? Process
    {
        get
        {
            try
            {
                var process = Process.GetProcessById(ProcessID);

#if !XL_MAC
                var fileTime = process.StartTime.ToFileTime();

                if ((uint)processStartTime.dwLowDateTime != (uint)(fileTime & uint.MaxValue))
                    return null;

                if ((uint)processStartTime.dwHighDateTime != (uint)(fileTime >> 32))
                    return null;
#endif

                return process;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
