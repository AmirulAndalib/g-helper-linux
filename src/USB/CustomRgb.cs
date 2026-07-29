using GHelper.Linux.Gpu;
using GHelper.Linux.Helpers;

namespace GHelper.Linux.USB;

/// <summary>
/// Software-driven "custom" AURA modes. These don't ride the AURA HID
/// firmware effects - they read a runtime signal (CPU temp, battery %,
/// GPU mode) and paint the keyboard via <see cref="Aura.ApplyDirect"/>
/// / <see cref="Aura.ApplyDirectZones"/>.
///
/// Linux port of upstream g-helper's <c>Aura.CustomRGB</c> inner class,
/// minus AMBIENT
/// </summary>
public static class CustomRgb
{
    // CPU temp thresholds (°C) and corresponding colors.
    // Blue at idle, red when toasty.
    private const int TempFreeze = 20;
    private const int TempCold = 40;
    private const int TempWarm = 65;
    private const int TempHot = 90;

    private static readonly Rgb ColorFreeze = new(0x00, 0x00, 0xFF); // blue
    private static readonly Rgb ColorCold = new(0x00, 0x80, 0x00);   // green
    private static readonly Rgb ColorWarm = new(0xFF, 0xFF, 0x00);   // yellow
    private static readonly Rgb ColorHot = new(0xFF, 0x00, 0x00);    // red

    // Battery thresholds (%) and corresponding colors.
    private const float BattLow = 20f;
    private const float BattMid = 60f;
    private const float BattHigh = 100f;

    private static readonly Rgb ColorBattLow = new(0xFF, 0x00, 0x00);  // red
    private static readonly Rgb ColorBattMid = new(0xFF, 0xFF, 0x00);  // yellow
    private static readonly Rgb ColorBattHigh = new(0x00, 0xFF, 0x00); // lime

    // GPU mode colors (Ultimate=red, Standard=yellow, Eco=green, Optimized=yellow).
    private static readonly Rgb ColorGpuUltimate = new(0xFF, 0x00, 0x00);
    private static readonly Rgb ColorGpuStandard = new(0xFF, 0xFF, 0x00);
    private static readonly Rgb ColorGpuEco = new(0x00, 0x80, 0x00);

    // Internal RGB tuple - byte-based to match Aura.cs's protocol layer
    // (no System.Drawing.Color dependency, keeps AOT trim profile clean).
    private readonly record struct Rgb(byte R, byte G, byte B);

    /// <summary>
    /// Linear interpolation between two colors. <c>t</c> is clamped to [0,1].
    /// Delegates to <see cref="ColorUtils.GetWeightedAverage"/>.
    /// </summary>
    private static Rgb Lerp(Rgb a, Rgb b, float t)
    {
        var c = ColorUtils.GetWeightedAverage(
            Avalonia.Media.Color.FromRgb(a.R, a.G, a.B),
            Avalonia.Media.Color.FromRgb(b.R, b.G, b.B),
            t);
        return new Rgb(c.R, c.G, c.B);
    }

    /// <summary>
    /// Heatmap mode tick: read CPU temp, blend across the four threshold
    /// stops, push to keyboard. <paramref name="init"/> forces an HID
    /// re-initialization on the first apply after a mode switch (cheap
    /// on subsequent ticks).
    /// </summary>
    public static void ApplyHeatmap(bool init = false)
    {
        // Reuses the same WMI deviceID the tray monitor uses (0x00120094
        // Temp_CPU). Dispatches to LinuxAsusWmi.GetCpuTemp under the hood.
        int cpuTemp = App.Wmi?.DeviceGet(0x00120094) ?? -1;

        Rgb color;
        if (cpuTemp < 0)
        {
            // Sensor unavailable - default to "freeze" color rather than
            // a confusing flash.
            color = ColorFreeze;
        }
        else if (cpuTemp < TempCold)
        {
            float t = (cpuTemp - TempFreeze) / (float)(TempCold - TempFreeze);
            color = Lerp(ColorFreeze, ColorCold, t);
        }
        else if (cpuTemp < TempWarm)
        {
            float t = (cpuTemp - TempCold) / (float)(TempWarm - TempCold);
            color = Lerp(ColorCold, ColorWarm, t);
        }
        else if (cpuTemp < TempHot)
        {
            float t = (cpuTemp - TempWarm) / (float)(TempHot - TempWarm);
            color = Lerp(ColorWarm, ColorHot, t);
        }
        else
        {
            color = ColorHot;
        }

        Aura.ApplyDirect(color.R, color.G, color.B, init);
    }

    /// <summary>
    /// Battery mode tick: read battery %, blend red/yellow/lime, push.
    /// On systems without a battery (desktop/null power source) the read
    /// returns -1 and we paint red.
    /// </summary>
    public static void ApplyBattery()
    {
        int pct = App.Power?.GetBatteryPercentage() ?? -1;
        float battery = pct < 0 ? 0f : pct;

        Rgb color;
        if (battery < BattLow)
        {
            color = ColorBattLow;
        }
        else if (battery < BattMid)
        {
            float t = (battery - BattLow) / (BattMid - BattLow);
            color = Lerp(ColorBattLow, ColorBattMid, t);
        }
        else if (battery < BattHigh)
        {
            float t = (battery - BattMid) / (BattHigh - BattMid);
            color = Lerp(ColorBattMid, ColorBattHigh, t);
        }
        else
        {
            color = ColorBattHigh;
        }

        Aura.ApplyDirect(color.R, color.G, color.B);
    }

