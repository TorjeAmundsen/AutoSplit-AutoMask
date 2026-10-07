#!/usr/bin/env bash
# Builds AutoMask.app from an osx-arm64 and an osx-x64 publish, as one universal app.
# Usage: bundle.sh <arm64 publish dir> <x64 publish dir> <version> <output dir> [zip path]
# Needs a Mac: lipo, sips, iconutil, codesign and ditto come with macOS.
set -euo pipefail

arm64_dir=$1
x64_dir=$2
version=$3
output_dir=$4
zip_path=${5:-}

script_dir=$(cd "$(dirname "$0")" && pwd)
app="$output_dir/AutoMask.app"
macos="$app/Contents/MacOS"
resources="$app/Contents/Resources"

rm -rf "$app"
mkdir -p "$macos" "$resources"

# Every Mach-O file that differs between the two publishes (the executable) is merged into a
# universal one. The NuGet dylibs are universal already, so they're the same in both.
(cd "$arm64_dir" && find . -type f ! -name '*.pdb' ! -path '*.dSYM/*' ! -path './presets/*' ! -path './splits/*') |
while read -r file; do
    mkdir -p "$macos/$(dirname "$file")"
    if [[ -f "$x64_dir/$file" ]] && ! cmp -s "$arm64_dir/$file" "$x64_dir/$file" \
        && lipo -archs "$arm64_dir/$file" >/dev/null 2>&1; then
        lipo -create "$arm64_dir/$file" "$x64_dir/$file" -output "$macos/$file"
    else
        cp -p "$arm64_dir/$file" "$macos/$file"
    fi
done

# Copied to ~/Library/Application Support/AutoMask on first launch (Utils.GetDataDirectory)
cp -R "$arm64_dir/presets" "$arm64_dir/splits" "$resources/"

# CFBundleShortVersionString only allows numbers and dots, so 0.11.2-alpha becomes 0.11.2
sed "s/__VERSION__/${version%%-*}/g" "$script_dir/Info.plist" > "$app/Contents/Info.plist"

iconset=$(mktemp -d)/AutoMask.iconset
mkdir -p "$iconset"
icon="$script_dir/../AutoMask/Assets/icon800x800.png"
for size in 16 32 128 256 512; do
    sips -z $size $size "$icon" --out "$iconset/icon_${size}x${size}.png" >/dev/null
    sips -z $((size * 2)) $((size * 2)) "$icon" --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$resources/AutoMask.icns"
rm -rf "$(dirname "$iconset")"

# Ad-hoc signature: Apple Silicon refuses to run unsigned code, and lipo drops the signature
# the linker gave the executable. Not notarized, so Gatekeeper still blocks the first start.
codesign --force --sign - "$macos"/*.dylib
codesign --force --sign - "$app"

if [[ -n "$zip_path" ]]; then
    rm -f "$zip_path"
    ditto -c -k --keepParent "$app" "$zip_path"
    echo "  Zipped: $zip_path"
fi
echo "  Output: $app"
