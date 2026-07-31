using System;
using GHelper.Linux.Helpers;

namespace GHelper.Linux.Gpu.NVidia;

/// <summary>
/// Proactive pause gate for the app's own GPU telemetry. An nvidia-smi spawned
/// while the driver is being unbound/unloaded opens /dev/nvidia* and becomes a
/// holder itself; killed on its timeout mid-ioctl it can wedge in D-state and
/// block rmmod forever. GPU mode switches pause BEFORE touching the driver and
/// resume once the dGPU is back (or let the window expire after Eco).
/// </summary>
public static class GpuQueryGate
{
    private static DateTime _pausedUntilUtc = DateTime.MinValue;
    private static bool _held;

    public static bool IsPaused => _held || DateTime.UtcNow < _pausedUntilUtc;

    public static void Pause(TimeSpan duration, string reason)
    {
        _pausedUntilUtc = DateTime.UtcNow + duration;
        Logger.WriteLine($"GpuQueryGate: GPU queries paused for {duration.TotalSeconds:0}s ({reason})");
    }

    /// <summary>
    /// Pause with no expiry, for a dGPU that failed to re-initialise. Every
    /// query against it spawns an nvidia-smi that blocks until its timeout, so
    /// a timed pause just resumes the spam. Cleared by Resume() on the next
    /// successful mode switch.
    /// </summary>
    public static void Hold(string reason)
    {
        _held = true;
        Logger.WriteLine($"GpuQueryGate: GPU queries held ({reason})");
    }

    public static void Resume()
    {
        _held = false;
        _pausedUntilUtc = DateTime.MinValue;
        Logger.WriteLine("GpuQueryGate: GPU queries resumed");
    }
}
