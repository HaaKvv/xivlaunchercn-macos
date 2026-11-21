# Contributing

感谢你愿意参与 XIVLauncherCN (macOS)。

## 开发环境

- macOS 11+（Apple Silicon 为主）
- .NET 10 SDK
- Xcode Command Line Tools（编译/打包）
- 可选：Wine / CrossOver 用于端到端运行测试

## 构建与测试

```bash
export PATH="$HOME/.dotnet:$PATH"

dotnet restore src/XIVLauncher.slnx
dotnet build   src/XIVLauncher.slnx -c Debug
dotnet test    src/XIVLauncher.Test/XIVLauncher.Test.csproj -c Debug
```

集成测试（`MaintenanceIntegrationTests`）会访问真实游戏目录、Wine 与网络；CI 中默认排除。需要本地验证时请先安装游戏与 Wine。

## 打包

```bash
./scripts/build-macos.sh arm64
./scripts/package-app.sh arm64
```

发布包为 framework-dependent，需要 .NET 10 Runtime；打包脚本会做 ad-hoc 签名。不要提交 `dist/`、`bin/`、`obj/` 或本地数据目录。

## 代码约定

- C# 使用 4 空格缩进；遵循现有命名与 `#if XL_MAC` 平台分支约定
- 平台相关代码放在 `XIVLauncher.Common.Mac`
- UI 放在 `XIVLauncher.Avalonia`
- 不引入 Windows 专属依赖（WPF、`CredentialManager`、`SharedMemory`、`System.Management` 等）
- 修改 Wine/Dalamud/游戏数据相关逻辑时，先备份并在 PR 描述中说明风险
- 不要提交账号、`cred.json`、`accounts.db`、日志、游戏文件、Wine Prefix 或 Dalamud 二进制

## PR 检查清单

- [ ] `dotnet build` 通过
- [ ] `dotnet test` 通过（或说明集成测试为何未运行）
- [ ] 没有提交个人路径、账号、令牌、日志或大体积构建产物
- [ ] 变更符合 GPL-3.0 与上游许可证
- [ ] UI 变更附带截图/说明（不要包含账号信息）

## 提交信息

建议使用 Conventional Commits 风格：

```
feat: add ...
fix: ...
docs: ...
chore: ...
```

## 许可证

提交代码即表示同意以 GPL-3.0 授权。第三方代码/资源必须保留其原始许可证与署名。