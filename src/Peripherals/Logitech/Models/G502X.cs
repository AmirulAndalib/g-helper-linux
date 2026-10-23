using GHelper.Linux.Peripherals.Mouse;

namespace GHelper.Linux.Peripherals.Logitech.Models;

/// <summary>G502 X (wired, PID 0xC098).</summary>
public class G502X : LogitechMouse
{
    public G502X() : base(0xC098, "", wireless: false) { }

    public override string GetDisplayName() => "G502 X";
    public override int DPIProfileCount() => 5;
    public override uint MaxDPI() => 25600;
    public override uint MinDPI() => 100;
    public override uint DPIIncrement() => 50;

    public override LightingZone[] SupportedLightingZones() => [];
    public override LightingMode[] SupportedLightingModes() => [LightingMode.Off];
}

/// <summary>G502 X PLUS (wireless with RGB, PID 0xC099).</summary>
public class G502XPlus : LogitechMouse
{
    public G502XPlus() : base(0xC099, "", wireless: true) { }

    public override string GetDisplayName() => "G502 X PLUS";
    public override int DPIProfileCount() => 5;
    public override uint MaxDPI() => 25600;
    public override uint MinDPI() => 100;
    public override uint DPIIncrement() => 50;

    public override LightingZone[] SupportedLightingZones() =>
        [LightingZone.Logo, LightingZone.Scrollwheel, LightingZone.Underglow];

    public override LightingMode[] SupportedLightingModes() =>
        [LightingMode.Off, LightingMode.Static, LightingMode.Breathing, LightingMode.ColorCycle, LightingMode.Rainbow];
}

/// <summary>G502 X Lightspeed (wireless without RGB, PID 0xC09C).</summary>
public class G502XLightspeed : LogitechMouse
{
    public G502XLightspeed() : base(0xC09C, "", wireless: true) { }

    public override string GetDisplayName() => "G502 X Lightspeed";
    public override int DPIProfileCount() => 5;
    public override uint MaxDPI() => 25600;
    public override uint MinDPI() => 100;
    public override uint DPIIncrement() => 50;

    public override LightingZone[] SupportedLightingZones() => [];
    public override LightingMode[] SupportedLightingModes() => [LightingMode.Off];
}
