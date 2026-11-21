# Troubleshooting

## Logs

- Launcher: `~/Library/Application Support/XIVLauncherCN/logs/launcher-YYYYMMDD.log`
- Dalamud: `~/.xlcore_cn/logs/dalamud.log`, `dalamud.injector.log`, `dalamud.boot.log`
- Wine: `~/Library/Application Support/XIVLauncherCN/wine.log`

请在 Issue 中附上日志尾部，并先移除账号、Cookie、令牌、本机路径等敏感信息。

## 启动器没有检测到 Wine

- 确认已安装 xlcore / CrossOver / Whisky 之一
- xlcore 路径：`~/.xlcore_cn/compatibilitytool/beta/wine/bin/wine64`
- 在设置中指定游戏目录，或确认游戏位于 `~/.xlcore_cn/ffxiv`

## 登录失败 / 扫码无响应

- 国服扫码登录通常最稳定；静态密码可能要求验证码
- 检查网络与 DNS；不要使用来源不明的网络加速器
- 查看启动器日志中的 `开始登录` / `登录工作流返回`

## 卡在“正在准备卫月”

卫月更新器可能在检查/下载资源。查看 `[DUPDATE]` / `[DASSET]` 日志；网络异常会自动重试。首次准备可能需要几十秒。

如果持续卡住：

- 关闭启动器与游戏后重试
- 检查 `~/Library/Application Support/XIVLauncherCN/addon/Hooks/` 的完整性
- 确认没有残留的 Wine 进程占用 Prefix（可用 `wineserver -k` 结束，但会关闭该 Prefix 内所有 Windows 程序）

## 卫月没有注入

- 确认卫月已启用且版本与游戏版本匹配
- 查看 `dalamud.injector.log` 与启动器日志中的 `[MacDalamudRunner]`
- 旧的 xlcore「修复游戏文件」有已知数据损坏风险，请使用本启动器的检查更新/修复

## 游戏更新 / 修复

- 先用启动器的「检查更新」确认目标版本
- 更新失败多数是网络或磁盘空间问题；补丁下载支持重试
- 不要手动替换 `ffxiv_dx11.exe` 或混用不同版本的游戏文件

## 性能 / 分辨率

- 2K 窗口下可用游戏内 3D 分辨率缩放 / FSR 降低内部渲染分辨率
- 避免同时叠加多层超分（例如 DXMT MetalFX 2x + 游戏内 FSR）
- 人多场景通常受 CPU 主线程限制；关闭重型插件与后台程序

## Gatekeeper 拦截

发布包为 ad-hoc 签名。首次启动可右键 → 打开，或：

```bash
xattr -dr com.apple.quarantine /Applications/XIVLauncherCN.app
```

如果仍然无法启动，请从源码构建，或在终端直接运行 App 内的可执行文件查看错误输出。