# NixOS module for G-Helper Linux.
#
# Provides the minimal system integration that the binary cannot
# do itself on NixOS (read-only /etc): nix-ld for foreign binary
# support, udev rules for sysfs permissions, kernel modules, and
# a Nix-native gpu-helper for privileged GPU operations.
#
# Usage in configuration.nix or flake:
#   imports = [ ./path/to/nixos/module.nix ];
#   services.ghelper.enable = true;
{ config, lib, pkgs, ... }:

let
  cfg = config.services.ghelper;
  packages = pkgs.callPackage ./package.nix {};

  # Udev rules from install/90-ghelper.rules with /bin/chmod
  # replaced by the Nix store coreutils path (NixOS has no /bin/chmod).
  udevRulesText = builtins.replaceStrings
    [ "/bin/chmod" "/bin/sh" "VERSION_PLACEHOLDER" ]
    [ "${pkgs.coreutils}/bin/chmod" "${pkgs.runtimeShell}" packages.ghelper.version ]
    (builtins.readFile ../install/90-ghelper.rules);

  udevRules = pkgs.writeTextFile {
    name = "90-ghelper.rules";
    destination = "/etc/udev/rules.d/90-ghelper.rules";
    text = udevRulesText;
  };

in {
  options.services.ghelper = {
    enable = lib.mkEnableOption "G-Helper Linux laptop control utility";

    gpuBootService = lib.mkOption {
      type = lib.types.bool;
      default = false;
      description = ''
        Enable the GPU boot service that applies pending GPU mode
        changes (Eco/Standard) early in boot before the display
        manager starts. Only useful on laptops with a discrete GPU.
      '';
    };

    user = lib.mkOption {
      type = lib.types.str;
      default = "";
      description = ''
        Username to grant input device access for the keyboard remapper.
        The user is added to the "input" group so g-helper can read
        /dev/input/event* devices. Leave empty to skip.
      '';
    };

    gpuEcoAtBoot = lib.mkOption {
      type = lib.types.bool;
      default = false;
      description = ''
        Persistent Eco mode for the PCI GPU backend: soft-blacklist the
        NVIDIA driver stack so it does not autoload at boot, keeping the
        dGPU's driver unloaded by default for battery life.

        This is the declarative NixOS replacement for the runtime udev
        hot-remove rule the app cannot persist (/etc/udev/rules.d is a
        read-only store symlink on NixOS).

        It is a SOFT blacklist: the app's live "Standard" switch can still
        `modprobe nvidia` within a session; the next boot returns to Eco.

        Scope: prevents driver autoload. Full device power-off still relies
        on the app's runtime PCI-remove. Leave off if you use
        `hardware.nvidia` (PRIME/offload), which loads the driver explicitly.
      '';
    };
  };

  config = lib.mkIf cfg.enable {
    # nix-ld: makes the /lib64/ld-linux stub functional so the
    # AOT binary and its runtime-extracted .so files can load.
    programs.nix-ld.enable = true;

    # Packages on PATH. ryzenadj comes from nixpkgs; the app resolves it
    # from PATH on NixOS instead of the bundled /opt binary.
    environment.systemPackages = [
      packages.ghelper
      packages.gpu-helper
      packages.gpu-block-helper
      pkgs.ryzenadj
    ];

    # Udev rules for sysfs node permissions (battery, backlight,
    # fan curves, thermal policy, GPU switching, hidraw access).
    services.udev.packages = [ udevRules ];

    # Input group for keyboard remapper access to /dev/input/event*.
    users.groups.input = {};
    users.users = lib.mkIf (cfg.user != "") {
      ${cfg.user}.extraGroups = [ "input" ];
    };

    # Kernel modules needed by the app
    boot.kernelModules = [
      "uinput"    # NumberPad virtual keyboard
      "i2c-dev"   # NumberPad LED control
    ];

    # Passwordless sudo for the helper binaries.
    # gpu-helper validates subcommands against an internal whitelist.
    # ryzenadj only touches the AMD SMU (power/temp/current limits).
    security.sudo.extraRules = [
      {
        groups = [ "wheel" ];
        commands = [
          {
            command = "${packages.gpu-helper}/bin/gpu-helper";
            options = [ "NOPASSWD" ];
          }
          {
            command = "${packages.gpu-block-helper}/bin/gpu-block-helper.sh";
            options = [ "NOPASSWD" ];
          }
          {
            command = "${pkgs.ryzenadj}/bin/ryzenadj";
            options = [ "NOPASSWD" ];
          }
        ];
      }
    ];

    # GPU state directory (boot service reads/writes pending mode here)
    systemd.tmpfiles.rules = [
      "d /etc/ghelper 0755 root root -"
    ];

    # Persistent PCI Eco: soft-blacklist the NVIDIA stack at boot. Declarative
    # replacement for the runtime udev hot-remove rule that can't persist on
    # NixOS. Soft (no install-false) so live "Standard" can still modprobe it.
    boot.extraModprobeConfig = lib.mkIf cfg.gpuEcoAtBoot ''
      blacklist nvidia
      blacklist nvidia_modeset
      blacklist nvidia_uvm
      blacklist nvidia_drm
    '';

    # Optional early-boot GPU mode service. Applies the pending GPU mode
    # before the display manager. On ASUS it writes firmware dgpu_disable
    # (sysfs, works); the PCI modprobe blacklist also works. The udev
    # hot-remove rule it tries to write lands in the read-only /etc/udev
    # (best-effort, ignored) - live PCI switching still works at runtime.
    systemd.services.ghelper-gpu-boot = lib.mkIf cfg.gpuBootService {
      description = "G-Helper GPU Mode Boot Application";
      wantedBy = [ "multi-user.target" ];
      before = [ "display-manager.service" "multi-user.target" ];
      after = [ "systemd-modules-load.service" "systemd-udevd.service" ];
      # Script calls gpu-helper, modprobe, udevadm, systemctl by bare name.
      path = [ packages.gpu-helper pkgs.kmod pkgs.systemd ];
      serviceConfig = {
        Type = "oneshot";
        ExecStart = "${packages.ghelper-gpu-boot}/bin/ghelper-gpu-boot.sh";
        RemainAfterExit = false;
        TimeoutStartSec = 60;
      };
    };
  };
}
