using GHelper.Linux.Peripherals.Mouse;

namespace GHelper.Linux.Peripherals.Logitech.Models;

/// <summary>G903 Lightspeed (non-Hero, wireless, PID 0xC086).</summary>
public class G903Lightspeed : LogitechMouse
{
    public G903Lightspeed() : base(0xC086, "", wireless: true) { }

    public override string GetDisplayName() => "G903 Lightspeed";
    public override int DPIProfileCount() => 5;
    public override uint MaxDPI() => 12000;
    public override uint MinDPI() => 200;
    public override uint DPIIncrement() => 50;

    public override LightingZone[] SupportedLightingZones() => [LightingZone.Logo];

    public override LightingMode[] SupportedLightingModes() =>
        [LightingMode.Off, LightingMode.Static, LightingMode.Breathing, LightingMode.ColorCycle];
}

/// <summary>G903 HERO (wireless, PID 0xC091).</summary>
public class G903Hero : LogitechMouse
{
    public G903Hero() : base(0xC091, "", wireless: true) { }

    public override string GetDisplayName() => "G903 HERO";
    public override int DPIProfileCount() => 5;
    public override uint MaxDPI() => 25600;
    public override uint MinDPI() => 100;
    public override uint DPIIncrement() => 50;

    public override LightingZone[] SupportedLightingZones() => [LightingZone.Logo];

    public override LightingMode[] SupportedLightingModes() =>
        [LightingMode.Off, LightingMode.Static, LightingMode.Breathing, LightingMode.ColorCycle];
}
