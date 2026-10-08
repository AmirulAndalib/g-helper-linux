#!/usr/bin/env python3
"""Regenerate nixos/deps.json (the NuGet lockfile for the Nix build).

Run this after changing PackageReference versions in src/*.csproj.
Uses buildDotnetModule's passthru.fetch-deps, which does a networked
dotnet restore and rewrites deps.json in place.
"""
import json
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

NIXOS_DIR = Path(__file__).resolve().parent.parent / "nixos"
PACKAGE_NIX = NIXOS_DIR / "package.nix"

# fetch-deps only exists when nugetDeps is a plain path, so temporarily
# bypass the SDK-fallback filter in package.nix.
FILTER_BLOCK = re.compile(r"nugetDeps =\n      let.*?deps\.json\)\)+;", re.S)

EVAL_EXPR = """
let
  pkgs = import (builtins.getFlake (toString ./.)).inputs.nixpkgs
    { system = "x86_64-linux"; };
in (pkgs.callPackage ./package.nix { }).ghelper.passthru.fetch-deps
"""


def main():
    original = PACKAGE_NIX.read_text()
    stripped, n = FILTER_BLOCK.subn("nugetDeps = ./deps.json;", original)
    if n != 1:
        sys.exit("error: could not find nugetDeps filter block in package.nix")

    tmpdir = Path(tempfile.mkdtemp(prefix="update-nix-deps-"))
    try:
        PACKAGE_NIX.write_text(stripped)
        subprocess.run(
            ["nix", "build", "--impure", "--expr", EVAL_EXPR,
             "-o", str(tmpdir / "fetch-deps")],
            cwd=NIXOS_DIR, check=True)
        subprocess.run([str(tmpdir / "fetch-deps")], cwd=NIXOS_DIR, check=True)
    finally:
        PACKAGE_NIX.write_text(original)
        shutil.rmtree(tmpdir, ignore_errors=True)

    count = len(json.loads((NIXOS_DIR / "deps.json").read_text()))
    print(f"deps.json regenerated ({count} entries)")
    print("now run: nix build ./nixos#ghelper")


if __name__ == "__main__":
    main()
