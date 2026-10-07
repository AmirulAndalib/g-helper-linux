# Overlay for building ghelper with current nixpkgs.
#
# SkiaSharp.NativeAssets.Linux >= 4.148.0 ships a libSkiaSharp.so that links
# against libstdc++.so.6, which nixpkgs' fetch-nupkg override (fontconfig
# only) does not provide, so its autoPatchelf step fails during the NuGet
# deps fetch. Give just that package's fetch derivation stdenv.cc.cc.lib;
# all other packages and their derivations stay untouched.
final: prev: {
  dotnetCorePackages = prev.dotnetCorePackages.overrideScope (self: super: {
    fetchNupkg = args @ { pname, ... }:
      if pname == "SkiaSharp.NativeAssets.Linux" then
        final.lib.overrideDerivation (super.fetchNupkg args) (old: {
          buildInputs = old.buildInputs or [ ] ++ [ final.stdenv.cc.cc.lib ];
        })
      else
        super.fetchNupkg args;
  });
}
