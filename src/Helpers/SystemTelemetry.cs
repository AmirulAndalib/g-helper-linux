namespace GHelper.Linux.Helpers;

/// <summary>
/// Live CPU and memory counters for the hardware monitor.
///
/// Temperature, fan and GPU figures come from the ASUS WMI / NVML paths;
/// these three have no firmware source and are read straight from procfs.
/// </summary>
public static class SystemTelemetry
{
    // /proc/stat reports cumulative jiffies since boot, so usage is only
    // meaningful as a delta between two reads. The previous sample lives here.
    private static long _lastIdle;
    private static long _lastTotal;
    private static bool _haveSample;

    /// <summary>
    /// CPU busy percentage since the previous call, or -1 until a second
    /// sample exists. Call on a fixed interval; the value is the average
    /// across the gap, not an instantaneous reading.
    /// </summary>
    public static int GetCpuUsagePercent()
    {
        if (!ReadCpuTicks(out long idle, out long total))
            return -1;

        if (!_haveSample)
        {
            _lastIdle = idle;
            _lastTotal = total;
            _haveSample = true;
            return -1;
        }

        long dTotal = total - _lastTotal;
        long dIdle = idle - _lastIdle;
        _lastIdle = idle;
        _lastTotal = total;

        // Counters reset on suspend/resume on some kernels; treat as no data.
        if (dTotal <= 0 || dIdle < 0)
            return -1;

        double busy = (dTotal - dIdle) * 100.0 / dTotal;
        return (int)Math.Round(Math.Clamp(busy, 0.0, 100.0));
    }

    /// <summary>Reset the delta baseline, e.g. after a resume.</summary>
    public static void ResetCpuSample() => _haveSample = false;

    /// <summary>
    /// Aggregate jiffies from the summary "cpu" line. Fields after the label
    /// are user, nice, system, idle, iowait, ... - index 3 and 4 are the two
    /// idle kinds.
    /// </summary>
    private static bool ReadCpuTicks(out long idle, out long total)
    {
        idle = 0;
        total = 0;
        try
        {
            using var reader = new StreamReader("/proc/stat");
            var line = reader.ReadLine();
            if (line == null || !line.StartsWith("cpu ", StringComparison.Ordinal))
                return false;

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 1; i < parts.Length; i++)
            {
                if (!long.TryParse(parts[i], out long ticks))
                    continue;
                total += ticks;
                if (i == 4 || i == 5)
                    idle += ticks;
            }
            return total > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Used memory as a percentage of total, or -1 when unreadable.</summary>
    public static int GetRamUsagePercent()
    {
        try
        {
            long total = 0, available = 0;
            foreach (var line in File.ReadLines("/proc/meminfo"))
            {
                if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
                    total = ParseMemKb(line);
                else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
                    available = ParseMemKb(line);

                if (total > 0 && available > 0)
                    break;
            }

            if (total <= 0)
                return -1;

            double used = (total - available) * 100.0 / total;
            return (int)Math.Round(Math.Clamp(used, 0.0, 100.0));
        }
        catch
        {
            return -1;
        }
    }

    private static long ParseMemKb(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && long.TryParse(parts[1], out long kb) ? kb : 0;
    }

    /// <summary>
    /// Mean current core frequency in MHz, or -1. Averaged over every online
    /// core because per-core boost means cpu0 alone is not representative.
    /// Falls back to /proc/cpuinfo when cpufreq is absent.
    /// </summary>
    public static int GetCpuFrequencyMhz()
    {
        double total = 0;
        int count = 0;

        try
        {
            foreach (var dir in Directory.EnumerateDirectories("/sys/devices/system/cpu", "cpu*"))
            {
                // Skip the sibling "cpufreq" / "cpuidle" dirs; only cpuN counts.
                var name = Path.GetFileName(dir);
                if (name.Length <= 3)
                    continue;
                bool numbered = true;
                for (int i = 3; i < name.Length && numbered; i++)
                    numbered = char.IsAsciiDigit(name[i]);
                if (!numbered)
                    continue;

                var path = Path.Combine(dir, "cpufreq", "scaling_cur_freq");
                long khz = Platform.Linux.SysfsHelper.ReadInt(path, -1);
                if (khz > 0)
                {
                    total += khz / 1000.0;
                    count++;
                }
            }
        }
        catch { }

        if (count == 0)
        {
            try
            {
                foreach (var line in File.ReadLines("/proc/cpuinfo"))
                {
                    if (!line.StartsWith("cpu MHz", StringComparison.Ordinal))
                        continue;
                    int colon = line.IndexOf(':');
                    if (colon >= 0 && double.TryParse(line.AsSpan(colon + 1).Trim(),
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double mhz))
                    {
                        total += mhz;
                        count++;
                    }
                }
            }
            catch { }
        }

        return count > 0 ? (int)Math.Round(total / count) : -1;
    }
}
