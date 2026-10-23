using GHelper.Linux.Peripherals.Mouse;

namespace GHelper.Linux.Peripherals.Logitech.Models;

/// <summary>G305 Lightspeed (wireless only, detected via receiver).</summary>
public class G305 : LogitechMouse
{
    public G305() : base(0x0000, "", wireless: true) { }

    public override string GetDisplayName() => "G305 Lightspeed";
    public override int DPIProfileCount() => 5;
    public override uint MaxDPI() => 12000;
    public override uint MinDPI() => 200;
    public override uint DPIIncrement() => 50;

    public override LightingZone[] SupportedLightingZones() => [];
    public override LightingMode[] SupportedLightingModes() => [LightingMode.Off];
}
