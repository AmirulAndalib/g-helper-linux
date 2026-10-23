using GHelper.Linux.Peripherals.Mouse;

namespace GHelper.Linux.Peripherals.Logitech.Models;

/// <summary>MX Ergo (wireless only, detected via BT or receiver).</summary>
public class MXErgo : LogitechMouse
{
    public MXErgo() : base(0x0000, "", wireless: true) { }
    protected MXErgo(ushort pid) : base(pid, "", wireless: true) { }

    public override string GetDisplayName() => "MX Ergo";
    public override int DPIProfileCount() => 1;
    public override uint MaxDPI() => 4000;  // trackball, lower DPI range
    public override uint MinDPI() => 200;
    public override uint DPIIncrement() => 50;

    public override LightingZone[] SupportedLightingZones() => [];
    public override LightingMode[] SupportedLightingModes() => [LightingMode.Off];
}

/// <summary>MX Ergo (Bluetooth, PID 0xB01D).</summary>
public class MXErgoBT : MXErgo
{
    public MXErgoBT() : base(0xB01D) { }
    public override string GetDisplayName() => "MX Ergo (BT)";
}
