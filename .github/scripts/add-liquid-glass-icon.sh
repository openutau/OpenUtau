#!/bin/bash
# Adds a Liquid Glass (Icon Composer) app icon to an already-built macOS .app bundle.
# If the .icon source doesn't exist, this is a no-op so older branches / missing
# assets don't break the build.
#
# Usage: add-liquid-glass-icon.sh <path-to-.app> <path-to-.icon> [icon-asset-name]

set -euo pipefail

APP_PATH="$1"
ICON_SOURCE="$2"
ICON_ASSET_NAME="${3:-OpenUtau}"

if [ ! -d "$ICON_SOURCE" ]; then
    echo "No Icon Composer source at $ICON_SOURCE — skipping Liquid Glass icon."
    exit 0
fi

if [ ! -d "$APP_PATH" ]; then
    echo "ERROR: app bundle not found at $APP_PATH"
    exit 1
fi

TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

actool "$ICON_SOURCE" --compile "$TMP_DIR" \
    --output-format human-readable-text --notices --warnings --errors \
    --output-partial-info-plist "$TMP_DIR/generated-info.plist" \
    --app-icon "$ICON_ASSET_NAME" --include-all-app-icons \
    --enable-on-demand-resources NO \
    --development-region en \
    --target-device mac \
    --minimum-deployment-target 26.0 \
    --platform macosx

cp "$TMP_DIR/Assets.car" "$APP_PATH/Contents/Resources/Assets.car"

INFO_PLIST="$APP_PATH/Contents/Info.plist"
if ! /usr/libexec/PlistBuddy -c "Print :CFBundleIconName" "$INFO_PLIST" >/dev/null 2>&1; then
    /usr/libexec/PlistBuddy -c "Add :CFBundleIconName string $ICON_ASSET_NAME" "$INFO_PLIST"
fi

echo "Liquid Glass icon added to $APP_PATH"