using GHelper.Linux.Peripherals.Mouse;

namespace GHelper.Linux.Peripherals.Logitech.Models;

/// <summary>G502 HERO (wired, PID 0xC08B).</summary>
public class G502Hero : LogitechMouse
{
    public G502Hero() : base(0xC08B, "", wireless: false) { }

    public override string GetDisplayName() => "G502 HERO";
    public override int DPIProfileCount() => 5;
    public override uint MaxDPI() => 25600;
    public override uint MinDPI() => 100;
    public override uint DPIIncrement() => 50;

    public override LightingZone[] SupportedLightingZones() => [LightingZone.Logo];

    public override LightingMode[] SupportedLightingModes() =>
        [LightingMode.Off, LightingMode.Static, LightingMode.Breathing, LightingMode.ColorCycle];
}
