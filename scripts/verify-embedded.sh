#!/usr/bin/env bash
# Fail if a built ghelper is missing any embedded resource (csproj LogicalName).
# Usage: scripts/verify-embedded.sh [path/to/ghelper]   (default: dist/ghelper)
set -euo pipefail

BIN="$(realpath "${1:-$(dirname "$0")/../dist/ghelper}")"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

EXPECTED=(
  gpu-helper
  ryzenadj
  gpu-block-helper.sh
  ghelper-gpu-boot.sh
  90-ghelper.rules
  libSkiaSharp.so
  libHarfBuzzSharp.so
  ghelper-audio
  wlr-randr
)
for res in "${EXPECTED[@]}"; do
  out="$TMP/$res"
  if ! "$BIN" --extract-helper "$res" "$out" >/dev/null 2>&1; then
    echo "ERROR: embedded resource '$res' not found in binary"
    exit 1
  fi
  size=$(stat -c%s "$out")
  if (( size < 100 )); then
    echo "ERROR: embedded resource '$res' is suspiciously small ($size bytes)"
    exit 1
  fi
  echo "  ok: $res ($size bytes)"
done
# Helper binaries must be valid ELF executables (not placeholders).
for bin in gpu-helper ryzenadj; do
  if ! file "$TMP/$bin" | grep -q "ELF.*executable"; then
    echo "ERROR: ${bin} is not a valid ELF executable"
    exit 1
  fi
done
echo "All embedded resources verified"
