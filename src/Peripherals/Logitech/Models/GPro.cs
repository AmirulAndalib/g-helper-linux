using GHelper.Linux.Peripherals.Mouse;

namespace GHelper.Linux.Peripherals.Logitech.Models;

/// <summary>G PRO Gaming Mouse (wired, PID 0xC088).</summary>
public class GProWired : LogitechMouse
{
    public GProWired() : base(0xC088, "", wireless: false) { }

    public override string GetDisplayName() => "G PRO Gaming Mouse";
    public override int DPIProfileCount() => 5;
    public override uint MaxDPI() => 16000;
    public override uint MinDPI() => 100;
    public override uint DPIIncrement() => 50;

    public override LightingZone[] SupportedLightingZones() => [LightingZone.Logo];

    public override LightingMode[] SupportedLightingModes() =>
        [LightingMode.Off, LightingMode.Static, LightingMode.Breathing, LightingMode.ColorCycle];
}

/// <summary>G PRO Wireless (USB wired mode, PID 0xC092).</summary>
public class GProWireless : LogitechMouse
{
    public GProWireless() : base(0xC092, "", wireless: true) { }

    public override string GetDisplayName() => "G PRO Wireless";
    public override int DPIProfileCount() => 5;
    public override uint MaxDPI() => 16000;
    public override uint MinDPI() => 100;
    public override uint DPIIncrement() => 50;

    public override LightingZone[] SupportedLightingZones() => [LightingZone.Logo];

    public override LightingMode[] SupportedLightingModes() =>
        [LightingMode.Off, LightingMode.Static, LightingMode.Breathing, LightingMode.ColorCycle];
}
