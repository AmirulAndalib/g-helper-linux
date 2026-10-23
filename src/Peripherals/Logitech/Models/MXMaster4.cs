using GHelper.Linux.Peripherals.Mouse;

namespace GHelper.Linux.Peripherals.Logitech.Models;

/// <summary>MX Master 4 (wireless only, detected via BT or Bolt receiver).</summary>
public class MXMaster4 : LogitechMouse
{
    public MXMaster4() : base(0x0000, "", wireless: true) { }
    protected MXMaster4(ushort pid) : base(pid, "", wireless: true) { }

    public override string GetDisplayName() => "MX Master 4";
    public override int DPIProfileCount() => 1;
    public override uint MaxDPI() => 8000;
    public override uint MinDPI() => 200;
    public override uint DPIIncrement() => 50;

    public override LightingZone[] SupportedLightingZones() => [];
    public override LightingMode[] SupportedLightingModes() => [LightingMode.Off];
}

/// <summary>MX Master 4 (Bluetooth, PID 0xB042).</summary>
public class MXMaster4BT : MXMaster4
{
    public MXMaster4BT() : base(0xB042) { }
    public override string GetDisplayName() => "MX Master 4 (BT)";
}
