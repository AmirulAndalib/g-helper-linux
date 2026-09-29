using GHelper.Linux.Display;

namespace GHelper.Linux.Tests;

public static class XrandrPanelTests
{
    internal const string NvidiaPanels = """
        Screen 0: minimum 8 x 8, current 5360 x 1440, maximum 32767 x 32767
        HDMI-0 connected primary 3440x1440+0+0 (normal) 797mm x 334mm
            ConnectorType: HDMI
           3440x1440     59.97 +  99.98*   49.99
        DP-0 connected 1920x1200+3440+240 (normal) 340mm x 220mm
            Backlight: 40
                range: (0, 100)
            ConnectorType: Panel
           1920x1200     60.00 + 144.00*
        eDP-1-1 disconnected (normal)
        """;

    public static void RunAll()
    {
        Harness.Scenario("NVIDIA panel wins over external primary", _ =>
            Harness.AssertEqual("DP-0", XrandrBackend.SelectOutput(NvidiaPanels), "panel"));
        Harness.Scenario("Refresh is scoped to the selected panel", _ =>
            Harness.AssertEqual(144, XrandrBackend.ParseRefreshRate(NvidiaPanels, "DP-0"), "panel Hz"));
        Harness.Scenario("External refresh stays distinct", _ =>
            Harness.AssertEqual(100, XrandrBackend.ParseRefreshRate(NvidiaPanels, "HDMI-0"), "external Hz"));
        Harness.Scenario("Missing display has no refresh", _ =>
            Harness.AssertEqual(-1, XrandrBackend.ParseRefreshRate(NvidiaPanels, "DP-9"), "missing Hz"));
        Harness.Scenario("Output names require an exact match", _ =>
            Harness.AssertEqual(-1, XrandrBackend.ParseRefreshRate(
                NvidiaPanels.Replace("DP-0 connected", "DP-01 connected"), "DP-0"), "prefix collision"));
        Harness.Scenario("Standard eDP panel still preferred", _ =>
            Harness.AssertEqual("eDP-1", XrandrBackend.SelectOutput(
                NvidiaPanels.Replace("DP-0 connected", "eDP-1 connected")
                    .Replace("ConnectorType: Panel", "ConnectorType: DisplayPort")), "eDP"));
        Harness.Scenario("Disconnected panel cannot capture controls", _ =>
            Harness.AssertEqual("HDMI-0", XrandrBackend.SelectOutput(
                NvidiaPanels.Replace("DP-0 connected 1920x1200+3440+240", "DP-0 disconnected")), "external fallback"));

        foreach (string panel in new[] { "DP-0", "eDP-1", "LVDS-1" })
            Harness.Scenario($"Inactive {panel} stays selected over active external", _ =>
            {
                string output = NvidiaPanels
                    .Replace("DP-0 connected 1920x1200+3440+240", panel + " connected")
                    .Replace("144.00*", "144.00");
                if (panel != "DP-0")
                    output = output.Replace("ConnectorType: Panel", "ConnectorType: DisplayPort");
                Harness.AssertEqual(panel, XrandrBackend.SelectOutput(output), "inactive panel");
                Harness.AssertEqual(-1, XrandrBackend.ParseRefreshRate(output, panel), "inactive Hz");
                WithXrandr(output, calls =>
                {
                    new XrandrBackend().SetRefreshRate(60);
                    Harness.AssertEqual("--current --prop", File.ReadAllText(calls).Trim(),
                        "inactive panel must not change external refresh");
                });
            });

        Harness.Scenario("Refresh queries use one current property snapshot each", _ =>
            WithXrandr(NvidiaPanels.Replace("Backlight: 40", "Backlight: 40\n    Gamma: 1.5 2.5 3.5"), calls =>
            {
                var backend = new XrandrBackend();
                Harness.AssertEqual(144, backend.GetRefreshRate(), "panel refresh");
                Harness.AssertEqual("--current --prop", File.ReadAllText(calls).Trim(), "single current query");
                File.WriteAllText(calls, "");
                Harness.AssertEqual("144,60", string.Join(",", backend.GetAvailableRefreshRates()),
                    "only mode rates, excluding numeric properties and external modes");
                Harness.AssertEqual("--current --prop", File.ReadAllText(calls).Trim(), "single rates query");
            }));

        Harness.Scenario("Refresh change queries once and targets the panel mode", _ =>
            WithXrandr(NvidiaPanels, calls =>
            {
                new XrandrBackend().SetRefreshRate(60);
                Harness.AssertEqual("--current --prop\n--output DP-0 --mode 1920x1200 --rate 60",
                    File.ReadAllText(calls).Trim(), "query followed by panel refresh change");
            }));
        Harness.Scenario("Gamma queries once and targets the panel", _ =>
            WithXrandr(NvidiaPanels, calls =>
            {
                new XrandrBackend().SetGamma(1, 1, 1);
                Harness.AssertEqual("--current --prop\n--output DP-0 --gamma 1.00:1.00:1.00",
                    File.ReadAllText(calls).Trim(), "query followed by panel gamma change");
            }));
        Harness.Scenario("External fallback prefers an active output", _ =>
            Harness.AssertEqual("HDMI-1", XrandrBackend.SelectOutput(
                "DP-1 connected (normal)\nHDMI-1 connected 1920x1080+0+0 (normal)"), "active external"));
        Harness.Scenario("External fallback preserves an inactive connected output", _ =>
            Harness.AssertEqual("HDMI-1", XrandrBackend.SelectOutput("HDMI-1 connected (normal)"), "connected external"));
    }

    private static void WithXrandr(string output, Action<string> test)
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("The xrandr command tests require Linux.");
        string directory = Path.Combine(Path.GetTempPath(), "ghelper-xrandr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string? oldPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            string executable = Path.Combine(directory, "xrandr");
            string calls = Path.Combine(directory, "calls");
            File.WriteAllText(Path.Combine(directory, "output"), output);
            File.WriteAllText(executable, $"#!/bin/sh\nprintf '%s\\n' \"$*\" >> '{calls}'\n/bin/cat '{directory}/output'\n");
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Environment.SetEnvironmentVariable("PATH", directory + ":" + oldPath);
            test(calls);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", oldPath);
            Directory.Delete(directory, recursive: true);
        }
    }
}
