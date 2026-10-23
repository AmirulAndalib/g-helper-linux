using GHelper.Linux.Peripherals.Mouse;

namespace GHelper.Linux.Peripherals.Logitech.Models;

/// <summary>G900 Chaos Spectrum (wireless, PID 0xC081).</summary>
public class G900 : LogitechMouse
{
    public G900() : base(0xC081, "", wireless: true) { }

    public override string GetDisplayName() => "G900 Chaos Spectrum";
    public override int DPIProfileCount() => 5;
    public override uint MaxDPI() => 12000;
    public override uint MinDPI() => 200;
    public override uint DPIIncrement() => 50;

    public override LightingZone[] SupportedLightingZones() => [LightingZone.Logo];

    public override LightingMode[] SupportedLightingModes() =>
        [LightingMode.Off, LightingMode.Static, LightingMode.Breathing, LightingMode.ColorCycle];
}
