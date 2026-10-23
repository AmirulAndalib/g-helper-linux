using GHelper.Linux.Peripherals.Mouse;

namespace GHelper.Linux.Peripherals.Logitech.Models;

/// <summary>G402 Hyperion Fury (wired, PID 0xC07E).</summary>
public class G402 : LogitechMouse
{
    public G402() : base(0xC07E, "", wireless: false) { }

    public override string GetDisplayName() => "G402 Hyperion Fury";
    public override int DPIProfileCount() => 4;
    public override uint MaxDPI() => 4000;
    public override uint MinDPI() => 240;
    public override uint DPIIncrement() => 10;

    public override LightingZone[] SupportedLightingZones() => [];
    public override LightingMode[] SupportedLightingModes() => [LightingMode.Off];
}
