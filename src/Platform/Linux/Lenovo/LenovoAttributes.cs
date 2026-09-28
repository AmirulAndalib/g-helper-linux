namespace GHelper.Linux.Platform.Linux;

/// <summary>
/// Lenovo lenovo-wmi-other firmware-attribute names (kernel 6.17+).
/// Path: /sys/class/firmware-attributes/lenovo-wmi-other-0/attributes/{name}/current_value
///
/// Read/written through the generic LinuxLenovoWmi PPT path
/// (GetPptLimit / SetPptLimit / GetAttributeRange / IsFeatureSupported).
/// Source: Linux kernel drivers/platform/x86/lenovo/wmi-other.c
/// </summary>
public static class LenovoAttributes
{
    // CPU power tunables (extend the existing PL1/PL2/FPPT sliders)

    public static readonly AttrDef PptApuSpl = new("ppt_pl1_apu_spl",
        description: "APU sustained power limit (SPL)");

    public static readonly AttrDef PptPl4Ipl = new("ppt_pl4_ipl",
        description: "Peak power limit (PL4 / instantaneous)");

    public static readonly AttrDef PptTau = new("ppt_pl1_tau",
        description: "Sustained power time window (Tau)");

    public static readonly AttrDef PptCpuCl = new("ppt_cpu_cl",
        description: "CPU cross-loading power limit");

    public static readonly AttrDef CpuTemp = new("cpu_temp",
        description: "CPU thermal limit (kernel 7.2+)");

    // Cross-loading variants (combined CPU+GPU budget)

    public static readonly AttrDef PptPl1SplCl = new("ppt_pl1_spl_cl",
        description: "PL1 SPL (cross-loading)");

    public static readonly AttrDef PptPl2SpptCl = new("ppt_pl2_sppt_cl",
        description: "PL2 SPPT (cross-loading)");

    public static readonly AttrDef PptPl3FpptCl = new("ppt_pl3_fppt_cl",
        description: "PL3 FPPT (cross-loading)");

    public static readonly AttrDef PptPl4IplCl = new("ppt_pl4_ipl_cl",
        description: "PL4 IPL (cross-loading)");

    // NVIDIA dGPU power / TGP (firmware budget; preferred over nvidia-smi -pl)

    public static readonly AttrDef GpuNvCtgp = new("gpu_nv_ctgp",
        description: "NVIDIA configurable TGP (settable power budget)");

    public static readonly AttrDef GpuNvPpab = new("gpu_nv_ppab",
        description: "NVIDIA PPAB / dynamic boost");

    public static readonly AttrDef GpuTemp = new("gpu_temp",
        description: "GPU temperature target");

    // Advanced GPU OC (Tier 3)

    public static readonly AttrDef DgpuBoostClk = new("dgpu_boost_clk",
        description: "dGPU boost clock");

    public static readonly AttrDef GpuNvCpuBoost = new("gpu_nv_cpu_boost",
        description: "NVIDIA CPU boost (for GPU)");

    public static readonly AttrDef GpuNvAcOffset = new("gpu_nv_ac_offset",
        description: "NVIDIA AC power offset");

    public static readonly AttrDef GpuNvBpl = new("gpu_nv_bpl",
        description: "NVIDIA battery power limit");

    // All known attributes (for diagnostics enumeration)

    public static readonly AttrDef[] All =
    {
        PptApuSpl, PptPl4Ipl, PptTau, PptCpuCl, CpuTemp,
        PptPl1SplCl, PptPl2SpptCl, PptPl3FpptCl, PptPl4IplCl,
        GpuNvCtgp, GpuNvPpab, GpuTemp,
        DgpuBoostClk, GpuNvCpuBoost, GpuNvAcOffset, GpuNvBpl,
    };
}
