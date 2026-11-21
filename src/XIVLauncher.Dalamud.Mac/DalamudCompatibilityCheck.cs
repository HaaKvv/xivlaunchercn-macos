using System.Runtime.InteropServices;
using Serilog;

namespace XIVLauncher.Dalamud;

/// <summary>
///     macOS 平台 Dalamud 兼容性检查。
///     <para>Windows 版检查 Visual C++ 运行库注册表项；macOS 上这些 DLL 由 Wine 兼容层
///     在 Prefix 内提供，因此仅校验进程架构（Dalamud 需 x64）。</para>
/// </summary>
public class DalamudCompatibilityCheck : IDalamudCompatibilityCheck
{
    public void EnsureCompatibility()
    {
        // macOS 移植：无 Windows 注册表与 DLL 检查需求，Wine prefix 内的运行库由
        // winetricks/CrossOver 自动安装。保留架构检查以兼容 Intel/Apple Silicon 双架构。
        EnsureArchitecture();
    }

    private static void EnsureArchitecture()
    {
        var arch = RuntimeInformation.ProcessArchitecture;

        switch (arch)
        {
            case Architecture.X86:
                throw new IDalamudCompatibilityCheck.ArchitectureNotSupportedException("Dalamud is not supported on x86 architecture.");

            case Architecture.X64:
            case Architecture.Arm64:
                // macOS 上 Rosetta 2 / Wine 提供 x64 仿真，Apple Silicon 下亦可运行 x64 游戏
                break;

            case Architecture.Arm:
                throw new IDalamudCompatibilityCheck.ArchitectureNotSupportedException("Dalamud is not supported on ARM32.");

            default:
                Log.Warning("[CompatibilityCheck] 未知架构 {Arch}, 继续尝试", arch);
                break;
        }
    }
}
