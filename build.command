#!/bin/bash
set -euo pipefail
cd -- "$(dirname -- "$0")"
xcrun swiftc -target "$(uname -m)-apple-macos14.0" -parse-as-library -swift-version 6 KeyboardLock.swift Checks.swift -o KeyboardLock -framework IOKit
./KeyboardLock --self-test
./KeyboardLock --check
app='BrickMyBoard.app'
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cat > "$app/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleExecutable</key><string>BrickMyBoard</string>
<key>CFBundleIdentifier</key><string>com.mohammedalsalhi.BrickMyBoard</string>
<key>CFBundleName</key><string>BrickMyBoard</string>
<key>CFBundleDisplayName</key><string>BrickMyBoard</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleShortVersionString</key><string>0.0.1</string>
<key>CFBundleIconFile</key><string>AppIcon</string>
<key>CFBundleVersion</key><string>1</string>
<key>LSMinimumSystemVersion</key><string>14.0</string>
<key>NSHighResolutionCapable</key><true/>
<key>NSPrincipalClass</key><string>NSApplication</string>
<key>NSInputMonitoringUsageDescription</key><string>Control only the physical keyboards you select. Keystrokes are never logged.</string>
</dict></plist>
PLIST
xcrun swiftc -target "$(uname -m)-apple-macos14.0" -parse-as-library -swift-version 6 -D NATIVE_APP KeyboardLock.swift Checks.swift App.swift Views.swift -o "$app/Contents/MacOS/BrickMyBoard" -framework AppKit -framework SwiftUI -framework Carbon -framework ServiceManagement -framework IOKit
iconset="Assets/AppIcon.iconset"
mkdir -p "$iconset"
for size in 16 32 128 256 512; do
    sips -z "$size" "$size" Assets/AppIcon.png --out "$iconset/icon_${size}x${size}.png" >/dev/null
    double=$((size * 2))
    sips -z "$double" "$double" Assets/AppIcon.png --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$app/Contents/Resources/AppIcon.icns"
cp Assets/logo.svg "$app/Contents/Resources/logo.svg"
codesign --force --sign - "$app"
codesign --verify --strict "$app"
"$app/Contents/MacOS/BrickMyBoard" --self-test
