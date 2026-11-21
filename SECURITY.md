# Security Policy

## 支持的版本

当前仅维护最新 Release 与 `main` 分支。

## 报告安全问题

请**不要**在公开 Issue 中披露漏洞细节、账号信息、Cookie、令牌或完整日志。

优先使用 GitHub 的私密漏洞报告：

1. 打开仓库的 **Security** → **Advisories** → **Report a vulnerability**
2. 或使用 GitHub Security Advisory 的私密渠道联系维护者

报告中请包含：

- 受影响版本 / commit
- macOS 与芯片型号
- 复现步骤（已脱敏）
- 影响范围（凭据泄露、任意代码执行、路径穿越、供应链等）

## 范围

本项目是第三方启动器，安全边界包括：

- 启动器自身的账号/凭据处理（Keychain、配置加密）
- 游戏启动参数、Wine 命令执行、下载与解压流程
- 补丁/差分合并的路径与校验逻辑

以下不属于本项目范围：

- FF14 游戏本体、盛趣登录服务、Dalamud 本体与第三方插件的漏洞
- Wine / CrossOver / Whisky 本身的漏洞
- 用户自行安装的插件、脚本或修改过的游戏文件

## 凭据与隐私

- 启动器使用 macOS Keychain 保存加密密钥，账号凭据不会以明文写入仓库或日志
- 请在提交日志/Issue 前移除用户名、账号 ID、会话票据、Cookie、本机绝对路径等信息