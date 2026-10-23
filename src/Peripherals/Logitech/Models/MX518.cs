using GHelper.Linux.Peripherals.Mouse;

namespace GHelper.Linux.Peripherals.Logitech.Models;

/// <summary>MX518 Gaming Mouse (wired, PID 0xC08E).</summary>
public class MX518 : LogitechMouse
{
    public MX518() : base(0xC08E, "", wireless: false) { }

    public override string GetDisplayName() => "MX518";
    public override int DPIProfileCount() => 4;
    public override uint MaxDPI() => 16000;
    public override uint MinDPI() => 100;
    public override uint DPIIncrement() => 50;

    public override LightingZone[] SupportedLightingZones() => [];
    public override LightingMode[] SupportedLightingModes() => [LightingMode.Off];
}
