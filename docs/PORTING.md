# 移植说明：XIVLauncherCN (Soil) → macOS

本文档记录从 **AtmoOmen/FFXIVQuickLauncher**（XIVLauncherCN Soil 分支，Windows-only WPF 应用）
移植到 macOS 的完整决策、改动点与后续维护指南。

## 1. 背景与目标

原项目（AutoOmen）是一个面向 FF14 国服的非官方启动器，功能包括：账号登录、游戏启动、
卫月（Dalamud）插件引导、跨大区旅行、游戏补丁更新等。原项目使用 WPF + `net10.0-windows`
目标框架，仅支持 Windows。

本移植的目标：

1. 在 macOS 上实现与原项目**功能一致**的启动器（登录、启动、Dalamud、版本检测、补丁）。
2. 保持**一致的 UI 交互体验**，同时遵循 macOS 设计规范。
3. 将 Windows 平台依赖替换为 macOS 等效实现。
4. 保留 GPL-3.0 协议，代码结构清晰，便于社区维护。

## 2. 关键技术事实

- **FF14 国服没有 macOS 原生客户端**。上游 GoatCorp 官方对 macOS 的解决方案是：
  通过 **Wine / CrossOver** 运行 Windows 版游戏客户端，启动器本体在 macOS 原生运行。
- **Dalamud 注入器是 Windows PE 可执行文件**，macOS 上必须在 Wine Prefix 内运行它来完成注入。
- AutoOmen 分支相对官方上游做了大量国服定制（盛趣账号、DCTravel、无数据上报等），
  移植时**以 AutoOmen 代码为基础**，macOS 平台层参照 GoatCorp 官方 `XIVLauncher.Common.Unix` 的架构。

## 3. 工程结构（改动一览）

```
XIVLauncherCN-macOS/
├── LICENSE                       # GPL-3.0（原样保留）
├── README.md
├── scripts/
│   ├── build-macos.sh            # [新增] dotnet publish osx-arm64/osx-x64
│   └── package-app.sh            # [新增] .app 打包 / codesign / DMG
├── docs/PORTING.md               # 本文档
└── src/
    ├── XIVLauncher.slnx          # [改写] 新解决方案
    ├── Directory.Build.props     # [改写] 增加 XL_MAC 编译符号
    ├── Directory.Packages.props  # [改写] 裁剪 Windows 专属包
    ├── XIVLauncher.Common/       # [移植] 核心逻辑（去 Windows 依赖）
    ├── XIVLauncher.Common.Http/  # [原样] HTTP 工具
    ├── XIVLauncher.Account/      # [移植] 账号（Keychain 替换凭据管理器）
    ├── XIVLauncher.Login/        # [移植] 登录（WeGame stub）
    ├── XIVLauncher.DCTravel/     # [原样] 跨大区旅行（纯 HTTP）
    ├── XIVLauncher.GamePatchV3/  # [移植] 补丁（Vcdiff IPC 改写）
    ├── XIVLauncher.Dalamud.Mac/  # [新增] Dalamud 引导逻辑（注入器抽象化）
    ├── XIVLauncher.Common.Mac/   # [新增] macOS 平台层（Wine 兼容层 + 游戏/注入器 runner）
    └── XIVLauncher.Avalonia/     # [新增] Avalonia UI（替代 WPF）
```

## 4. 逐模块移植清单

### 4.1 XIVLauncher.Common（核心逻辑）

| 文件 | 改动 |
| --- | --- |
| `Constant/Paths.cs` | 删除注册表读取（`GetGamePath` 原用 `HKLM\...\Uninstall\最终幻想14`）；macOS 改为「配置文件路径 → 常见安装位置扫描（家目录 / Volumes / CrossOver Bottles）→ 默认路径」。数据目录由 `%AppData%\XIVLauncherCN` 映射为 `~/Library/Application Support/XIVLauncherCN`（`SpecialFolder.ApplicationData` 在 macOS 上即为该目录，无需显式改动）。`ResolveExecutablePath` 不再硬编码 `.exe`。 |
| `Util/PlatformHelpers.cs` | `BringProcessForeground`：`user32` API → macOS `open -a` 激活；`IsElevated`：`WindowsPrincipal` → `id -u == 0`；`GetDiskFreeSpace`：`kernel32.GetDiskFreeSpaceEx` → `/bin/df -k` 解析；`Unzip7ZAsset`：不再依赖 `7zr.exe`，查找 Homebrew 7z 后回退托管解压。 |
| `Util/FileLockDetector.cs` | `rstrtmgr.dll`（Restart Manager）→ `/usr/sbin/lsof` 检测占用进程。 |
| `Util/GameHelpers.cs` | 保留（`StartOfficialLauncher` 的 `runas` verb 在 macOS 上由 UseShellExecute 处理）。 |
| `Game/RestartMonitor.cs` | `System.Management`（WMI 查 `Win32_Process`）→ `/usr/bin/pgrep -P` 查找崩溃处理器子进程。 |
| `Game/FFXIVProcess.cs` / `AutoInjectProcessSelector.cs` / `Launcher.cs` / `IGameRunner.cs` | 原样复用（纯 .NET，跨平台）。 |
| `Game/GameArgumentInterop.cs` / `NativeAclFix.cs` | **不移植**（Windows 进程内存读取与 DACL 注入，macOS 无对应物；手动注入功能在 macOS 上由 Wine 注入链路取代）。 |
| `DPIAwareness.cs` | 保留枚举（macOS 上实际由系统缩放处理）。 |

