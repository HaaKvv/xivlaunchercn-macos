# Changelog

## v2.5.3-macos.1

首个公开版本。

### Added

- macOS 原生 Avalonia 启动器（登录 → 游戏启动）
- 扫码 / 一键 / 静态密码登录
- Dalamud 版本检测、更新与 Wine 内注入
- 复用既有 XIVLauncherCN.Core / xlcore Wine Prefix、游戏与卫月配置
- 本地账号管理、设备档案与上次账号自动登录
- 跨大区旅行、在途订单查询与返回原大区
- 游戏 V3 补丁下载、完整性校验与文件修复
- 使用随包 xdelta3 完成 V3 差分合并
- macOS Keychain 凭据存储
- 设置窗口：游戏/补丁目录、启动参数、卫月加载方式与延迟、Wine 信息
- GitHub Actions CI 与 macOS arm64 Release 打包

### Known limitations

- 仅支持 FF14 国服；WeGame 渠道不可用
- 需要 Wine / CrossOver / Whisky 兼容层
- 发布包为 Apple Silicon arm64；Intel 需自行构建
- 无内置自更新