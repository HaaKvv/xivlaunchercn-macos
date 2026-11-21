<div align="center">
  <img src="src/XIVLauncher.Avalonia/Resources/logo.png" width="140" alt="XIVLauncherCN macOS logo" />

  <h1>XIVLauncherCN (macOS)</h1>

  <p>
    Unofficial macOS launcher for <b>FINAL FANTASY XIV (China)</b>, built with Avalonia.<br/>
    基于 <a href="https://github.com/AtmoOmen/FFXIVQuickLauncher">AtmoOmen/FFXIVQuickLauncher</a>（XIVLauncherCN Soil 分支）移植。
  </p>

  <p>
    <a href="https://github.com/HaaKvv/xivlaunchercn-macos/actions/workflows/ci.yml"><img alt="CI" src="https://github.com/HaaKvv/xivlaunchercn-macos/actions/workflows/ci.yml/badge.svg" /></a>
    <img alt="License: GPL-3.0" src="https://img.shields.io/badge/license-GPL--3.0-blue" />
    <img alt="Platform: macOS" src="https://img.shields.io/badge/platform-macOS-lightgrey" />
    <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10-512BD4" />
  </p>
</div>

> [!WARNING]
> 这是一个**非官方社区项目**，与 Square Enix、盛趣游戏、CodeWeavers、Dalamud 官方均无隶属关系。FF14 国服没有原生 macOS 客户端；本项目通过 Wine / CrossOver / Whisky 运行 Windows 客户端。
>
> 本项目**不包含**游戏本体、游戏资源或 Dalamud 本体。使用前请自备合法游戏账号与客户端，并自行评估第三方启动器/插件的服务条款风险。作者不对账号、数据或设备损失负责。

## 功能

- FF14 国服客户端启动：扫码 / 一键 / 静态密码登录
- 卫月（Dalamud）版本检测、更新、注入与启动
- 本地账号管理、头像、设备档案与上次账号自动登录
- 启动器内跨大区旅行、在途订单查询与返回原大区
- 游戏版本检测、V3 补丁下载、完整性校验与文件修复
- 复用既有 XIVLauncherCN.Core / xlcore 的 Wine Prefix、游戏与卫月配置
- macOS Keychain 凭据存储；无遥测
- Avalonia UI，原生 macOS 窗口

## 系统要求

