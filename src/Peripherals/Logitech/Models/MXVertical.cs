using GHelper.Linux.Peripherals.Mouse;

namespace GHelper.Linux.Peripherals.Logitech.Models;

/// <summary>MX Vertical Wireless Mouse (USB wired mode, PID 0xC08A).</summary>
public class MXVertical : LogitechMouse
{
    public MXVertical() : base(0xC08A, "", wireless: true) { }
    protected MXVertical(ushort pid, string path, bool wireless) : base(pid, path, wireless) { }

    public override string GetDisplayName() => "MX Vertical";
    public override int DPIProfileCount() => 1;
    public override uint MaxDPI() => 4000;
    public override uint MinDPI() => 400;
    public override uint DPIIncrement() => 50;

    public override LightingZone[] SupportedLightingZones() => [];
    public override LightingMode[] SupportedLightingModes() => [LightingMode.Off];
}

/// <summary>MX Vertical (Bluetooth, PID 0xB020).</summary>
public class MXVerticalBT : MXVertical
{
    public MXVerticalBT() : base(0xB020, "", true) { }
    public override string GetDisplayName() => "MX Vertical (BT)";
}
