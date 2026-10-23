using GHelper.Linux.Peripherals.Mouse;

namespace GHelper.Linux.Peripherals.Logitech.Models;

/// <summary>
/// MX Master 1st gen base class. Protocol 4.5, 29 features over BT.
/// Features include SmartShift, HiRes wheel, thumb wheel, gesture button,
/// reprogrammable controls, and adjustable DPI (400-1600).
/// </summary>
public class MXMaster : LogitechMouse
{
    public MXMaster() : base(0x0000, "", wireless: true) { }
    protected MXMaster(ushort pid, string path, bool wireless) : base(pid, path, wireless) { }

    public override string GetDisplayName() => "MX Master";
    public override int DPIProfileCount() => 1;
    public override uint MaxDPI() => 1600;
    public override uint MinDPI() => 200;
    public override uint DPIIncrement() => 50;

    public override LightingZone[] SupportedLightingZones() => [];
    public override LightingMode[] SupportedLightingModes() => [LightingMode.Off];
}

/// <summary>MX Master 1st gen (Bluetooth, PID 0xB012).</summary>
public class MXMasterBT : MXMaster
{
    public MXMasterBT() : base(0xB012, "", true) { }
    public override string GetDisplayName() => "MX Master (BT)";
}

/// <summary>MX Master 1st gen hardware variant (Bluetooth, PID 0xB01E).</summary>
public class MXMasterBTv2 : MXMaster
{
    public MXMasterBTv2() : base(0xB01E, "", true) { }
    public override string GetDisplayName() => "MX Master (BT)";
}

/// <summary>
/// MX Master 2S base class. Uses SmartShift Enhanced (0x2111) with tunable
/// torque, higher max DPI (4000), and the same thumb wheel/gesture features.
/// </summary>
public class MXMaster2S : LogitechMouse
{
    public MXMaster2S() : base(0x0000, "", wireless: true) { }
    protected MXMaster2S(ushort pid, string path, bool wireless) : base(pid, path, wireless) { }

    public override string GetDisplayName() => "MX Master 2S";
    public override int DPIProfileCount() => 1;
    public override uint MaxDPI() => 4000;
    public override uint MinDPI() => 200;
    public override uint DPIIncrement() => 50;

    public override LightingZone[] SupportedLightingZones() => [];
    public override LightingMode[] SupportedLightingModes() => [LightingMode.Off];
}

/// <summary>MX Master 2S (Bluetooth, PID 0xB019).</summary>
public class MXMaster2SBT : MXMaster2S
{
    public MXMaster2SBT() : base(0xB019, "", true) { }
    public override string GetDisplayName() => "MX Master 2S (BT)";
}
