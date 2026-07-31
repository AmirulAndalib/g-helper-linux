using System.Diagnostics;

namespace GHelper.Linux.Platform.Linux;

/// <summary>
/// NixOS-specific detection and path resolution. All NixOS runtime
/// logic lives here; other code calls into this class.
/// </summary>
public static class NixOS
{
    private static bool? _detected;

    /// <summary>True when running on NixOS (/etc/NIXOS marker).</summary>
    public static bool IsNixOS => _detected ??= File.Exists("/etc/NIXOS");

    /// <summary>The udev rules file the module ships (store symlink under /etc).</summary>
    public const string UdevRulePath = "/etc/udev/rules.d/90-ghelper.rules";

    /// <summary>The .desktop entry the package installs into the system profile,
    /// or null if absent.</summary>
    public static string? DesktopFilePath() => SystemFilePath("share/applications/ghelper.desktop");

    /// <summary>The app icon the package installs into the system profile,
    /// or null if absent.</summary>
    public static string? IconFilePath() => SystemFilePath("share/icons/hicolor/256x256/apps/ghelper.png");

    /// <summary>Stable launcher path for relaunch - the /run/current-system
    /// symlink is repointed by each rebuild, so it tracks the latest generation.</summary>
    public static string LauncherPath => ResolveOnPath("ghelper") ?? "/run/current-system/sw/bin/ghelper";

    /// <summary>Absolute bash path (pkexec sanitizes PATH, so a bare name fails).</summary>
    public static string ResolveBash() => ResolveOnPath("bash") ?? "/run/current-system/sw/bin/bash";

