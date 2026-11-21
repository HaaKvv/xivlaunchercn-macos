#!/bin/bash
# =============================================================================
# XIVLauncherCN (macOS) - 构建脚本
# 移植自 AtmoOmen/FFXIVQuickLauncher (GPL-3.0)
#
# 用法:
#   ./scripts/build-macos.sh [arm64|x64]
#
# 说明:
#   - 默认构建 Apple Silicon (arm64)；Intel Mac 传参 x64
#   - 输出目录: src/bin/{osx-arm64|osx-x64}/
# =============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"
SRC_DIR="$ROOT_DIR/src"

ARCH="${1:-arm64}"
RID="osx-${ARCH}"

echo "==> 构建 XIVLauncherCN (macOS) [${RID}]"

cd "$SRC_DIR"

# 定位 dotnet：优先使用环境变量 DOTNET_ROOT，其次常见安装位置
if [ -n "${DOTNET_ROOT:-}" ]; then
    export PATH="$DOTNET_ROOT:$PATH"
fi

if ! command -v dotnet >/dev/null 2>&1; then
    for candidate in "$HOME/.workbuddy/dotnet" "$HOME/.dotnet" "/usr/local/share/dotnet" "/opt/homebrew/share/dotnet"; do
        if [ -x "$candidate/dotnet" ]; then
            export DOTNET_ROOT="$candidate"
            export PATH="$candidate:$PATH"
            break
        fi
    done
fi

command -v dotnet >/dev/null 2>&1 || { echo "错误: 未找到 dotnet SDK，请安装 .NET 10 SDK"; exit 1; }
echo "==> 使用 dotnet: $(command -v dotnet)"

dotnet restore XIVLauncher.slnx -p:RuntimeIdentifier="$RID"
dotnet publish XIVLauncher.Avalonia/XIVLauncher.Avalonia.csproj \
    -c Release \
    -r "$RID" \
    --self-contained false \
    -p:PublishSingleFile=false \
    -p:RuntimeIdentifier="$RID"

echo "==> 构建完成: $SRC_DIR/bin/$RID/"
ls -la "$SRC_DIR/bin/$RID/" | head -20
