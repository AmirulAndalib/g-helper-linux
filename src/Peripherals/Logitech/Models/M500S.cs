using GHelper.Linux.Peripherals.Mouse;

namespace GHelper.Linux.Peripherals.Logitech.Models;

/// <summary>M500S Mouse (wired, PID 0xC093).</summary>
public class M500S : LogitechMouse
{
    public M500S() : base(0xC093, "", wireless: false) { }

    public override string GetDisplayName() => "M500S";
    public override int DPIProfileCount() => 1;
    public override uint MaxDPI() => 4000;
    public override uint MinDPI() => 400;
    public override uint DPIIncrement() => 25;

    public override LightingZone[] SupportedLightingZones() => [];
    public override LightingMode[] SupportedLightingModes() => [LightingMode.Off];
}
