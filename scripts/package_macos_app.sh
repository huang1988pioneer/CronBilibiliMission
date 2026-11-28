#!/bin/sh
set -eu

# Assemble the .NET single-file publish output into a Finder-launchable app.
published=${1:?expected publish directory}
bundle=${2:?expected output .app path}
version=${3:?expected application version}
root=$(cd "$(dirname "$0")/.." && pwd)
test -f "$published/BilibiliCookieReader"
test ! -e "$bundle"
case "$bundle" in *.app) ;; *) exit 1 ;; esac

mkdir -p "$bundle/Contents/Resources"
/usr/bin/ditto --norsrc --noextattr "$published" "$bundle/Contents/MacOS"
chmod 755 "$bundle/Contents/MacOS/BilibiliCookieReader"
cp "$root/BilibiliCookieReader/Assets/BilibiliCookieReader.icns" "$bundle/Contents/Resources/"

python3 - "$bundle/Contents/Info.plist" "$version" <<'PY'
import plistlib
import sys

with open(sys.argv[1], "wb") as file:
    plistlib.dump({
        "CFBundlePackageType": "APPL",
        "CFBundleIdentifier": "io.github.huang1988pioneer.bilibilicookiereader",
        "CFBundleName": "BilibiliCookieReader",
        "CFBundleDisplayName": "Bilibili Cookie 讀取器",
        "CFBundleExecutable": "BilibiliCookieReader",
        "CFBundleIconFile": "BilibiliCookieReader.icns",
        "CFBundleShortVersionString": sys.argv[2],
        "CFBundleVersion": sys.argv[2],
        "NSHighResolutionCapable": True,
    }, file)
PY

/usr/bin/codesign --force --sign - --timestamp=none "$bundle"
/usr/bin/codesign --verify --strict "$bundle"
