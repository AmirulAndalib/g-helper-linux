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
    }
}
