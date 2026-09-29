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

    // Real FA608UHI `xrandr --prop` captures from #201 (NVIDIA dGPU mode),
    // trimmed. Escaped literals on purpose: properties are tab-indented and
    // many lines end with a space, which a raw string plus .editorconfig
    // (indent_style = space, trim_trailing_whitespace) would silently rewrite.
    internal static readonly string Fa608uhiDgpuExternal = string.Join("\n",
        "Screen 0: minimum 8 x 8, current 5360 x 1440, maximum 32767 x 32767",
        "HDMI-0 connected primary 3440x1440+0+0 (normal left inverted right x axis y axis) 797mm x 334mm",
        "\t_MUTTER_PRESENTATION_OUTPUT: 0 ",
        "\tCTM: 0 1 0 0 0 0 0 0 0 1 0 0 0 0 0 0 ",
        "\t\t0 1 ",
        "\tCscMatrix: 65536 0 0 0 0 65536 0 0 0 0 65536 0 ",
        "\tEDID: ",
        "\t\t00ffffffffffff0010ac55d156453130",
        "\t\t1121010380502178eab495ac5046a025",
        "\tBorderDimensions: 4 ",
        "\t\tsupported: 4",
        "\tBorder: 0 0 0 0 ",
        "\t\trange: (0, 65535)",
        "\tSignalFormat: TMDS ",
        "\t\tsupported: TMDS",
        "\tConnectorType: HDMI ",
        "\tConnectorNumber: 0 ",
        "\t_ConnectorLocation: 0 ",
        "\tnon-desktop: 0 ",
        "\t\tsupported: 0, 1",
        "   3440x1440     59.97 +  99.98*   49.99  ",
        "   2560x1080     99.94    60.00    59.94  ",
        "   1920x1080     60.00    59.94    50.00  ",
        "   1680x1050     59.95  ",
        "   1440x900      59.89  ",
        "   1280x1024     75.02    60.02  ",
        "   1280x960      60.00  ",
        "   1280x800      59.81  ",
        "   1280x720     100.00    60.00    59.94    50.00  ",
        "   1152x864      75.00  ",
        "   1024x768      75.03    60.00  ",
        "   800x600       75.00    60.32  ",
        "   720x576       50.00  ",
        "   720x480       59.94  ",
        "   640x480       75.00    59.94    59.93  ",
        "DP-0 connected 1920x1200+3440+240 (normal left inverted right x axis y axis) 340mm x 220mm",
        "\t_MUTTER_PRESENTATION_OUTPUT: 0 ",
        "\tCTM: 0 1 0 0 0 0 0 0 0 1 0 0 0 0 0 0 ",
        "\t\t0 1 ",
        "\tCscMatrix: 65536 0 0 0 0 65536 0 0 0 0 65536 0 ",
        "\tBacklight: 100 ",
        "\t\trange: (0, 100)",
        "\tEDID: ",
        "\t\t00ffffffffffff0009e53c3800000000",
        "\t\t2d230104a52216780776f5a9534ca125",
        "\tBorderDimensions: 4 ",
        "\t\tsupported: 4",
        "\tBorder: 0 0 0 0 ",
        "\t\trange: (0, 65535)",
        "\tSignalFormat: DisplayPort ",
        "\t\tsupported: DisplayPort",
        "\tConnectorType: Panel ",
        "\tConnectorNumber: 1 ",
        "\t_ConnectorLocation: 1 ",
        "\tnon-desktop: 0 ",
        "\t\tsupported: 0, 1",
        "   1920x1200     60.00 + 144.00* ",
        "DP-1 disconnected (normal left inverted right x axis y axis)",
        "\tConnectorType: DisplayPort ",
        "eDP-1-1 disconnected (normal left inverted right x axis y axis)",
        "\tPRIME Synchronization: 1 ",
        "\t\tsupported: 0, 1",
        "\tGAMMA_LUT_SIZE: 4096 ",
        "\t\trange: (0, -1)",
        "\tpanel_type: unknown ",
        "\t\tsupported: unknown, OLED",
        "DisplayPort-1-1 disconnected (normal left inverted right x axis y axis)",
        "\tPRIME Synchronization: 1 ",
        "\t\tsupported: 0, 1");

    internal static readonly string Fa608uhiDgpuInternal = string.Join("\n",
        "Screen 0: minimum 8 x 8, current 1920 x 1200, maximum 32767 x 32767",
        "HDMI-0 disconnected (normal left inverted right x axis y axis)",
        "\tConnectorType: HDMI ",
        "DP-0 connected primary 1920x1200+0+0 (normal left inverted right x axis y axis) 345mm x 215mm",
        "\t_MUTTER_PRESENTATION_OUTPUT: 0 ",
        "\tCTM: 0 1 0 0 0 0 0 0 0 1 0 0 0 0 0 0 ",
        "\t\t0 1 ",
        "\tCscMatrix: 65536 0 0 0 0 65536 0 0 0 0 65536 0 ",
        "\tBacklight: 100 ",
        "\t\trange: (0, 100)",
        "\tEDID: ",
        "\t\t00ffffffffffff0009e53c3800000000",
        "\t\t2d230104a52216780776f5a9534ca125",
        "\tBorderDimensions: 4 ",
        "\t\tsupported: 4",
        "\tBorder: 0 0 0 0 ",
        "\t\trange: (0, 65535)",
        "\tSignalFormat: DisplayPort ",
        "\t\tsupported: DisplayPort",
        "\tConnectorType: Panel ",
        "\tConnectorNumber: 1 ",
        "\t_ConnectorLocation: 1 ",
        "\tnon-desktop: 0 ",
        "\t\tsupported: 0, 1",
        "   1920x1200     60.00*+ 144.00  ",
        "DP-1 disconnected (normal left inverted right x axis y axis)",
        "\tConnectorType: DisplayPort ",
        "eDP-1-1 disconnected (normal left inverted right x axis y axis)",
        "\tPRIME Synchronization: 1 ",
        "\t\tsupported: 0, 1");

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

        Harness.Scenario("FA608UHI capture: panel wins over external HDMI primary", _ =>
        {
            Harness.AssertEqual("DP-0", XrandrBackend.SelectOutput(Fa608uhiDgpuExternal), "panel");
            Harness.AssertEqual(144, XrandrBackend.ParseRefreshRate(Fa608uhiDgpuExternal, "DP-0"), "panel Hz");
            Harness.AssertEqual(100, XrandrBackend.ParseRefreshRate(Fa608uhiDgpuExternal, "HDMI-0"), "external Hz");
            WithXrandr(Fa608uhiDgpuExternal, calls =>
                Harness.AssertEqual("144,60", string.Join(",", new XrandrBackend().GetAvailableRefreshRates()),
                    "panel rates only, tab-indented properties skipped"));
        });
        Harness.Scenario("FA608UHI capture: internal panel only", _ =>
            WithXrandr(Fa608uhiDgpuInternal, calls =>
            {
                var backend = new XrandrBackend();
                Harness.AssertEqual("DP-0", backend.GetDisplayName(), "panel");
                Harness.AssertEqual(60, backend.GetRefreshRate(), "current Hz");
                Harness.AssertEqual("144,60", string.Join(",", backend.GetAvailableRefreshRates()), "panel rates");
            }));
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
            File.WriteAllText(executable, $"#!/bin/sh\nprintf '%s\\n' \"$*\" >> '{calls}'\ncat '{directory}/output'\n");
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
