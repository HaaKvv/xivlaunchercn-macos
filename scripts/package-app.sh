#!/bin/bash
# =============================================================================
# XIVLauncherCN (macOS) - .app 打包脚本
# 将 dotnet publish 产物打包为 macOS .app bundle（可选 .dmg）
#
# 用法:
#   ./scripts/package-app.sh [arm64|x64]
#   环境变量:
#     APP_OUT=/absolute/path/XIVLauncherCN.app  指定输出 .app（默认 dist/）
#     MAKE_DMG=1  同时生成 .dmg（需要 hdiutil，macOS 自带）
#     SIGN_ID="Developer ID Application: xxx"  启用 codesign（可选）
# =============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"
SRC_DIR="$ROOT_DIR/src"

ARCH="${1:-arm64}"
RID="osx-${ARCH}"
APP_NAME="XIVLauncherCN"
APP_BUNDLE="${APP_OUT:-$ROOT_DIR/dist/$APP_NAME.app}"
BIN_DIR="$SRC_DIR/bin/$RID"

if [ ! -d "$BIN_DIR" ]; then
    echo "错误: 未找到构建产物 $BIN_DIR，请先运行 ./scripts/build-macos.sh $ARCH"
    exit 1
fi

# APP_OUT 是外部输入，删除前做基本校验，避免误删根目录/家目录或非 .app 路径
if [ -z "$APP_BUNDLE" ] || [ "$APP_BUNDLE" = "/" ] || [ "$APP_BUNDLE" = "$HOME" ]; then
    echo "错误: 非法的 APP_OUT: $APP_BUNDLE" >&2
    exit 1
fi
case "$APP_BUNDLE" in
    /*) ;;
    *) echo "错误: APP_OUT 必须是绝对路径: $APP_BUNDLE" >&2; exit 1 ;;
esac
case "$APP_BUNDLE" in
    *.app) ;;
    *) echo "错误: APP_OUT 必须以 .app 结尾: $APP_BUNDLE" >&2; exit 1 ;;
esac

echo "==> 打包 .app: $APP_BUNDLE"

# 创建 .app 结构
rm -rf "$APP_BUNDLE"
mkdir -p "$APP_BUNDLE/Contents/MacOS"
mkdir -p "$APP_BUNDLE/Contents/Resources"

# 复制运行库（framework-dependent）
cp -R "$BIN_DIR/"* "$APP_BUNDLE/Contents/MacOS/" 2>/dev/null || true

# 保留原生 apphost 作为实际入口（进程名/菜单栏才是 XIVLauncherCN，而不是 dotnet）
if [ -f "$APP_BUNDLE/Contents/MacOS/XIVLauncherCN" ]; then
    mv "$APP_BUNDLE/Contents/MacOS/XIVLauncherCN" "$APP_BUNDLE/Contents/MacOS/XIVLauncherCN.bin"
fi

# 生成 Info.plist
cat > "$APP_BUNDLE/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>XIVLauncherCN</string>
    <key>CFBundleDisplayName</key>
    <string>XIVLauncherCN (macOS)</string>
    <key>CFBundleIdentifier</key>
    <string>cn.xivlauncher.macos</string>
    <key>CFBundleVersion</key>
    <string>2.5.3</string>
    <key>CFBundleShortVersionString</key>
    <string>2.5.3</string>
    <key>CFBundleExecutable</key>
    <string>XIVLauncherCN</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>LSMinimumSystemVersion</key>
    <string>11.0</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>NSPrincipalClass</key>
    <string>NSApplication</string>
    <key>NSHumanReadableCopyright</key>
    <string>Copyright (c) 2024-2026 AtmoOmen &amp; contributors. GPL-3.0.</string>
    <key>CFBundleIconFile</key>
    <string>dalamud_icon</string>
</dict>
</plist>
PLIST

# 启动器包装脚本
cat > "$APP_BUNDLE/Contents/MacOS/XIVLauncherCN" <<'LAUNCH'
#!/bin/bash
# macOS .app 启动包装器
# 自动定位 dotnet 运行时，然后 exec 原生 apphost（保持应用名与进程名）
DIR="$(cd "$(dirname "$0")" && pwd)"

find_dotnet_root() {
    # 1. 内嵌运行时（self-contained 发布时存在）
    if [ -x "$DIR/dotnet" ]; then
        echo "$DIR"
        return 0
    fi

    # 2. PATH 中的 dotnet
    if command -v dotnet >/dev/null 2>&1; then
        echo "$(dirname "$(command -v dotnet)")"
        return 0
    fi

    # 3. 常见安装位置探测
    for candidate in "$HOME/.workbuddy/dotnet" "$HOME/.dotnet" "/usr/local/share/dotnet" "/opt/homebrew/share/dotnet" "/usr/share/dotnet"; do
        if [ -x "$candidate/dotnet" ]; then
            echo "$candidate"
            return 0
        fi
    done

    return 1
}

DOTNET_DIR="$(find_dotnet_root)"
if [ -z "$DOTNET_DIR" ]; then
    echo "错误: 未找到 dotnet 运行时。请安装 .NET 10 Runtime 后重试。" >&2
    exit 1
fi

export DOTNET_ROOT="$DOTNET_DIR"
exec "$DIR/XIVLauncherCN.bin" "$@"
LAUNCH
chmod +x "$APP_BUNDLE/Contents/MacOS/XIVLauncherCN"

# 复制图标（若有 .icns）
if [ -f "$SRC_DIR/XIVLauncher.Avalonia/Resources/dalamud_icon.icns" ]; then
    cp "$SRC_DIR/XIVLauncher.Avalonia/Resources/dalamud_icon.icns" "$APP_BUNDLE/Contents/Resources/"
fi

# Managed .dll/.pdb/.json are data, not Mach-O. Remove execute bits so codesign
# does not treat them as nested code, then sign nested executables explicitly.
find "$APP_BUNDLE/Contents/MacOS" -maxdepth 1 -type f \
    \( -name '*.dll' -o -name '*.pdb' -o -name '*.json' -o -name '*.xml' -o -name '*.txt' \) \
    -exec chmod a-x {} +

SIGN_ARG="${SIGN_ID:--}"
if [ -n "${SIGN_ID:-}" ]; then
    echo "==> codesign (identity: $SIGN_ID)"
else
    echo "==> ad-hoc codesign"
fi

for nested in \
    "$APP_BUNDLE/Contents/MacOS/XIVLauncherCN.bin" \
    "$APP_BUNDLE/Contents/MacOS/Resources/xdelta3"
do
    if [ -f "$nested" ]; then
        codesign --force --sign "$SIGN_ARG" "$nested"
    fi
done

codesign --force --deep --sign "$SIGN_ARG" "$APP_BUNDLE"
codesign --verify --deep --strict "$APP_BUNDLE"
echo "==> codesign verified"

echo "==> .app 打包完成: $APP_BUNDLE"

# 可选: 生成 DMG
if [ "${MAKE_DMG:-0}" = "1" ]; then
    DMG_PATH="$ROOT_DIR/dist/$APP_NAME-$ARCH.dmg"
    echo "==> 生成 DMG: $DMG_PATH"
    mkdir -p "$ROOT_DIR/dist"
    hdiutil create -volname "$APP_NAME" -srcfolder "$APP_BUNDLE" -ov -format UDZO "$DMG_PATH"
    echo "==> DMG 完成: $DMG_PATH"
fi
