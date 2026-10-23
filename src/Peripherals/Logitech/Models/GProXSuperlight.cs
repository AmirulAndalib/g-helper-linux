using GHelper.Linux.Peripherals.Mouse;

namespace GHelper.Linux.Peripherals.Logitech.Models;

/// <summary>G PRO X SUPERLIGHT (USB wired mode, PID 0xC094).</summary>
public class GProXSuperlight : LogitechMouse
{
    public GProXSuperlight() : base(0xC094, "", wireless: true) { }

    public override string GetDisplayName() => "G PRO X SUPERLIGHT";
    public override int DPIProfileCount() => 5;
    public override uint MaxDPI() => 25600;
    public override uint MinDPI() => 100;
    public override uint DPIIncrement() => 50;
}

/// <summary>G PRO X SUPERLIGHT 2 (USB wired mode, PID 0xC09B).</summary>
public class GProXSuperlight2 : LogitechMouse
{
    public GProXSuperlight2() : base(0xC09B, "", wireless: true) { }

    public override string GetDisplayName() => "G PRO X SUPERLIGHT 2";
    public override int DPIProfileCount() => 5;
    public override uint MaxDPI() => 32000;
    public override uint MinDPI() => 100;
    public override uint DPIIncrement() => 50;
}