    /// <summary>
    /// GPU-mode color: paint the keyboard a static color reflecting the
    /// current dGPU power state. Event-driven (no timer) - call this
    /// after every successful GPU mode switch and once on Aura init when
    /// the mode is selected.
    /// </summary>
    public static void ApplyGpuColor()
    {
        var gpuMode = App.GpuModeCtrl?.GetCurrentMode() ?? GpuMode.Standard;
        Rgb color = gpuMode switch
        {
            GpuMode.Ultimate => ColorGpuUltimate,
            GpuMode.Eco => ColorGpuEco,
            GpuMode.Optimized => ColorGpuStandard,  // Optimized uses Standard color on Windows
            _ => ColorGpuStandard,
        };
        Aura.ApplyDirect(color.R, color.G, color.B);
    }

    /// <summary>
    /// 2-color gradient across the 8 zones (4 keyboard + 4 lightbar).
    /// On non-Strix devices we degrade to a single-color paint with
    /// <c>Color1</c> - the user picked Gradient, so honor at least one
    /// of their colors instead of silently no-op'ing.
    ///
    /// Zone layout (matches upstream):
    /// - keyboard zones 0..3 → Color2 → Color1 weighted by index/3
    /// - lightbar zones, painted in order [7,6,4,5] → Color2 → Color1 weighted by i/3
    ///   (the 7,6,4,5 order is the physical L→R lightbar arrangement)
    /// </summary>
    public static void ApplyGradient()
    {
        if (!Aura.IsStrixZoned)
        {
            // Non-Strix fallback: paint primary color across the keyboard.
            Aura.ApplyDirect(Aura.ColorR, Aura.ColorG, Aura.ColorB, true);
            return;
        }

        var c1 = new Rgb(Aura.ColorR, Aura.ColorG, Aura.ColorB);
        var c2 = new Rgb(Aura.Color2R, Aura.Color2G, Aura.Color2B);

        // 8 zones × 3 bytes (RGB) - the byte layout consumed by ApplyDirectZones.
        byte[] zones = new byte[8 * 3];

        // Keyboard zones 0..3: gradient from c2 (left) to c1 (right).
        for (int z = 0; z < 4; z++)
        {
            float t = z / 3f;
            var c = Lerp(c2, c1, t);
            zones[z * 3] = c.R;
            zones[z * 3 + 1] = c.G;
            zones[z * 3 + 2] = c.B;
        }

        // Lightbar zones - physical L→R order on the chassis is [7,6,4,5].
        // Paint that sequence with the gradient so the lightbar visually
        // matches the keyboard direction.
        int[] lightbarOrder = { 7, 6, 4, 5 };
        for (int i = 0; i < lightbarOrder.Length; i++)
        {
            float t = i / 3f;
            var c = Lerp(c2, c1, t);
            int z = lightbarOrder[i];
            zones[z * 3] = c.R;
            zones[z * 3 + 1] = c.G;
            zones[z * 3 + 2] = c.B;
        }

        // Init handshake can swallow the first frame on Strix; send the
        // frame again so the static paint always lands
        Aura.ApplyDirectZones(zones, true);
        Aura.ApplyDirectZones(zones);
    }

    /// <summary>
    /// Diagnostic 8-zone rainbow paint: red, orange, yellow, green, cyan, blue,
    /// magenta, white across the keyboard zones, plus the same colors on the
    /// lightbar. Used to verify zone wiring after detection (especially the
    /// G513 flipped-lightbar quirk). Static, no timer.
    /// </summary>
    public static void ApplyZoneTest()
    {
        // Eight test zones - one per zone index used by ApplyDirectZones.
        var test = new (byte R, byte G, byte B)[]
        {
            (0xFF, 0x00, 0x00), // 0 = red
            (0xFF, 0x80, 0x00), // 1 = orange
            (0xFF, 0xFF, 0x00), // 2 = yellow
            (0x00, 0xFF, 0x00), // 3 = green
            (0x00, 0xFF, 0xFF), // 4 = cyan
            (0x00, 0x00, 0xFF), // 5 = blue
            (0xFF, 0x00, 0xFF), // 6 = magenta
            (0xFF, 0xFF, 0xFF), // 7 = white
        };

        byte[] zones = new byte[test.Length * 3];
        for (int z = 0; z < test.Length; z++)
        {
            zones[z * 3] = test[z].R;
            zones[z * 3 + 1] = test[z].G;
            zones[z * 3 + 2] = test[z].B;
        }

        // Paint keyboard zones first, then explicitly target the lightbar.
        // ApplyDirectZones handles the per-key/4-zone branch + flipped quirk;
        // ApplyDirectLightbar sends one extra packet so the lightbar isn't
        // skipped on per-key Strix where ApplyDirectZones bails after keys.
        Aura.ApplyDirectZones(zones, true);
        Aura.ApplyDirectLightbar(zones);
    }
}
