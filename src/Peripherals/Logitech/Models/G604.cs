using GHelper.Linux.Peripherals.Mouse;

namespace GHelper.Linux.Peripherals.Logitech.Models;

/// <summary>G604 Lightspeed (USB dongle mode, PID 0xC08F).</summary>
public class G604 : LogitechMouse
{
    public G604() : base(0xC08F, "", wireless: true) { }

    public override string GetDisplayName() => "G604 Lightspeed";
    public override int DPIProfileCount() => 5;
    public override uint MaxDPI() => 16000;
    public override uint MinDPI() => 100;
    public override uint DPIIncrement() => 50;

    public override LightingZone[] SupportedLightingZones() => [];
    public override LightingMode[] SupportedLightingModes() => [LightingMode.Off];
}
