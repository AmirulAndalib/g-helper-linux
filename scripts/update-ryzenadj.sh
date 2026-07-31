#!/usr/bin/env bash
# Updates the vendored ryzenadj CLI binary from the latest RyzenAdj GitHub
# release. The static musl build has no runtime dependencies, so one binary
# works on every distro. The binary lives in git at vendor/ryzenadj/ and is
# embedded into ghelper at build time; run this script to bump the version.
set -euo pipefail

REPO="FlyGoat/RyzenAdj"
ASSET="ryzenadj-linux-musl-static-x86_64.tar.gz"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEST_DIR="$SCRIPT_DIR/../vendor/ryzenadj"

api="https://api.github.com/repos/$REPO/releases/latest"
echo "Querying $api ..."
json="$(curl -fsSL "$api")"

tag="$(printf '%s' "$json" | python3 -c 'import json,sys; print(json.load(sys.stdin)["tag_name"])')"
url="$(printf '%s' "$json" | python3 -c '
import json, sys
release = json.load(sys.stdin)
for asset in release["assets"]:
    if asset["name"] == "'"$ASSET"'":
        print(asset["browser_download_url"])
        break
else:
    sys.exit("asset not found in release: '"$ASSET"'")
')"

current=""
[[ -f "$DEST_DIR/VERSION" ]] && current="$(cat "$DEST_DIR/VERSION")"
echo "Latest release: $tag (vendored: ${current:-none})"

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

echo "Downloading $url ..."
curl -fsSL "$url" -o "$tmp/$ASSET"
tar -xzf "$tmp/$ASSET" -C "$tmp"

bin="$(find "$tmp" -type f -name ryzenadj | head -1)"
[[ -n "$bin" ]] || { echo "ERROR: ryzenadj binary not found in $ASSET" >&2; exit 1; }
license="$(find "$tmp" -type f -name LICENSE | head -1)"

mkdir -p "$DEST_DIR"
install -m755 "$bin" "$DEST_DIR/ryzenadj"
[[ -n "$license" ]] && install -m644 "$license" "$DEST_DIR/LICENSE"
printf '%s\n' "$tag" > "$DEST_DIR/VERSION"

echo "Vendored: $DEST_DIR/ryzenadj"
echo "Version:  $tag"
echo "Size:     $(du -h "$DEST_DIR/ryzenadj" | cut -f1)"
echo "SHA256:   $(sha256sum "$DEST_DIR/ryzenadj" | cut -d' ' -f1)"
