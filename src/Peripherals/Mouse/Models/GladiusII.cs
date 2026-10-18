namespace GHelper.Linux.Peripherals.Mouse.Models;

public class GladiusIIOrigin : AsusMouse
{
    public GladiusIIOrigin() : this(0x1877, "mi_02", false) { }
    protected GladiusIIOrigin(ushort pid, string path, bool wireless, byte reportId = 0x00)
        : base(0x0B05, pid, path, wireless, reportId) { }

    public override string GetDisplayName() => "ROG Gladius II Origin";
    public override int ProfileCount() => 2;
    public override int DPIProfileCount() => 2;
    public override uint MaxDPI() => 12000;
    public override uint DPIIncrement() => 100;
    public override int MaxBrightness() => 4;
    public override PollingRate[] SupportedPollingrates() =>
        [PollingRate.PR125Hz, PollingRate.PR250Hz, PollingRate.PR500Hz, PollingRate.PR1000Hz];
}

public class GladiusII : GladiusIIOrigin
{
    public GladiusII() : base(0x1845, "mi_02", false) { }
    public override string GetDisplayName() => "ROG Gladius II";
    public override int ProfileCount() => 3;
}

public class GladiusIIOriginPink : GladiusIIOrigin
{
    public GladiusIIOriginPink() : base(0x18CD, "mi_02", false) { }
    public override string GetDisplayName() => "ROG Gladius II Origin PNK LTD";
    public override int ProfileCount() => 3;
    public override LightingZone[] SupportedLightingZones() =>
        [LightingZone.Scrollwheel, LightingZone.Underglow];
}

public class GladiusIIOriginCOD : GladiusIIOrigin
{
    public GladiusIIOriginCOD() : base(0x18B1, "mi_02", false) { }
    public override string GetDisplayName() => "ROG Gladius II Origin COD";
}

public class GladiusIICore : StrixImpactII
{
    public GladiusIICore() : base(0x18DD, "mi_00", false) { }
    public override string GetDisplayName() => "ROG Gladius II Core";
    public override LightingZone[] SupportedLightingZones() =>
        [LightingZone.Logo, LightingZone.Scrollwheel];
}
