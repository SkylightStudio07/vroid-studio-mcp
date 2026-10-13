#!/usr/bin/env bash
# Assemble a vroid-studio-mcp release archive.
#
#   tools/package-release.sh [version]
#
# With no argument the version is read from [BepInPlugin] in plugin/VroidStudioMcp/Plugin.cs.
# Output: dist/vroid-studio-mcp-<version>.zip
#
# The archive ships only the plugin and our own data/docs; the app and BepInEx are prerequisites
# and are never bundled. See README.md ("Installation" / "Release contents").
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo"

version="${1:-}"
if [[ -z "$version" ]]; then
    version="$(grep -oP 'BepInPlugin\("[^"]+",\s*"[^"]+",\s*"\K[^"]+' plugin/VroidStudioMcp/Plugin.cs)"
fi
if [[ -z "$version" ]]; then
    echo "could not determine version (pass it as an argument)" >&2
    exit 1
fi

config="${GAME_DIR:-run/game}"
echo "building VroidStudioMcp $version (GameDir=$config)"
dotnet build plugin/VroidStudioMcp/VroidStudioMcp.csproj -c Release -p:GameDir="$repo/$config"

stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT

mkdir -p "$stage/BepInEx/plugins" "$stage/BepInEx/config" "$stage/i18n" "$stage/labels"
cp plugin/VroidStudioMcp/bin/Release/VroidStudioMcp.dll "$stage/BepInEx/plugins/"
cp config/BepInEx.cfg "$stage/BepInEx/config/"
cp data/i18n/*.json "$stage/i18n/"
cp data/labels/*.json "$stage/labels/"
cp README.md README.ru.md LICENSE "$stage/"

mkdir -p dist
out="$repo/dist/vroid-studio-mcp-$version.zip"
rm -f "$out"
( cd "$stage" && zip -qr "$out" . )
echo "wrote ${out#"$repo/"}"
