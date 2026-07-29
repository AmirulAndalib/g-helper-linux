namespace GHelper.Linux.Peripherals.Mouse.Models;

public class TUFM4Wirelss : TUFM4Air
{
    public TUFM4Wirelss() : this(0x19F4, "mi_00", true) { }
    protected TUFM4Wirelss(ushort pid, string path, bool wireless, byte reportId = 0x00)
        : base(pid, path, wireless, reportId) { }

    public override string GetDisplayName() => "TUF Gaming M4 Wireless";
    public override uint MaxDPI() => 12000;
    public override bool HasDebounce() => true;
}

public class TUFM4WirelssCN : TUFM4Wirelss
{
    public TUFM4WirelssCN() : base(0x1A8D, "mi_00", true) { }
    public override string GetDisplayName() => "TUF Gaming M4 Wireless";
}

public class TXGamingMini : TUFM4Wirelss
{
    public TXGamingMini() : this(0x1AF5, "mi_00", true) { }
    protected TXGamingMini(ushort pid, string path, bool wireless, byte reportId = 0x00)
        : base(pid, path, wireless, reportId) { }

    public override string GetDisplayName() => "TX Gaming Mini";
    public override uint DPIIncrement() => 50;
    public override bool HasXYDPI() => true;
}

public class TXGamingMiniWired : TXGamingMini
{
    public TXGamingMiniWired() : base(0x1AF3, "mi_00", false) { }
    public override string GetDisplayName() => "TX Gaming Mini (Wired)";
}

public class TUFGamingMiniMiku : TXGamingMini
{
    public TUFGamingMiniMiku() : this(0x1C57, true) { }
    protected TUFGamingMiniMiku(ushort pid, bool wireless) : base(pid, "mi_00", wireless) { }

    public override string GetDisplayName() => "TUF GAMING Mini Miku Edition (Wireless)";

    public override LightingZone[] SupportedLightingZones() =>
        [LightingZone.Logo, LightingZone.Scrollwheel, LightingZone.Underglow];
}

public class TUFGamingMiniMikuWired : TUFGamingMiniMiku
{
    public TUFGamingMiniMikuWired() : base(0x1C56, false) { }
    public override string GetDisplayName() => "TUF GAMING Mini Miku Edition (Wired)";
}