### 4.2 XIVLauncher.Account（账号与凭据）

- **KeychainCred**（新增，`Cred/Providers/KeychainCred.cs`）：调用 `/usr/bin/security` 命令
  访问 macOS 钥匙串，语义与 Windows 版 `CredentialManager` 完全一致——钥匙串中保存随机
  加密密钥，凭据密文仍由 `EncryptionHelper`（Argon2id + Aegis256）加密后存配置文件。
- `CredType` 枚举：`XL_MAC` 下用 `Keychain` 替代 `WindowsCredManager` / `WindowsHello`。
- `AccountManager.GetCredProvider`：条件编译选择 `KeychainCred`。
- 未移植：`AdysTech.CredentialManager`、`WindowsHello`（生物识别）。

### 4.3 XIVLauncher.Login（登录）

- 静态密码 / 一键 / 扫码渠道：**原样复用**（纯 HTTP + 加密，跨平台）。
- **WeGame**：macOS 上不可用（依赖 `version.dll` DLL 劫持、NamedPipe、`wegame://` URI）。
  移植策略：保留接口 `IWeGameTokenCaptureCoordinator` 与 `WeGameCaptureResult`，
  新增 `WeGameTokenCaptureCoordinator` stub 实现，捕获时提示"不支持"并返回 null，
  上层自动回退。`WeGameLoginDataReader`（读进程内存）未移植。
- `ArgReader`：仅服务于 WeGame 链路，未移植。

### 4.4 XIVLauncher.DCTravel（跨大区旅行）

- **整体原样复用**。`DCTravelClient`（HTTP API）与 `DCTravelListener`（EmbedIO 本地
  `127.0.0.1` 服务）均为纯跨平台代码，无注册表/进程依赖。

### 4.5 XIVLauncher.GamePatchV3（补丁）

- **VcdiffClient**（改写）：原实现用 `SharedMemory` 包（Windows 内存映射文件 + 命名事件）
  与差分 worker 进程通信；macOS 改写为 **Unix Domain Socket**，协议格式（4 字节操作码 +
  字符串 + 长度前缀消息）保持与原版一致。
- `VcdiffShim` / `XdeltaShim`（Rust DLL）：macOS 需另行编译 dylib 版本，
  本项目提供 Unix Domain Socket 客户端，worker 端（`VcdiffWorker`）按同样协议实现即可。
- 下载/完整性/安装逻辑：原样复用。

### 4.6 XIVLauncher.Dalamud.Mac（Dalamud 引导）

- 从原 `XIVLauncher.Dalamud` 移植：`DalamudUpdater`（远程版本检测、`hashes.json`、
  `latest.7z` 下载、7z 解压）、`DalamudService`、`DalamudSession`、`DalamudStartInfo`、
  `DalamudInjectorArgs`、`DalamudConsoleOutput` 等。
- `DalamudService.CreateLauncher` 改为**依赖注入 `IDalamudRunner`**（不再 `new DalamudInjector()`）。
- `DalamudSession.InjectGame` 改为调用 `IDalamudRunner.Run`（Windows 上为静态注入，
  macOS 上为 Wine 内注入器启动）。
- `DalamudCompatibilityCheck`（macOS 版）：删除 VC++ 运行库注册表检查，仅保留架构校验
  （x64 / arm64 均可，Rosetta 2 提供 x64 仿真）。

### 4.7 XIVLauncher.Common.Mac（macOS 平台层，新增）

| 文件 | 职责 |
| --- | --- |
| `Compatibility/WineSettings.cs` | 检测 Wine 环境：CrossOver → Whisky → Homebrew/系统 Wine，自动定位 `wine64`。 |
| `Compatibility/CompatibilityTools.cs` | Wine Prefix 管理（`wineboot -u` 初始化）、`RunInPrefix` 执行命令（注入 `WINEPREFIX`/`WINEDLLOVERRIDES` 等）、`winepath` 路径映射、`reg add`、`wineserver -k` 退出。 |
| `MacGameRunner.cs` | 实现 `IGameRunner`：将 macOS 路径映射为 Wine 内路径（`Z:` 盘）后通过 `wine64` 启动 `ffxiv_dx11.exe`。 |
| `MacDalamudRunner.cs` | 实现 `IDalamudRunner`：在 Wine Prefix 内运行 `Dalamud.Injector.exe`，解析其 JSON 输出（pid）获取游戏进程句柄。 |

