using GHelper.Linux.Peripherals.Mouse;

namespace GHelper.Linux.Peripherals.Logitech.Models;

/// <summary>G403 Gaming Mouse (wired, PID 0xC082).</summary>
public class G403 : LogitechMouse
{
    public G403() : base(0xC082, "", wireless: false) { }

    public override string GetDisplayName() => "G403";
    public override int DPIProfileCount() => 5;
    public override uint MaxDPI() => 12000;
    public override uint MinDPI() => 200;
    public override uint DPIIncrement() => 50;

    public override LightingZone[] SupportedLightingZones() => [LightingZone.Logo];

    public override LightingMode[] SupportedLightingModes() =>
        [LightingMode.Off, LightingMode.Static, LightingMode.Breathing, LightingMode.ColorCycle];
}