- macOS 11+（发布包为 **Apple Silicon / arm64**；Intel 需自行构建并替换 x64 的 xdelta3）
- [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)（运行发布包需要）
- Wine 环境之一：
  - XIVLauncherCN.Core / xom Wine（推荐，复用已有 Prefix）
  - [CrossOver](https://www.codeweavers.com/crossover)
  - [Whisky](https://getwhisky.app/)
- 已安装的 FF14 国服 Windows 客户端
- 合法有效的 FF14 国服账号

## 安装

### 使用 Release（推荐）

1. 从 [Releases](https://github.com/HaaKvv/xivlaunchercn-macos/releases) 下载 `XIVLauncherCN-macos-arm64.zip`。
2. 解压后把 `XIVLauncherCN.app` 拖入 `/Applications`。
3. 发布包使用 ad-hoc 签名，首次启动若被 Gatekeeper 拦截：
   - 右键 App → 打开；或
   - 在终端执行：`xattr -dr com.apple.quarantine /Applications/XIVLauncherCN.app`
4. 首次启动会自动检测 Wine / CrossOver / 游戏路径；也可以手动选择游戏目录。

> 发布包为 framework-dependent，需要系统中有 .NET 10 Runtime。启动器会从 `~/.dotnet`、`~/.workbuddy/dotnet`、Homebrew 或系统路径查找 dotnet。

### 从源码构建

```bash
# 前置：.NET 10 SDK、Xcode Command Line Tools
export PATH="$HOME/.dotnet:$PATH"

dotnet build src/XIVLauncher.slnx -c Release
dotnet test  src/XIVLauncher.Test/XIVLauncher.Test.csproj -c Release

./scripts/build-macos.sh arm64
./scripts/package-app.sh arm64
open dist/XIVLauncherCN.app
```

构建与打包细节见 [docs/BUILDING.md](docs/BUILDING.md)。

## 数据与配置

- 启动器配置/账号库/日志：`~/Library/Application Support/XIVLauncherCN/`
- 复用 xlcore 时：
  - Wine：`~/.xlcore_cn/compatibilitytool/beta/wine`
  - Prefix：`~/.xlcore_cn/wineprefix`
  - 游戏：`~/.xlcore_cn/ffxiv`
  - 卫月配置/插件：`~/.xlcore_cn/dalamudConfig.json`、`installedPlugins/`、`pluginConfigs/`
- 本启动器下载的卫月二进制位于 `~/Library/Application Support/XIVLauncherCN/addon/Hooks/`，不会覆盖旧启动器的卫月目录。

## 项目结构

```
src/
├── XIVLauncher.slnx
├── XIVLauncher.Common/          # 核心逻辑
├── XIVLauncher.Common.Http/     # HTTP 工具
├── XIVLauncher.Account/         # 账号 / Keychain 凭据
├── XIVLauncher.Login/           # 登录工作流
├── XIVLauncher.DCTravel/        # 跨大区旅行
├── XIVLauncher.GamePatchV3/     # V3 补丁 / 完整性 / 修复
├── XIVLauncher.Dalamud.Mac/     # Dalamud 更新与注入参数
├── XIVLauncher.Common.Mac/      # macOS / Wine 平台层
├── XIVLauncher.Avalonia/        # Avalonia UI
└── XIVLauncher.Test/            # xUnit 测试
```

更多设计见 [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) 与 [docs/PORTING.md](docs/PORTING.md)。

## 已知限制

- 仅面向 FF14 **国服**；WeGame 渠道在 macOS 上不可用
- 需要 Wine 兼容层；性能取决于游戏版本、Wine/DXMT、FSR/DLSS 与硬件
- Dalamud 与第三方插件由各自项目维护；插件兼容性/封禁策略不由本项目控制
- 发布包目前为 arm64；Intel 支持需要自行构建
- 暂无内置自更新

## 隐私与安全

- 账号密码/快速登录密钥存储在 macOS Keychain，并以配置文件中的加密字段保存
- 启动器不上报遥测
- 运行时会访问盛趣登录服务、游戏文件 CDN、Dalamud 资源源等第三方服务
- 请勿在 Issue 中上传账号、密码、Cookie、完整日志中的凭据或游戏文件

安全问题请按 [SECURITY.md](SECURITY.md) 私下报告。

## 参与贡献

欢迎提交 Issue / PR。请先阅读 [CONTRIBUTING.md](CONTRIBUTING.md)。本项目遵循 [Contributor Covenant](CODE_OF_CONDUCT.md)。

## 许可证与致谢

本项目以 **GPL-3.0** 发布，保留上游 XIVLauncher / XIVLauncherCN / AtmoOmen 的版权与许可证。第三方组件与资源声明见 [NOTICE](NOTICE) 与 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

- [GoatCorp/FFXIVQuickLauncher](https://github.com/GoatCorp/FFXIVQuickLauncher) — 上游项目与 Unix 平台实现参考
- [AtmoOmen/FFXIVQuickLauncher](https://github.com/AtmoOmen/FFXIVQuickLauncher) — 国服 Soil 分支，本移植基础
- [ottercorp/XIVLauncher.Core](https://github.com/ottercorp/XIVLauncher.Core) — macOS Wine/兼容层实现参考
- [Avalonia](https://avaloniaui.net/)、[.NET](https://dotnet.microsoft.com/)、[Dalamud](https://github.com/goatcorp/Dalamud) 及所有第三方依赖的作者