### 4.8 XIVLauncher.Avalonia（UI，新增）

- 用 **Avalonia 12** 重写原 WPF UI，保持原版的布局与交互：
  - 主窗口：左上 logo + 标题，右上游戏版本/新闻刷新/数据目录按钮，底部状态栏，
    主体「左侧官方新闻列表 + 右侧登录卡片」（与原 `MainWindow.xaml` 一致）。
  - 登录卡片：大区下拉、登录方式（扫码/一键/静态）、用户名/密码输入、登录并启动按钮。
- 遵循 macOS 设计规范：系统原生标题栏（`Window` 默认）、深色主题、圆角卡片、
  数据目录提示等。
- 业务编排：`MainWindowViewModel` 等价移植原 `MainWindowViewModel.cs`，
  用 CommunityToolkit.Mvvm（原项目同款）；将 WPF 的 `Window`/`Dispatcher`
  依赖替换为平台无关实现。
- `ILoginWorkflowInteraction` 的 macOS 实现：`MacLoginWorkflowInteraction`。

## 5. 编译符号约定

- `XL_MAC`：所有 `#if XL_MAC` 分支为 macOS 专属代码，`#else` 保留 Windows 原实现。
  这样同一份源码可同时在两个平台构建（社区可继续维护 Windows 版）。
- 无需 `WIN32` 宏（原项目 Common 中定义，已移除）。

## 6. 依赖包对照

| 原项目包 | 状态 | 说明 |
| --- | --- | --- |
| WPF 全家桶（Dragablz / MaterialDesign / MahApps / MdXaml / CalcBinding / WindowsAPICodePack） | 移除 | 由 Avalonia 取代 |
| `AdysTech.CredentialManager` | 移除 | 由 KeychainCred 取代 |
| `PInvoke.Kernel32` / `Reloaded.*` / `Iced` | 移除 | Windows 注入/内存读取 |
| `System.Management` | 移除 | WMI → pgrep |
| `SharedMemory` | 移除 | → Unix Domain Socket |
| `Velopack`（自更新） | 移除（暂） | macOS 自更新待后续接入 Sparkle |
| `Avalonia` 12.1.1 系列 | 新增 | 跨平台 UI |
| `sqlite-net-pcl` / `NSec` / `Konscious.Argon2` / `SharpCompress` / `EmbedIO` / `Downloader` / `StreamJsonRpc` / `Newtonsoft.Json` / `Serilog` / `CommunityToolkit.Mvvm` | 保留 | 跨平台 |

## 7. 构建与打包

```bash
./scripts/build-macos.sh arm64     # 或 x64
./scripts/package-app.sh arm64     # 或 x64；MAKE_DMG=1 生成 DMG
```

注意：

- 需要 .NET 10 SDK（`global.json` 指定 10.0.100+，rollForward latestMajor）。
- 打包的 `.app` 默认 **ad-hoc 签名**（仅本机运行）；对外分发需 Developer ID 证书
  （`SIGN_ID` 环境变量）。

## 8. 后续维护指南 / TODO

- [x] **KeychainCred 参数传递**（验证阶段修复）：`security` 命令非 shell，改用 `ArgumentList` 传参，补 `-a` 必填参数，空串防护
- [x] **`$HOME` 环境变量展开**（验证阶段修复）：macOS 上 `Environment.ExpandEnvironmentVariables` 不支持 `$VAR`，Paths 增加正则展开
- [ ] **Wine 版本细化**：当前 `WINEDLLOVERRIDES` 仅覆盖基础运行库；游戏运行所需
      `dxvk`/`vkd3d`/字体等建议参考 CrossOver 预设。
- [ ] **Dalamud 完整链路验证**：需要真实国服游戏 + Wine 环境做端到端验证。
- [ ] **二维码展示**：`MacLoginWorkflowInteraction.ShowQrCode` 当前仅提示文本，
      建议接入 Avalonia 图片显示（`QRCoder` 库）。
- [ ] **Vcdiff worker**：为 macOS 编译 `xivlauncher_xdelta_shim.dylib`（Rust），
      并实现 Unix Domain Socket 版 `VcdiffWorker`。
- [ ] **自更新**：接入 Sparkle 或 .NET 自更新方案。
- [ ] **WeGame 渠道**：macOS 上基本不可行，除非未来有官方支持。

详细验证结果见发布说明与 [TROUBLESHOOTING.md](TROUBLESHOOTING.md)。

## 9. 协议与合规

- GPL-3.0（见 LICENSE），保留原项目版权声明。
- 本项目不提供、不捆绑任何游戏客户端文件或 Dalamud 本体（运行时按需下载）。
- 与 Square Enix 服务条款冲突的风险由使用者自行评估。
