using GHelper.Linux.Peripherals.Mouse;

namespace GHelper.Linux.Peripherals.Logitech.Models;

// HID++ 1.0 mice. Feature-based control is not available; only name display.

/// <summary>G9 Laser Mouse (wired, PID 0xC048, HID++ 1.0).</summary>
public class G9 : LogitechMouse
{
    public G9() : base(0xC048, "", wireless: false) { }

    public override string GetDisplayName() => "G9 Laser Mouse";
    public override int DPIProfileCount() => 1;
    public override uint MaxDPI() => 3200;
    public override uint MinDPI() => 200;
    public override uint DPIIncrement() => 200;

    public override LightingZone[] SupportedLightingZones() => [];
    public override LightingMode[] SupportedLightingModes() => [LightingMode.Off];
}

/// <summary>G9x Laser Mouse (wired, PID 0xC066, HID++ 1.0).</summary>
public class G9x : LogitechMouse
{
    public G9x() : base(0xC066, "", wireless: false) { }

    public override string GetDisplayName() => "G9x Laser Mouse";
    public override int DPIProfileCount() => 1;
    public override uint MaxDPI() => 5700;
    public override uint MinDPI() => 200;
    public override uint DPIIncrement() => 200;

    public override LightingZone[] SupportedLightingZones() => [];
    public override LightingMode[] SupportedLightingModes() => [LightingMode.Off];
}

/// <summary>G500 Gaming Mouse (wired, PID 0xC068, HID++ 1.0).</summary>
public class G500 : LogitechMouse
{
    public G500() : base(0xC068, "", wireless: false) { }

    public override string GetDisplayName() => "G500 Gaming Mouse";
    public override int DPIProfileCount() => 1;
    public override uint MaxDPI() => 5700;
    public override uint MinDPI() => 200;
    public override uint DPIIncrement() => 200;

    public override LightingZone[] SupportedLightingZones() => [];
    public override LightingMode[] SupportedLightingModes() => [LightingMode.Off];
}

/// <summary>G500s Gaming Mouse (wired, PID 0xC24E, HID++ 1.0).</summary>
public class G500s : LogitechMouse
{
    public G500s() : base(0xC24E, "", wireless: false) { }

    public override string GetDisplayName() => "G500s Gaming Mouse";
    public override int DPIProfileCount() => 1;
    public override uint MaxDPI() => 8200;
    public override uint MinDPI() => 200;
    public override uint DPIIncrement() => 50;

    public override LightingZone[] SupportedLightingZones() => [];
    public override LightingMode[] SupportedLightingModes() => [LightingMode.Off];
}

/// <summary>G700 Gaming Mouse (wired mode, PID 0xC06B, HID++ 1.0).</summary>
public class G700 : LogitechMouse
{
    public G700() : base(0xC06B, "", wireless: false) { }

    public override string GetDisplayName() => "G700 Gaming Mouse";
    public override int DPIProfileCount() => 1;
    public override uint MaxDPI() => 5700;
    public override uint MinDPI() => 200;
    public override uint DPIIncrement() => 200;

    public override LightingZone[] SupportedLightingZones() => [];
    public override LightingMode[] SupportedLightingModes() => [LightingMode.Off];
}

/// <summary>G700s Gaming Mouse (wired mode, PID 0xC07C, HID++ 1.0).</summary>
public class G700s : LogitechMouse
{
    public G700s() : base(0xC07C, "", wireless: false) { }

    public override string GetDisplayName() => "G700s Gaming Mouse";
    public override int DPIProfileCount() => 1;
    public override uint MaxDPI() => 8200;
    public override uint MinDPI() => 200;
    public override uint DPIIncrement() => 50;

    public override LightingZone[] SupportedLightingZones() => [];
    public override LightingMode[] SupportedLightingModes() => [LightingMode.Off];
}
