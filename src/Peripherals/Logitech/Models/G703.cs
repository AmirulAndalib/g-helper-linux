using GHelper.Linux.Peripherals.Mouse;

namespace GHelper.Linux.Peripherals.Logitech.Models;

/// <summary>G703 Lightspeed (non-Hero, wireless, PID 0xC087).</summary>
public class G703Lightspeed : LogitechMouse
{
    public G703Lightspeed() : base(0xC087, "", wireless: true) { }

    public override string GetDisplayName() => "G703 Lightspeed";
    public override int DPIProfileCount() => 5;
    public override uint MaxDPI() => 12000;
    public override uint MinDPI() => 200;
    public override uint DPIIncrement() => 50;

    public override LightingZone[] SupportedLightingZones() => [LightingZone.Logo];

    public override LightingMode[] SupportedLightingModes() =>
        [LightingMode.Off, LightingMode.Static, LightingMode.Breathing, LightingMode.ColorCycle];
}

/// <summary>G703 HERO (wireless, PID 0xC090).</summary>
public class G703 : LogitechMouse
{
    public G703() : base(0xC090, "", wireless: true) { }

    public override string GetDisplayName() => "G703 HERO";
    public override int DPIProfileCount() => 5;
    public override uint MaxDPI() => 25600;
    public override uint MinDPI() => 100;
    public override uint DPIIncrement() => 50;

    public override LightingZone[] SupportedLightingZones() => [LightingZone.Logo];

    public override LightingMode[] SupportedLightingModes() =>
        [LightingMode.Off, LightingMode.Static, LightingMode.Breathing, LightingMode.ColorCycle];
}
