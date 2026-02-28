#!/bin/bash
set -e

PROJECT_DIR="/Volumes/outDisk/Data/MyMusicPlayer/MyMusicPlayer"
PUBLISH_DIR="$PROJECT_DIR/bin/Release/net8.0/osx-arm64/publish"
APP_NAME="MyMusicPlayer"
OUTPUT_DIR="/Volumes/outDisk/Data/MyMusicPlayer"
APP_BUNDLE="$OUTPUT_DIR/$APP_NAME.app"
DMG_PATH="$OUTPUT_DIR/$APP_NAME.dmg"

# ── 1. dotnet publish ────────────────────────────────────────────────────
echo "==> 编译并发布 (Release / osx-arm64)..."
cd "$PROJECT_DIR"
dotnet publish -c Release -r osx-arm64 --self-contained true \
  -p:PublishSingleFile=false --nologo -v minimal

# ── 2. .app bundle ───────────────────────────────────────────────────────
echo "==> 清理旧的 .app bundle..."
rm -rf "$APP_BUNDLE"

echo "==> 创建 .app 目录结构..."
mkdir -p "$APP_BUNDLE/Contents/MacOS"
mkdir -p "$APP_BUNDLE/Contents/Resources"

echo "==> 复制发布文件..."
cp -R "$PUBLISH_DIR/." "$APP_BUNDLE/Contents/MacOS/"

echo "==> 复制 native dylibs..."
cp "$PROJECT_DIR/NativeLibs/"*.dylib "$APP_BUNDLE/Contents/MacOS/" 2>/dev/null || true

echo "==> 生成 icns 图标..."
ICON_SRC="$PROJECT_DIR/Assets/AppIcon.png"
ICONSET_DIR="/tmp/${APP_NAME}.iconset"
rm -rf "$ICONSET_DIR"
mkdir -p "$ICONSET_DIR"

# 生成各尺寸图标
for SIZE in 16 32 64 128 256 512; do
    sips -z $SIZE $SIZE "$ICON_SRC" --out "$ICONSET_DIR/icon_${SIZE}x${SIZE}.png"       > /dev/null 2>&1
    DOUBLE=$((SIZE * 2))
    sips -z $DOUBLE $DOUBLE "$ICON_SRC" --out "$ICONSET_DIR/icon_${SIZE}x${SIZE}@2x.png" > /dev/null 2>&1
done
sips -z 1024 1024 "$ICON_SRC" --out "$ICONSET_DIR/icon_512x512@2x.png" > /dev/null 2>&1

iconutil -c icns "$ICONSET_DIR" -o "$APP_BUNDLE/Contents/Resources/$APP_NAME.icns"
rm -rf "$ICONSET_DIR"
echo "    图标生成完毕"

echo "==> 写入 Info.plist..."
cat > "$APP_BUNDLE/Contents/Info.plist" << 'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN"
  "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>MyMusicPlayer</string>
    <key>CFBundleDisplayName</key>
    <string>MyMusicPlayer</string>
    <key>CFBundleIdentifier</key>
    <string>com.myapp.mymusicplayer</string>
    <key>CFBundleVersion</key>
    <string>1.0.0</string>
    <key>CFBundleShortVersionString</key>
    <string>1.0</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleSignature</key>
    <string>????</string>
    <key>CFBundleExecutable</key>
    <string>MyMusicPlayer</string>
    <key>CFBundleIconFile</key>
    <string>MyMusicPlayer</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>NSPrincipalClass</key>
    <string>NSApplication</string>
    <key>LSMinimumSystemVersion</key>
    <string>12.0</string>
    <key>CFBundleSupportedPlatforms</key>
    <array>
        <string>MacOSX</string>
    </array>
    <key>LSApplicationCategoryType</key>
    <string>public.app-category.music</string>
</dict>
</plist>
PLIST

echo "==> 设置可执行权限..."
chmod +x "$APP_BUNDLE/Contents/MacOS/$APP_NAME"

echo "==> 移除隔离属性（xattr）..."
xattr -cr "$APP_BUNDLE" 2>/dev/null || true

# ── 3. DMG ───────────────────────────────────────────────────────────────
echo "==> 制作 DMG..."
STAGING="/tmp/dmg_staging_$$"
rm -rf "$STAGING"
mkdir -p "$STAGING"
cp -R "$APP_BUNDLE" "$STAGING/"
ln -s /Applications "$STAGING/Applications"

rm -f "$DMG_PATH"
hdiutil create -volname "$APP_NAME" \
  -srcfolder "$STAGING" \
  -ov -format UDZO \
  -o "$DMG_PATH" > /dev/null 2>&1

rm -rf "$STAGING"

echo ""
echo "✅ 打包完成！"
echo "   .app  →  $APP_BUNDLE  ($(du -sh "$APP_BUNDLE" | cut -f1))"
echo "   .dmg  →  $DMG_PATH  ($(du -sh "$DMG_PATH" | cut -f1))"
echo ""
echo "将 MyMusicPlayer.app 拖入 /Applications 即可安装，或直接双击 DMG。"
