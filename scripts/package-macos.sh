#!/usr/bin/env bash
# Local macOS arm64 build of the CI package recipe (.github/workflows/build.yml, osx-arm64) plus local extras:
# a locally built app never carries the quarantine flag, so it opens without any Gatekeeper dialog.
set -euo pipefail
cd "$(dirname "$0")/.."

rid=osx-arm64

# Same MinVer query as CI; needs -restore and the repository's git tags.
version=$(dotnet msbuild src/Homestead.Client/Homestead.Client.csproj -restore -t:MinVer -getProperty:MinVerVersion -p:Configuration=Release)
name="monoSettlers-$version-$rid"
echo "Version: $version"

rm -rf "publish/$name" "publish/$name.zip"

dotnet publish src/Homestead.Client/Homestead.Client.csproj -c Release -r "$rid" --self-contained -o "publish/$name"

pushd publish
app="monoSettlers.app"
# monoSettlers.app with the published folder as Contents/MacOS (the program finds data/ next to itself).
mkdir -p "$app/Contents/Resources"
mv "$name" "$app/Contents/MacOS"
# macOS wants plain numbers: without a pre-release suffix.
sed "s/VERSION/${version%%-*}/g" ../packaging/macos/Info.plist > "$app/Contents/Info.plist"
mkdir icon.iconset
sips -s format png ../src/Homestead.Client/Icon.bmp --out icon.iconset/icon_256x256.png
iconutil -c icns icon.iconset -o "$app/Contents/Resources/monoSettlers.icns"
rm -r icon.iconset
# Strip attributes BEFORE signing so the verified signature is the final bundle state (belt and braces:
# locally built files carry no quarantine flag, but the zip may travel through Downloads later).
xattr -cr "$app"
codesign --force --deep --sign - "$app"
codesign --verify --deep "$app"
mkdir "$name"
mv "$app" "$name/"
cp ../LICENSE.md ../THIRD_PARTY_NOTICES.md ../README.md "$name/"
xattr -cr "$name"
# ditto keeps the bundle's attributes and signature, unlike zip.
ditto -c -k --keepParent "$name" "$name.zip"
popd

echo "App: $PWD/publish/$name/monoSettlers.app"
echo "Zip: $PWD/publish/$name.zip"
echo "Open with: open $PWD/publish/$name/monoSettlers.app (no Gatekeeper dialog: a local build is never quarantined)"
