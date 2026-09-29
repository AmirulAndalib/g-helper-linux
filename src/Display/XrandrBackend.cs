using System.Globalization;
using System.Text.RegularExpressions;
using GHelper.Linux.Platform.Linux;

namespace GHelper.Linux.Display;

/// <summary>
/// Display backend using xrandr (X11).
/// Works on all X11 sessions and XWayland on some Wayland compositors.
/// </summary>
public class XrandrBackend : IDisplayBackend
{
    public string Name => "xrandr";
    public bool SupportsGamma => true;

    public int GetRefreshRate()
    {
        var output = SysfsHelper.RunCommand("xrandr", "--current --prop");
        if (output == null)
            return -1;
        var displayName = SelectOutput(output);
        return displayName == null ? -1 : ParseRefreshRate(output, displayName);
    }

    internal static int ParseRefreshRate(string output, string displayName)
    {
        bool selected = false;
        foreach (var line in output.Split('\n'))
        {
            if (line.Length > 0 && !char.IsWhiteSpace(line[0]))
            {
                selected = line.StartsWith(displayName + " connected", StringComparison.Ordinal);
                continue;
            }
            if (!selected)
                continue;
            var match = Regex.Match(line, @"(\d+(?:\.\d+)?)\*");
            if (match.Success && double.TryParse(match.Groups[1].Value,
                CultureInfo.InvariantCulture, out double hz))
                return (int)Math.Round(hz);
        }

        return -1;
    }

    public List<int> GetAvailableRefreshRates()
    {
        var rates = new List<int>();
        var output = SysfsHelper.RunCommand("xrandr", "--current --prop");
        if (output == null)
            return rates;
        var displayName = SelectOutput(output);
        if (displayName == null)
            return rates;

        bool foundDisplay = false;
        foreach (var line in output.Split('\n'))
        {
            if (line.StartsWith(displayName + " connected", StringComparison.Ordinal))
            {
                foundDisplay = true;
                continue;
            }

            if (foundDisplay)
            {
                if (!line.StartsWith("   ") && !line.StartsWith("\t"))
                {
                    if (line.Length > 0 && !char.IsWhiteSpace(line[0]))
                        break;
                }

                // Properties (EDID, gamma, ranges) also contain numbers.
                // Only parse refresh tokens following a mode name. Each rate
                // needs a leading separator so non-matching lines fail in
                // linear time (no nested empty-capable quantifier).
                var mode = Regex.Match(line, @"^ {3}\S+((?:[ *+]+\d+(?:\.\d+)?)+)[ *+]*$");
                if (!mode.Success)
                    continue;
                var matches = Regex.Matches(mode.Groups[1].Value, @"\d+(?:\.\d+)?");
                foreach (Match match in matches)
                {
                    if (double.TryParse(match.Value,
                        CultureInfo.InvariantCulture, out double hz))
                    {
                        int intHz = (int)Math.Round(hz);
                        if (!rates.Contains(intHz) && intHz > 0)
                            rates.Add(intHz);
                    }
                }
            }
        }

        rates.Sort();
        rates.Reverse();
        return rates;
    }

    public void SetRefreshRate(int hz)
    {
        var output = SysfsHelper.RunCommand("xrandr", "--current --prop");
        if (output == null)
        {
            Helpers.Logger.WriteLine("Xrandr.SetRefreshRate: xrandr query returned null");
            return;
        }
        var displayName = SelectOutput(output);
        if (displayName == null)
        {
            Helpers.Logger.WriteLine("Xrandr.SetRefreshRate: no primary output found");
            return;
        }

        Helpers.Logger.WriteLine($"Xrandr.SetRefreshRate: requesting {hz}Hz on {displayName}");

        string? currentResolution = null;
        bool foundDisplay = false;

        foreach (var line in output.Split('\n'))
        {
            if (line.StartsWith(displayName + " connected", StringComparison.Ordinal))
            {
                foundDisplay = true;
                continue;
            }

            if (foundDisplay)
            {
                if (line.Length > 0 && !char.IsWhiteSpace(line[0]))
                    break;

                if (line.Contains('*') && currentResolution == null)
                {
                    var resMatch = Regex.Match(line.Trim(), @"(\d+x\d+)");
                    if (resMatch.Success)
                        currentResolution = resMatch.Groups[1].Value;
                }
            }
        }

        if (currentResolution == null)
        {
            Helpers.Logger.WriteLine($"Xrandr.SetRefreshRate: could not determine current resolution for {displayName}");
            return;
        }

        SysfsHelper.RunCommand("xrandr",
            $"--output {displayName} --mode {currentResolution} --rate {hz}");

        Helpers.Logger.WriteLine($"SetRefreshRate (xrandr): {displayName} -> {currentResolution}@{hz}Hz");
    }

    public void SetGamma(float r, float g, float b)
    {
        var displayName = GetPrimaryOutput();
        if (displayName == null)
            return;

        r = Math.Clamp(r, 0.1f, 5.0f);
        g = Math.Clamp(g, 0.1f, 5.0f);
        b = Math.Clamp(b, 0.1f, 5.0f);

        var gammaStr = string.Format(CultureInfo.InvariantCulture,
            "{0:F2}:{1:F2}:{2:F2}", r, g, b);

        SysfsHelper.RunCommand("xrandr",
            $"--output {displayName} --gamma {gammaStr}");

        Helpers.Logger.WriteLine($"SetGamma (xrandr): {displayName} -> {gammaStr}");
    }

    public string? GetDisplayName()
    {
        return GetPrimaryOutput();
    }

    // Probing

    /// <summary>
    /// Test if xrandr is available and returns output.
    /// Returns true if xrandr can query the display.
    /// </summary>
    public static bool Probe()
    {
        var output = SysfsHelper.RunCommand("xrandr", "--query");
        return output != null && output.Contains(" connected");
    }

    // Internal helpers

    /// <summary>
    /// Get the primary/laptop display output name from xrandr.
    /// Priority: connected explicit panel > eDP/LVDS > first active output
    /// > first connected output. Keep inactive panels selected so automatic
    /// refresh changes do not target an external display when the lid is closed.
    /// </summary>
    internal static string? GetPrimaryOutput()
    {
        var output = SysfsHelper.RunCommand("xrandr", "--current --prop");
        if (output == null)
            return null;

        return SelectOutput(output);
    }

    internal static string? SelectOutput(string output)
    {
        string? panel = null;
        string? namedPanel = null;
        string? firstConnected = null;
        string? firstActive = null;
        foreach (string block in Regex.Split(output, @"(?=^\S+ (?:dis)?connected\b)", RegexOptions.Multiline))
        {
            string header = block.Split('\n')[0];
            if (!Regex.IsMatch(header, @"^\S+ connected\b"))
                continue;
            string name = header.Split(' ')[0];
            firstConnected ??= name;
            if (Regex.IsMatch(header, @"\b\d+x\d+[+-]\d+[+-]\d+"))
                firstActive ??= name;
            if (Regex.IsMatch(block, @"^\s+ConnectorType:\s+Panel\s*$", RegexOptions.Multiline))
                panel ??= name;
            if (name.StartsWith("eDP", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("LVDS", StringComparison.OrdinalIgnoreCase))
                namedPanel ??= name;
        }
        return panel ?? namedPanel ?? firstActive ?? firstConnected;
    }
}
