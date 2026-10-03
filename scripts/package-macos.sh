#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "macOS packaging must run on macOS." >&2
  exit 1
fi

arch="$(uname -m)"
self_contained="false"
for arg in "$@"; do
  case "$arg" in
    arm64|x64|x86_64) arch="$arg" ;;
    --self-contained) self_contained="true" ;;
    *) echo "Usage: $0 [arm64|x64] [--self-contained]" >&2; exit 1 ;;
  esac
done
case "$arch" in
  arm64) rid="osx-arm64" ;;
  x64|x86_64) rid="osx-x64" ;;
  *) echo "Use arm64 or x64." >&2; exit 1 ;;
esac

version="$(dotnet msbuild "$repo_dir/EtchForge.csproj" -getProperty:Version -nologo | tail -n 1)"
if [[ ! "$version" =~ ^[0-9]+(\.[0-9]+){1,3}$ ]]; then
  echo "Invalid app version: $version" >&2
  exit 1
fi

output_dir="$repo_dir/dist/$rid"
mkdir -p "$output_dir"
staging_dir="$(mktemp -d "$output_dir/.etchforge-package.XXXXXX")"
trap 'rm -rf "$staging_dir"' EXIT
publish_dir="$staging_dir/publish"
app_dir="$staging_dir/EtchForge.app"

dotnet publish "$repo_dir/EtchForge.csproj" -c Release -r "$rid" \
  --self-contained "$self_contained" -p:UseAppHost=true \
  -p:NuGetAudit=false -p:RestoreIgnoreFailedSources=true -o "$publish_dir"

mkdir -p "$app_dir/Contents/MacOS" "$app_dir/Contents/Resources"
ditto "$publish_dir" "$app_dir/Contents/MacOS"
cp "$repo_dir/Assets/etchforge.icns" "$app_dir/Contents/Resources/etchforge.icns"
sed "s/@VERSION@/$version/g" "$repo_dir/packaging/macos/Info.plist" > "$app_dir/Contents/Info.plist"
chmod +x "$app_dir/Contents/MacOS/EtchForge"
plutil -lint "$app_dir/Contents/Info.plist"
codesign --force --deep --sign - "$app_dir"

rm -rf "$output_dir/EtchForge.app"
mv "$app_dir" "$output_dir/EtchForge.app"
echo "$output_dir/EtchForge.app"