    /// <summary>
    /// Re-run the installer's NixOS branch as root (fetch latest release +
    /// nixos-rebuild). The in-place binary replace can't work on the read-only
    /// /nix/store, so updates go through the declarative installer. Downloads
    /// the installer to a temp file and runs it via pkexec. UI-free; returns
    /// (ok, log) for the caller to surface.
    /// </summary>
    public static async Task<(bool ok, string log)> RunModuleUpdate(string installScriptUrl, string userAgent)
    {
        string script = Path.Combine(Path.GetTempPath(), $"ghelper-install-{Guid.NewGuid():N}.sh");
        try
        {
            using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
            {
                http.DefaultRequestHeaders.Add("User-Agent", userAgent);
                await File.WriteAllTextAsync(script, await http.GetStringAsync(installScriptUrl));
            }

            var psi = new ProcessStartInfo
            {
                FileName = "pkexec",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add(ResolveBash());
            psi.ArgumentList.Add(script);

            using var p = Process.Start(psi);
            if (p == null)
                return (false, "pkexec failed to start");
            string outp = await p.StandardOutput.ReadToEndAsync();
            string errp = await p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            return p.ExitCode == 0 ? (true, outp) : (false, $"exit={p.ExitCode}; {errp.Trim()}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
        finally
        {
            try
            { File.Delete(script); }
            catch { }
        }
    }

    /// <summary>
    /// Resolve gpu-helper path on NixOS. The nixos/ module puts a
    /// Nix-native gpu-helper on PATH. Returns null if not found.
    /// </summary>
    public static string? ResolveGpuHelper()
    {
        if (!IsNixOS)
            return null;
        return WhichCached("gpu-helper", ref _gpuHelper);
    }

    /// <summary>
    /// Resolve gpu-block-helper.sh path on NixOS. The nixos/ module
    /// puts it on PATH. Returns null if not found.
    /// </summary>
    public static string? ResolveGpuBlockHelper()
    {
        if (!IsNixOS)
            return null;
        return WhichCached("gpu-block-helper.sh", ref _gpuBlockHelper);
    }

    /// <summary>
    /// Resolve ryzenadj path on NixOS. The nixos/ module adds the
    /// nixpkgs ryzenadj to PATH. Returns null if not found.
    /// </summary>
    public static string? ResolveRyzenadj()
    {
        if (!IsNixOS)
            return null;
        return WhichCached("ryzenadj", ref _ryzenadj);
    }

    /// <summary>
    /// Stable launcher exec for .desktop files on NixOS. Nix store
    /// paths change on every rebuild so autostart entries that embed
    /// them break. Return "ghelper" (on PATH via the module) instead.
    /// </summary>
    public static string? StableLauncherExec()
    {
        if (!IsNixOS)
            return null;
        return "ghelper";
    }

    /// <summary>
    /// True when the self-install prompt should be skipped. On NixOS
    /// the /etc paths are read-only store symlinks; system files are
    /// managed declaratively by the nixos/ module.
    /// </summary>
    public static bool SkipSelfInstall => IsNixOS;

    /// <summary>
    /// True when the udev-not-installed warning should be suppressed.
    /// The nixos/ module provides udev rules via services.udev.packages.
    /// </summary>
    public static bool SkipUdevWarning => IsNixOS;

    /// <summary>
    /// True when the Installer integrity panel should report root-owned
    /// managed files as NotApplicable (managed by the nix module, not
    /// by the binary's self-install).
    /// </summary>
    public static bool ManagedByModule => IsNixOS;

    /// <summary>
    /// Resolve a command to its PATH location (symlink NOT followed - returns
    /// e.g. /run/current-system/sw/bin/X, which stays valid across rebuilds).
    /// Returns null if not found.
    /// </summary>
    public static string? ResolveOnPath(string name)
    {
        var which = SysfsHelper.RunCommandWithTimeout("which", name, 2000);
        if (!string.IsNullOrWhiteSpace(which))
        {
            var resolved = which.Trim();
            if (File.Exists(resolved))
                return resolved;
        }
        return null;
    }

    /// <summary>
    /// Resolve a path through all symlinks to its final target. On NixOS,
    /// PATH entries like /run/current-system/sw/bin/X are symlinks into the
    /// /nix/store; the sudoers rule references the store path, so matching
    /// requires the resolved target. Returns the input on failure.
    /// </summary>
    public static string? RealPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return null;
        try
        {
            var fi = new FileInfo(path);
            return fi.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path;
        }
        catch
        {
            return path;
        }
    }

    /// <summary>True when the kernel modules the app needs (uinput, i2c-dev)
    /// are loaded. The nixos/ module loads them via boot.kernelModules.</summary>
    public static bool KernelModulesLoaded()
        => Directory.Exists("/sys/module/uinput")
           && (Directory.Exists("/sys/module/i2c_dev") || Directory.Exists("/sys/module/i2c-dev"));

    /// <summary>
    /// Resolve a file under any entry of the system profile / XDG data dirs
    /// (where the nix package's share/ tree is linked). Returns the first
    /// existing path, or null. Used to locate the .desktop entry and icon
    /// shipped by the package.
    /// </summary>
    public static string? SystemFilePath(string relativePath)
    {
        foreach (var root in SystemDataRoots())
        {
            var p = Path.Combine(root, relativePath);
            if (File.Exists(p))
                return p;
        }
        return null;
    }

    private static IEnumerable<string> SystemDataRoots()
    {
        // The current system profile (where systemPackages land)
        yield return "/run/current-system/sw";

        // XDG_DATA_DIRS entries minus the trailing /share (we append it)
        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_DIRS");
        if (!string.IsNullOrEmpty(xdg))
        {
            foreach (var dir in xdg.Split(':', StringSplitOptions.RemoveEmptyEntries))
            {
                // dir is like /run/current-system/sw/share; strip trailing /share
                var trimmed = dir.TrimEnd('/');
                if (trimmed.EndsWith("/share", StringComparison.Ordinal))
                    yield return trimmed[..^"/share".Length];
            }
        }
    }

    // Cached PATH lookups
    private static string? _gpuHelper;
    private static string? _gpuBlockHelper;
    private static string? _ryzenadj;
    private static bool _gpuHelperResolved;
    private static bool _gpuBlockHelperResolved;
    private static bool _ryzenadjResolved;

    private static string? WhichCached(string name, ref string? cache)
    {
        // Use a separate resolved flag per field to distinguish
        // "not yet looked up" from "looked up but not found".
        if (name == "gpu-helper" && _gpuHelperResolved)
            return cache;
        if (name == "gpu-block-helper.sh" && _gpuBlockHelperResolved)
            return cache;
        if (name == "ryzenadj" && _ryzenadjResolved)
            return cache;

        var which = SysfsHelper.RunCommandWithTimeout("which", name, 2000);
        string? result = null;
        if (!string.IsNullOrWhiteSpace(which))
        {
            var resolved = which.Trim();
            if (File.Exists(resolved))
                // Resolve through the /run/current-system/sw/bin symlink to the
                // real /nix/store path. The module's NOPASSWD sudoers rule grants
                // access to that store path; sudo does not match across symlinks,
                // so invoking the symlink would require a password.
                result = RealPath(resolved) ?? resolved;
        }

        cache = result;
        if (name == "gpu-helper")
            _gpuHelperResolved = true;
        if (name == "gpu-block-helper.sh")
            _gpuBlockHelperResolved = true;
        if (name == "ryzenadj")
            _ryzenadjResolved = true;
        return result;
    }
}
