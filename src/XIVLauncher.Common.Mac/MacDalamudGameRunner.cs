using System.Diagnostics;
using XIVLauncher.Common.Game;
using XIVLauncher.Dalamud;

namespace XIVLauncher.Common.Mac;

/// <summary>
///     通过卫月注入器在 Wine 内拉起游戏并注入 Dalamud 的 <see cref="IGameRunner" />。
///     <para>与官方 GoatCorp Unix 实现语义一致：游戏由 Dalamud.Injector.exe 启动，
///     而不是由启动器直接执行 ffxiv_dx11.exe。</para>
/// </summary>
public sealed class MacDalamudGameRunner(DalamudSession session) : IGameRunner
{
    public Process? Start(string path, string workingDirectory, string arguments, IDictionary<string, string> environment, DPIAwareness dpiAwareness) =>
        session.LaunchGame(new FileInfo(path), arguments, environment);
}
