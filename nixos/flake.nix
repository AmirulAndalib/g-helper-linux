# G-Helper Linux NixOS flake.
#
# Exposes the ghelper + gpu-helper packages and a NixOS module.
#
# Quick start:
#   1. Test package:   nix build .#ghelper   (builds from source, no build.sh needed)
#   2. In your flake:
#        inputs.ghelper.url = "path:./nixos";  # or github:utajum/g-helper-linux?dir=nixos
#        nixosConfigurations.myhost = nixpkgs.lib.nixosSystem {
#          modules = [
#            ghelper.nixosModules.default
#            { services.ghelper.enable = true; }
#          ];
#        };
{
  description = "G-Helper for Linux - ASUS/Lenovo laptop control";

  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";

  outputs = { self, nixpkgs }:
  let
    system = "x86_64-linux";
    pkgs = nixpkgs.legacyPackages.${system};
    packages = pkgs.callPackage ./package.nix {};
  in {
    packages.${system} = {
      ghelper = packages.ghelper;
      ghelper-audio = packages.ghelper-audio;
      wlr-randr = packages.wlr-randr;
      gpu-helper = packages.gpu-helper;
      gpu-block-helper = packages.gpu-block-helper;
      ghelper-gpu-boot = packages.ghelper-gpu-boot;
      default = packages.ghelper;
    };

    nixosModules.default = import ./module.nix;
  };
}
