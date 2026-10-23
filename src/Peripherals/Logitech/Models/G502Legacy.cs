using GHelper.Linux.Peripherals.Mouse;

namespace GHelper.Linux.Peripherals.Logitech.Models;

/// <summary>G502 original (wired, PID 0xC07D).</summary>
public class G502 : LogitechMouse
{
    public G502() : base(0xC07D, "", wireless: false) { }

    public override string GetDisplayName() => "G502";
    public override int DPIProfileCount() => 5;
    public override uint MaxDPI() => 12000;
    public override uint MinDPI() => 200;
    public override uint DPIIncrement() => 50;

    public override LightingZone[] SupportedLightingZones() => [LightingZone.Logo];

    public override LightingMode[] SupportedLightingModes() =>
        [LightingMode.Off, LightingMode.Static, LightingMode.Breathing, LightingMode.ColorCycle];
}

/// <summary>G502 Proteus Spectrum (wired, PID 0xC332).</summary>
public class G502ProteusSpectrum : LogitechMouse
{
    public G502ProteusSpectrum() : base(0xC332, "", wireless: false) { }

    public override string GetDisplayName() => "G502 Proteus Spectrum";
    public override int DPIProfileCount() => 5;
    public override uint MaxDPI() => 12000;
    public override uint MinDPI() => 200;
    public override uint DPIIncrement() => 50;

    public override LightingZone[] SupportedLightingZones() => [LightingZone.Logo];

    public override LightingMode[] SupportedLightingModes() =>
        [LightingMode.Off, LightingMode.Static, LightingMode.Breathing, LightingMode.ColorCycle];
}

/// <summary>G502 Lightspeed (wireless, PID 0xC08D).</summary>
public class G502Lightspeed : LogitechMouse
{
    public G502Lightspeed() : base(0xC08D, "", wireless: true) { }

    public override string GetDisplayName() => "G502 Lightspeed";
    public override int DPIProfileCount() => 5;
    public override uint MaxDPI() => 16000;
    public override uint MinDPI() => 100;
    public override uint DPIIncrement() => 50;

    public override LightingZone[] SupportedLightingZones() => [LightingZone.Logo];

    public override LightingMode[] SupportedLightingModes() =>
        [LightingMode.Off, LightingMode.Static, LightingMode.Breathing, LightingMode.ColorCycle];
}
