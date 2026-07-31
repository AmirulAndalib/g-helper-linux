using System.Runtime.InteropServices;
using System.Text;
using GHelper.Linux.Gpu.AMD;
using GHelper.Linux.Helpers;
using GHelper.Linux.I18n;
using GHelper.Linux.Input;
using GHelper.Linux.Mode;
using GHelper.Linux.Platform.Linux;
using GHelper.Linux.USB;
using HidSharp;

namespace GHelper.Linux.Ally;

/// <summary>
/// Linux port of Windows g-helper Ally/AllyControl.cs (~809 LoC → ~470 LoC after
/// the FPS / FPS-limiter / AutoTDP-by-FPS pieces were dropped - those rely on
/// AMD ADL which doesn't exist on Linux).
///
/// What's retained verbatim:
///   - Controller mode protocol     [0x5A, 0xD1, 0x01, 0x01, mode]
///   - Apply / commit               [0x5A, 0xD1, 0x0F, 0x20]
///   - Per-zone button bindings     [0x5A, 0xD1, 0x02, zone, 0x2C, ...]
///   - Turbo (autorepeat-hold)      [0x5A, 0xD1, 0x0F, 0x20, ...turbo_ms/50]
///   - Stick / trigger deadzones    [0x5A, 0xD1, 0x04|0x05|0x06, ...]
///   - Stick emulation injection    [0x5A, 0xD1, 0x15, ...]
///   - Disable Xbox controller      [0x5A, 0xD1, 0x0B, 0x01, 0x01|0x02]
///   - Wakeup handshake             ASCII "ZASUS Tech.Inc."
///
/// What's replaced:
///   - <c>Program.acpi.DeviceSet(PPT_APUA0/A3/C1, t)</c>
///        ⇒ <c>SysfsHelper.WriteToAllBackends(AsusAttributes.PptPl2Sppt/PptPl1Spl/PptFppt, t)</c>
///   - <c>amdControl.GetiGpuUse()</c>
///        ⇒ <c>LinuxAmdGpuMetrics.GetIgpuBusyPercent()</c>
///   - <c>Program.toast.RunToast()</c> ⇒ <c>App.System?.ShowNotification()</c>
///   - <c>InputDispatcher.GetBacklight()/SetBacklight()</c>
///        ⇒ <c>App.Wmi?.GetKeyboardBrightness()/SetKeyboardBrightness()</c>
///
/// What's dropped (no Linux equivalent without external tooling):
///   - <c>amdControl.GetFPS()</c> / <c>SetFPSLimit()</c> / <c>ToggleFPSLimit()</c>
///   - AutoTDP feedback loop that targets a frame rate
///   - FPS-driven hysteresis in auto controller-mode switching (replaced
///     with iGPU-busy-only heuristic - threshold &gt; 25%)
///
/// All HID protocol packet layouts mirror Windows g-helper EXACTLY because the
/// EC firmware on the Ally / Ally X parses these byte-for-byte; any deviation
/// silently fails. Reference: asusctl/rog-platform/examples/ally-*.rs.
///
/// Hardware coverage: ROG Ally (RC71L, USB PID 0x1ABE) + Ally X (RC72L,
/// USB PID 0x1B4C). Inert on every other model - gated by AppConfig.IsAlly().
/// </summary>
public class AllyControl
{
    private static System.Timers.Timer? timer;

    // Auto-mode state (re-evaluated every 300ms when _mode == Auto).
    private static ControllerMode _mode = ControllerMode.Auto;
    private static ControllerMode _applyMode = ControllerMode.Mouse;
    private static int _autoCount;

    // Manual TDP override (used by HandheldWindow / ModeControl integration).
    // Without an FPS feedback loop we can't dynamically adjust like Windows
    // does - this stays a static manual setting.
    private const int TdpMin = 6;
    private static int tdpStable = TdpMin;
    private static int tdpCurrent = -1;

    // Reuse Windows' Bind* aliases so callers can refer to them by historical
    // name. The catalog of all bindings (codes + display names) lives in
    // BindingGroups.cs.
    public const string BindA = BindingGroups.BindA;
    public const string BindB = BindingGroups.BindB;
    public const string BindX = BindingGroups.BindX;
    public const string BindY = BindingGroups.BindY;
    public const string BindLB = BindingGroups.BindLB;
    public const string BindRB = BindingGroups.BindRB;
    public const string BindLS = BindingGroups.BindLS;
    public const string BindRS = BindingGroups.BindRS;
    public const string BindDU = BindingGroups.BindDU;
    public const string BindDD = BindingGroups.BindDD;
    public const string BindDL = BindingGroups.BindDL;
    public const string BindDR = BindingGroups.BindDR;
    public const string BindLT = BindingGroups.BindLT;
    public const string BindRT = BindingGroups.BindRT;
    public const string BindVB = BindingGroups.BindVB;
    public const string BindMB = BindingGroups.BindMB;
    public const string BindXB = BindingGroups.BindXB;
    public const string BindM1 = BindingGroups.BindM1;
    public const string BindM2 = BindingGroups.BindM2;

    public const string BindMouseL = BindingGroups.BindMouseL;
    public const string BindMouseR = BindingGroups.BindMouseR;

    public const string BindKBU = BindingGroups.BindKBU;
    public const string BindKBD = BindingGroups.BindKBD;
    public const string BindKBL = BindingGroups.BindKBL;
    public const string BindKBR = BindingGroups.BindKBR;

    public const string BindTab = BindingGroups.BindTab;
    public const string BindEnter = BindingGroups.BindEnter;
    public const string BindBack = BindingGroups.BindBack;
    public const string BindEsc = BindingGroups.BindEsc;
    public const string BindPgU = BindingGroups.BindPgU;
    public const string BindPgD = BindingGroups.BindPgD;
    public const string BindShift = BindingGroups.BindShift;
    public const string BindCtrl = BindingGroups.BindCtrl;
    public const string BindAlt = BindingGroups.BindAlt;
    public const string BindWin = BindingGroups.BindWin;

    public const string BindToggleMode = BindingGroups.BindToggleMode;
    public const string BindShiftTab = BindingGroups.BindShiftTab;
    public const string BindAltTab = BindingGroups.BindAltTab;
    public const string BindWinTab = BindingGroups.BindWinTab;
    public const string BindBrightnessDown = BindingGroups.BindBrightnessDown;
    public const string BindBrightnessUp = BindingGroups.BindBrightnessUp;
    public const string BindXGM = BindingGroups.BindXGM;
    public const string BindOverlay = BindingGroups.BindOverlay;
    public const string BindScreenshot = BindingGroups.BindScreenshot;
    public const string BindShowDesktop = BindingGroups.BindShowDesktop;
    public const string BindShowKeyboard = BindingGroups.BindShowKeyboard;

    /// <summary>
    /// Constructor - sets up the 300ms auto-mode timer when running on an Ally.
    /// All operations are no-ops on non-Ally hardware (gated by IsAlly()),
    /// so it's safe to call this unconditionally from App startup.
    /// </summary>
    public AllyControl()
    {
        if (!Helpers.AppConfig.IsAlly())
            return;

        if (timer == null)
        {
            timer = new System.Timers.Timer(300);
            timer.Elapsed += Timer_Elapsed;
            Logger.WriteLine("Ally timer initialised");
        }
    }

    // ---------------------------------------------------------------------
    // TDP control - Linux equivalent of the 3 ACPI WMI writes Windows does.
    // Routes through SysfsHelper.WriteToAllBackends so we hit both legacy
    // (asus-nb-wmi) and firmware-attributes (asus-armoury) paths if both are
    // present - same as the existing PPT plumbing in ModeControl.
    // ---------------------------------------------------------------------

    private static int GetMaxTDP()
    {
        int tdp = Helpers.AppConfig.GetMode("limit_total");
        if (tdp > 0)
            return tdp;

        // Windows mapping verbatim:
        //   base=0 (Balanced) → 15W
        //   base=1 (Turbo)    → 25W
        //   base=2 (Silent)   → 10W
        return Modes.GetCurrentBase() switch
        {
            1 => 25,
            2 => 10,
            _ => 15,
        };
    }

    public static int GetTDP()
    {
        if (tdpCurrent < 0)
            tdpCurrent = GetMaxTDP();
        return tdpCurrent;
    }

    /// <summary>
    /// Write TDP value to all 3 PPT registers (PL1 sustained, PL2 boost,
    /// PL3 fast) so the APU power-limit envelope stays consistent.
    /// Only fires when called from a context that already verified IsAlly().
    /// </summary>
    public static void SetTDP(int tdp, string? log = null)
    {
        if (tdp < tdpStable)
            tdp = tdpStable;
        int max = GetMaxTDP();
        if (tdp > max)
            tdp = max;
        if (tdp == tdpCurrent)
            return;

        var v = tdp.ToString();
        SysfsHelper.WriteToAllBackends(AsusAttributes.PptPl2Sppt, v);
        SysfsHelper.WriteToAllBackends(AsusAttributes.PptPl1Spl, v);
        SysfsHelper.WriteToAllBackends(AsusAttributes.PptFppt, v);

        tdpCurrent = tdp;
        if (log != null)
            Logger.WriteLine($"AllyTDP: {log} = {tdp}W");
    }

    // ---------------------------------------------------------------------
    // 300ms timer: when in Auto mode, watch iGPU activity and switch to
    // Gamepad while a 3D app is running, otherwise to Mouse for desktop use.
    //
    // Windows uses BOTH FPS and iGPU usage as inputs. We can only get iGPU
    // busy% on Linux - single signal, slightly more conservative threshold
    // (>25% vs Windows >15%) to reduce false positives from desktop
    // compositor effects.
    // ---------------------------------------------------------------------

    private static void Timer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (_mode != ControllerMode.Auto)
            return;

        int? usage = LinuxAmdGpuMetrics.GetIgpuBusyPercent();
        ControllerMode newMode = (usage != null && usage > 25)
            ? ControllerMode.Gamepad
            : ControllerMode.Mouse;

        if (_applyMode != newMode)
            _autoCount++;
        else
            _autoCount = 0;

        if (_autoCount == 3)
        {
            _autoCount = 0;
            ApplyMode(newMode);
            Logger.WriteLine($"Ally Controller Auto-Mode (iGPU={usage}%): {newMode}");
        }
    }

    // ---------------------------------------------------------------------
    // Initialisation entry point - called from App.axaml.cs once at startup
    // (after AsusHid + Aura). Discovers the controller HID device, applies
    // the saved controller mode, and primes the timer.
    // ---------------------------------------------------------------------

    public void Init()
    {
        if (!Helpers.AppConfig.IsAlly())
            return;

        SetMode((ControllerMode)Helpers.AppConfig.Get("controller_mode", (int)ControllerMode.Auto), true);
    }

    // ---------------------------------------------------------------------
    // Controller-mode switching.
    //   Auto    → timer drives Gamepad/Mouse based on iGPU activity
    //   Gamepad → static gamepad
    //   Mouse   → sticks emulate mouse, buttons emulate clicks
    //   WASD    → sticks emulate WASD/arrows for legacy games
    //   Skip    → init-only sentinel (don't apply anything)
    //
    // ApplyMode() is the public entry point - it also reapplies all bindings
    // / turbo / deadzones because firmware drops them on mode change.
    // ---------------------------------------------------------------------

    private void SetMode(ControllerMode mode, bool init = false)
    {
        _mode = mode;
        Helpers.AppConfig.Set("controller_mode", (int)mode);

        ApplyMode(mode, init);

        timer?.Start();
    }

    public void ToggleModeHotkey()
    {
        if (!Helpers.AppConfig.IsAlly())
            return;

        if (_applyMode == ControllerMode.Gamepad)
        {
            SetMode(ControllerMode.Mouse);
            App.System?.ShowNotification(Labels.Get("ally_toast_title"),
                Labels.Get("controller_mode_mouse"), "input-gaming");
        }
        else
        {
            SetMode(ControllerMode.Gamepad);
            App.System?.ShowNotification(Labels.Get("ally_toast_title"),
                Labels.Get("controller_mode_gamepad"), "input-gaming");
        }
    }

    public void ToggleMode()
    {
        if (!Helpers.AppConfig.IsAlly())
            return;

        switch (_mode)
        {
            case ControllerMode.Auto:
                SetMode(ControllerMode.Gamepad);
                break;
            case ControllerMode.Gamepad:
                SetMode(ControllerMode.Mouse);
                break;
            case ControllerMode.Mouse:
                SetMode(ControllerMode.Skip);
                break;
            case ControllerMode.Skip:
                SetMode(ControllerMode.Auto);
                break;
        }
    }

    /// <summary>
    /// Apply the current mode to the controller. Spawns a short worker that
    /// retries up to ~5s waiting for the HID device to enumerate (the EC may
    /// take a moment to publish the controller endpoint after wake).
    ///
    /// On every apply we also reflash all 9 zone bindings + turbo + deadzones
    /// because the firmware resets them when the mode byte changes.
    /// </summary>
    public static void ApplyMode(ControllerMode applyMode = ControllerMode.Auto, bool init = false)
    {
        if (!Helpers.AppConfig.IsAlly())
            return;

        Task.Run(() =>
        {
            if (applyMode == ControllerMode.Skip)
                return;

            HidStream? input = AsusHid.FindHidStream(AsusHid.INPUT_ID);
            int count = 0;
            while (input == null && count++ < 10)
            {
                input = AsusHid.FindHidStream(AsusHid.INPUT_ID);
                Thread.Sleep(500);
            }
            if (input == null)
            {
                Logger.WriteLine("Ally: controller HID stream not found after 5s");
                return;
            }
            // We don't write through `input` directly - AsusHid.WriteInput()
            // re-discovers and writes via SetFeature which works for both USB
            // HidSharp and I2C-HID hidraw fallback. We just needed the wait.

            if (applyMode != ControllerMode.Auto)
                _applyMode = applyMode;

            if (init)
            {
                WakeUp();
                DisableXBoxController(false);
            }

            // Set the mode byte.
            AsusHid.WriteInput(
                new byte[] { AsusHid.INPUT_ID, 0xD1, 0x01, 0x01, (byte)_applyMode },
                "Ally Controller");

            // Reapply every zone binding for the new mode.
            BindZone(BindingZone.M1M2);
            BindZone(BindingZone.DPadUpDown);
            BindZone(BindingZone.DPadLeftRight);
            BindZone(BindingZone.StickClick);
            BindZone(BindingZone.Bumper);
            BindZone(BindingZone.AB);
            BindZone(BindingZone.XY);
            BindZone(BindingZone.ViewMenu);
            BindZone(BindingZone.Trigger);

            SetTurbo();
            SetDeadzones();

            // Restore disable-controller state if user had it set.
            if (init && Helpers.AppConfig.Is("controller_disabled"))
            {
                Thread.Sleep(500);
                DisableXBoxController(true);
            }
        });
    }

    // ---------------------------------------------------------------------
    // Wake-up handshake - same magic ASCII string Windows sends to nudge the
    // EC's controller block out of sleep. Without this, the first ApplyMode()
    // after suspend can be silently dropped.
    // ---------------------------------------------------------------------

    public static void WakeUp()
    {
        AsusHid.WriteInput(Encoding.ASCII.GetBytes("ZASUS Tech.Inc."), "Ally Wake");
    }

    // ---------------------------------------------------------------------
    // Per-zone binding write. Each "zone" packs two physical inputs (e.g.
    // DPadUpDown = D-Pad-Up + D-Pad-Down) and two binding slots per side
    // (primary + secondary) into a single 50-byte HID write.
    //
    // Wire layout (verbatim from Windows AllyControl.cs:595-608):
    //   bytes[0..4]    = [0x5A, 0xD1, 0x02, zone, 0x2C]
    //   bytes[5..15]   = primary-left  (DecodeBinding output, 11 bytes incl. padding)
    //   bytes[16..26]  = secondary-left
    //   bytes[27..37]  = primary-right
    //   bytes[38..48]  = secondary-right
    // ---------------------------------------------------------------------

    private static void BindZone(BindingZone zone)
    {
        string? KeyL1, KeyR1, KeyL2, KeyR2;
        bool desktop = (_applyMode == ControllerMode.Mouse);

        switch (zone)
        {
            case BindingZone.DPadUpDown:
                KeyL1 = Helpers.AppConfig.GetString("bind_du", desktop ? BindKBU : BindDU);
                KeyR1 = Helpers.AppConfig.GetString("bind_dd", desktop ? BindKBD : BindDD);
                KeyL2 = Helpers.AppConfig.GetString("bind2_du", BindShowKeyboard);
                KeyR2 = Helpers.AppConfig.GetString("bind2_dd", BindShowDesktop);
                break;
            case BindingZone.DPadLeftRight:
                KeyL1 = Helpers.AppConfig.GetString("bind_dl", desktop ? BindKBL : BindDL);
                KeyR1 = Helpers.AppConfig.GetString("bind_dr", desktop ? BindKBR : BindDR);
                KeyL2 = Helpers.AppConfig.GetString("bind2_dl", BindBrightnessDown);
                KeyR2 = Helpers.AppConfig.GetString("bind2_dr", BindBrightnessUp);
                break;
            case BindingZone.StickClick:
                KeyL1 = Helpers.AppConfig.GetString("bind_ls", desktop ? BindShift : BindLS);
                KeyR1 = Helpers.AppConfig.GetString("bind_rs", desktop ? BindMouseL : BindRS);
                KeyL2 = Helpers.AppConfig.GetString("bind2_ls");
                KeyR2 = Helpers.AppConfig.GetString("bind2_rs", BindToggleMode);
                break;
            case BindingZone.Bumper:
                KeyL1 = Helpers.AppConfig.GetString("bind_lb", desktop ? BindTab : BindLB);
                KeyR1 = Helpers.AppConfig.GetString("bind_rb", desktop ? BindMouseL : BindRB);
                KeyL2 = Helpers.AppConfig.GetString("bind2_lb");
                KeyR2 = Helpers.AppConfig.GetString("bind2_rb");
                break;
            case BindingZone.AB:
                KeyL1 = Helpers.AppConfig.GetString("bind_a", desktop ? BindEnter : BindA);
                KeyR1 = Helpers.AppConfig.GetString("bind_b", desktop ? BindEsc : BindB);
                KeyL2 = Helpers.AppConfig.GetString("bind2_a");
                KeyR2 = Helpers.AppConfig.GetString("bind2_b");
                break;
            case BindingZone.XY:
                KeyL1 = Helpers.AppConfig.GetString("bind_x", desktop ? BindPgD : BindX);
                KeyR1 = Helpers.AppConfig.GetString("bind_y", desktop ? BindPgU : BindY);
                KeyL2 = Helpers.AppConfig.GetString("bind2_x", BindScreenshot);
                KeyR2 = Helpers.AppConfig.GetString("bind2_y", BindOverlay);
                break;
            case BindingZone.ViewMenu:
                KeyL1 = Helpers.AppConfig.GetString("bind_vb", BindVB);
                KeyR1 = Helpers.AppConfig.GetString("bind_mb", BindMB);
                KeyL2 = Helpers.AppConfig.GetString("bind2_vb");
                KeyR2 = Helpers.AppConfig.GetString("bind2_mb");
                break;
            case BindingZone.M1M2:
                KeyL1 = Helpers.AppConfig.GetString("bind_m2", BindM2);
                KeyR1 = Helpers.AppConfig.GetString("bind_m1", BindM1);
                KeyL2 = Helpers.AppConfig.GetString("bind2_m2", BindM2);
                KeyR2 = Helpers.AppConfig.GetString("bind2_m1", BindM1);
                break;
            default:  // Trigger
                KeyL1 = Helpers.AppConfig.GetString("bind_lt", desktop ? BindShiftTab : BindLT);
                KeyR1 = Helpers.AppConfig.GetString("bind_rt", desktop ? BindMouseR : BindRT);
                KeyL2 = Helpers.AppConfig.GetString("bind2_lt");
                KeyR2 = Helpers.AppConfig.GetString("bind2_rt");
                break;
        }

        if (string.IsNullOrEmpty(KeyL1) && string.IsNullOrEmpty(KeyR1))
            return;

        var bindings = new byte[50];
        var init = new byte[] { AsusHid.INPUT_ID, 0xD1, 0x02, (byte)zone, 0x2C };
        init.CopyTo(bindings, 0);

        DecodeBinding(KeyL1).CopyTo(bindings, 5);
        DecodeBinding(KeyL2).CopyTo(bindings, 16);
        DecodeBinding(KeyR1).CopyTo(bindings, 27);
        DecodeBinding(KeyR2).CopyTo(bindings, 38);

        AsusHid.WriteInput(bindings, "Ally Bind");
    }

    /// <summary>
    /// Decode a string code like "01-01" or "04-03-8C-88-76" to a 10-byte
    /// HID slot. The first byte is the page; subsequent bytes go into
    /// page-specific offsets in the slot.
    /// </summary>
    private static byte[] DecodeBinding(string? binding)
    {
        if (string.IsNullOrEmpty(binding))
            return new byte[2];

        byte[] bytes;
        try
        { bytes = Helpers.AppConfig.StringToBytes(binding); }
        catch { return new byte[2]; }

        var code = new byte[10];
        code[0] = bytes[0];

        switch (bytes[0])
        {
            case 0x02:
                code[2] = bytes[1];
                break;
            case 0x03:
                code[4] = bytes[1];
                break;
            case 0x04:
                bytes.Skip(1).ToArray().CopyTo(code, 5);
                break;
            case 0x05:
                code[3] = bytes[1];
                break;
            default:
                code[1] = bytes[1];
                break;
        }
        return code;
    }

    // ---------------------------------------------------------------------
    // Turbo (auto-repeat hold timer).
    // 36 slots = 9 zones × 4 bindings (L1/L2/R1/R2). Each value is ms/50,
    // so 0=off, 1=50ms, 10=500ms (max). AppConfig keys are turbo_* (primary)
    // and turbo2_* (secondary).
    // ---------------------------------------------------------------------

    public static void SetTurbo()
    {
        if (!Helpers.AppConfig.IsAlly())
            return;

        var turbo = new byte[64];
        turbo[0] = AsusHid.INPUT_ID;
        turbo[1] = 0xD1;
        turbo[2] = 0x0F;
        turbo[3] = 0x20;

        // Layout for each zone: offset = 4 + (zone-1)*4, [L1, L2, R1, R2]
        void Z(int zone, string l1, string l2, string r1, string r2)
        {
            int o = 4 + (zone - 1) * 4;
            turbo[o + 0] = (byte)(Helpers.AppConfig.Get(l1, 0) / 50);
            turbo[o + 1] = (byte)(Helpers.AppConfig.Get(l2, 0) / 50);
            turbo[o + 2] = (byte)(Helpers.AppConfig.Get(r1, 0) / 50);
            turbo[o + 3] = (byte)(Helpers.AppConfig.Get(r2, 0) / 50);
        }

        Z(1, "turbo_du", "turbo2_du", "turbo_dd", "turbo2_dd");
        Z(2, "turbo_dl", "turbo2_dl", "turbo_dr", "turbo2_dr");
        Z(3, "turbo_ls", "turbo2_ls", "turbo_rs", "turbo2_rs");
        Z(4, "turbo_lb", "turbo2_lb", "turbo_rb", "turbo2_rb");
        Z(5, "turbo_a", "turbo2_a", "turbo_b", "turbo2_b");
        Z(6, "turbo_x", "turbo2_x", "turbo_y", "turbo2_y");
        Z(7, "turbo_vb", "turbo2_vb", "turbo_mb", "turbo2_mb");
        Z(8, "turbo_m2", "turbo2_m2", "turbo_m1", "turbo2_m1");
        Z(9, "turbo_lt", "turbo2_lt", "turbo_rt", "turbo2_rt");

        AsusHid.WriteInput(turbo, "Ally Turbo");
    }

    // ---------------------------------------------------------------------
    // Stick / trigger deadzones + vibration intensity.
    //   0x04 = sticks    [ls_min, ls_max, rs_min, rs_max]
    //   0x05 = triggers  [lt_min, lt_max, rt_min, rt_max]
    //   0x06 = vibration [intensity, intensity] (left + right rumble)
    // All values are 0..100 (percent).
    // ---------------------------------------------------------------------

    public static void SetDeadzones()
    {
        if (!Helpers.AppConfig.IsAlly())
            return;

        AsusHid.WriteInput(new byte[]
        {
            AsusHid.INPUT_ID, 0xD1, 0x04, 0x04,
            (byte)Helpers.AppConfig.Get("ls_min", 0),
            (byte)Helpers.AppConfig.Get("ls_max", 100),
            (byte)Helpers.AppConfig.Get("rs_min", 0),
            (byte)Helpers.AppConfig.Get("rs_max", 100),
        }, "Ally Stick DZ");

        AsusHid.WriteInput(new byte[]
        {
            AsusHid.INPUT_ID, 0xD1, 0x05, 0x04,
            (byte)Helpers.AppConfig.Get("lt_min", 0),
            (byte)Helpers.AppConfig.Get("lt_max", 100),
            (byte)Helpers.AppConfig.Get("rt_min", 0),
            (byte)Helpers.AppConfig.Get("rt_max", 100),
        }, "Ally Trigger DZ");

        AsusHid.WriteInput(new byte[]
        {
            AsusHid.INPUT_ID, 0xD1, 0x06, 0x02,
            (byte)Helpers.AppConfig.Get("vibra", 100),
            (byte)Helpers.AppConfig.Get("vibra", 100),
        }, "Ally Vibra");
    }

    // ---------------------------------------------------------------------
    // Stick calibration - analog axis range trim. Lets the user override the
    // factory stable / min / max raw values when the sticks drift or report
    // off-center centers. Protocol verbatim from
    // asusctl/rog-platform/examples/ally-gamepad-calibration.rs.
    //
    //   Calibrate :  [0x5A, 0xD1, 0x0D, 0x0E, 0x01, 0x01,
    //                 0, y_stable_hi, y_stable_lo, y_min_hi, y_min_lo,
    //                 y_max_hi, y_max_lo, x_stable_hi, x_stable_lo,
    //                 x_min_hi, x_min_lo, x_max_hi, x_max_lo, checksum,
    //                 ...44 zero bytes]
    //   Reset     :  [0x5A, 0xD1, 0x0D, 0x02, 0x02, 0x01, ...zeros]  (factory)
    //   Apply     :  [0x5A, 0xD1, 0x0D, 0x01, 0x03, ...zeros]        (commit)
    //
    // checksum = sum(bytes 6..17) & 0xFF.
    //
    // The asusctl example only writes one calibration packet - protocol byte
    // for left-vs-right stick selection is unclear without hardware. For now
    // we expose a single Apply method that operates on the active stick
    // (firmware default = left). Right-stick calibration tracked as TODO.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Reset stick calibration to factory defaults. Sends the reset packet
    /// followed by the apply commit so the change takes effect immediately.
    /// </summary>
    public static void ResetStickCalibration()
    {
        if (!Helpers.AppConfig.IsAlly())
            return;

        var reset = new byte[64];
        reset[0] = AsusHid.INPUT_ID;
        reset[1] = 0xD1;
        reset[2] = 0x0D;
        reset[3] = 0x02;
        reset[4] = 0x02;
        reset[5] = 0x01;
        AsusHid.WriteInput(reset, "Ally StickCal Reset");

        // Commit.
        var apply = new byte[64];
        apply[0] = AsusHid.INPUT_ID;
        apply[1] = 0xD1;
        apply[2] = 0x0D;
        apply[3] = 0x01;
        apply[4] = 0x03;
        AsusHid.WriteInput(apply, "Ally StickCal Apply");
    }

    /// <summary>
    /// Stick-selector byte sent in the calibrate header.
    /// asusctl's example uses <c>0x01, 0x01</c> after <c>0x0D, 0x0E</c> and
    /// only operates on the left stick. Best-current-guess based on protocol
    /// symmetry: byte[5] = stick index (1 = left, 2 = right). Untestable
    /// without hardware, but isolated: if firmware rejects "2", the packet
    /// is silently dropped - no permanent damage.
    /// </summary>
    public enum CalStick : byte
    {
        Left = 0x01,
        Right = 0x02,
    }

    /// <summary>
    /// Write user-supplied stick calibration. Each value is the raw ADC
    /// reading the EC reports for that position (range 0..4095). Apply is
    /// fired automatically after the calibration packet.
    /// </summary>
    /// <param name="stick">Which stick to calibrate (Left = factory default).</param>
    /// <param name="xStable">Center X (rest position).</param>
    /// <param name="xMin">Leftmost X observed.</param>
    /// <param name="xMax">Rightmost X observed.</param>
    /// <param name="yStable">Center Y.</param>
    /// <param name="yMin">Topmost Y observed.</param>
    /// <param name="yMax">Bottommost Y observed.</param>
    public static void ApplyStickCalibration(
        CalStick stick,
        int xStable, int xMin, int xMax,
        int yStable, int yMin, int yMax)
    {
        if (!Helpers.AppConfig.IsAlly())
            return;

        // Clamp to the EC's 12-bit range. Out-of-range values are silently
        // rejected by firmware - better to clip here so the checksum we
        // compute matches what the EC verifies.
        ushort xs = (ushort)Math.Clamp(xStable, 0, 4095);
        ushort xn = (ushort)Math.Clamp(xMin, 0, 4095);
        ushort xx = (ushort)Math.Clamp(xMax, 0, 4095);
        ushort ys = (ushort)Math.Clamp(yStable, 0, 4095);
        ushort yn = (ushort)Math.Clamp(yMin, 0, 4095);
        ushort yx = (ushort)Math.Clamp(yMax, 0, 4095);

        var pkt = new byte[64];
        pkt[0] = AsusHid.INPUT_ID;
        pkt[1] = 0xD1;
        pkt[2] = 0x0D;
        pkt[3] = 0x0E;
        pkt[4] = 0x01;
        pkt[5] = (byte)stick;  // 0x01 = left, 0x02 = right (best-guess)

        // Helpers - the EC reads big-endian 16-bit words for axis values.
        static byte HiByte(ushort v) => (byte)((v >> 8) & 0xFF);
        static byte LoByte(ushort v) => (byte)(v & 0xFF);

        pkt[6] = HiByte(ys);
        pkt[7] = LoByte(ys);
        pkt[8] = HiByte(yn);
        pkt[9] = LoByte(yn);
        pkt[10] = HiByte(yx);
        pkt[11] = LoByte(yx);
        pkt[12] = HiByte(xs);
        pkt[13] = LoByte(xs);
        pkt[14] = HiByte(xn);
        pkt[15] = LoByte(xn);
        pkt[16] = HiByte(xx);
        pkt[17] = LoByte(xx);

        // 8-bit modular checksum over the 12 axis bytes.
        int sum = 0;
        for (int i = 6; i <= 17; i++)
            sum += pkt[i];
        pkt[18] = (byte)(sum & 0xFF);

        AsusHid.WriteInput(pkt, $"Ally StickCal {stick} x=[{xn}/{xs}/{xx}] y=[{yn}/{ys}/{yx}]");

        // Commit.
        var apply = new byte[64];
        apply[0] = AsusHid.INPUT_ID;
        apply[1] = 0xD1;
        apply[2] = 0x0D;
        apply[3] = 0x01;
        apply[4] = 0x03;
        AsusHid.WriteInput(apply, "Ally StickCal Apply");
    }

    /// <summary>
    /// Trigger calibration packet - analog LT/RT range trim. Same envelope
    /// as the stick packet but uses sub-command 0x0F (vs 0x0E for sticks)
    /// and only carries 4 axis values: each trigger has stable (rest) and
    /// max (fully pressed). No min - triggers always rest at 0.
    ///
    /// Speculative byte layout matching the stick analogue:
    ///   [0..5]   = [0x5A, 0xD1, 0x0D, 0x0F, 0x02, stick_id]
    ///   [6..7]   = lt_stable (BE u16)
    ///   [8..9]   = lt_max
    ///   [10..11] = rt_stable
    ///   [12..13] = rt_max
    ///   [14]     = checksum (sum of bytes 6..13 mod 256)
    ///
    /// Untestable without HW. If firmware rejects sub-command 0x0F it'll
    /// silently no-op. Reset packet shape stays the same as for sticks.
    /// </summary>
    public static void ApplyTriggerCalibration(
        int ltStable, int ltMax, int rtStable, int rtMax)
    {
        if (!Helpers.AppConfig.IsAlly())
            return;

        ushort lts = (ushort)Math.Clamp(ltStable, 0, 4095);
        ushort ltx = (ushort)Math.Clamp(ltMax, 0, 4095);
        ushort rts = (ushort)Math.Clamp(rtStable, 0, 4095);
        ushort rtx = (ushort)Math.Clamp(rtMax, 0, 4095);

        var pkt = new byte[64];
        pkt[0] = AsusHid.INPUT_ID;
        pkt[1] = 0xD1;
        pkt[2] = 0x0D;
        pkt[3] = 0x0F;
        pkt[4] = 0x02;
        pkt[5] = 0x01;

        static byte HiByte(ushort v) => (byte)((v >> 8) & 0xFF);
        static byte LoByte(ushort v) => (byte)(v & 0xFF);

        pkt[6] = HiByte(lts);
        pkt[7] = LoByte(lts);
        pkt[8] = HiByte(ltx);
        pkt[9] = LoByte(ltx);
        pkt[10] = HiByte(rts);
        pkt[11] = LoByte(rts);
        pkt[12] = HiByte(rtx);
        pkt[13] = LoByte(rtx);

        int sum = 0;
        for (int i = 6; i <= 13; i++)
            sum += pkt[i];
        pkt[14] = (byte)(sum & 0xFF);

        AsusHid.WriteInput(pkt, $"Ally TriggerCal lt=[{lts}/{ltx}] rt=[{rts}/{rtx}]");

        // Commit.
        var apply = new byte[64];
        apply[0] = AsusHid.INPUT_ID;
        apply[1] = 0xD1;
        apply[2] = 0x0D;
        apply[3] = 0x01;
        apply[4] = 0x03;
        AsusHid.WriteInput(apply, "Ally TriggerCal Apply");
    }

    /// <summary>
    /// Programmatically disable / re-enable the Xbox controller endpoint.
    /// Used by the "Disable Controller" checkbox so external XInput hooks
    /// (Steam Input, etc.) don't see a duplicate device when the Ally is
    /// in Mouse mode.
    /// </summary>
    public static void DisableXBoxController(bool disabled)
    {
        if (!Helpers.AppConfig.IsAlly())
            return;

        AsusHid.WriteInput(
            new byte[] { AsusHid.INPUT_ID, 0xD1, 0x0B, 0x01, disabled ? (byte)0x02 : (byte)0x01 },
            $"Ally CtrlDis={disabled}");
    }

    /// <summary>
    /// Inject a virtual stick event. Used (currently) by macros / tests
    /// to drive XInput from software. x/y are clamped to [-1, 1].
    ///   stick = 0 (left) | 1 (right)
    /// </summary>
    public static void EmitStick(int stick, float x, float y)
    {
        if (!Helpers.AppConfig.IsAlly())
            return;

        var r = new byte[64];
        r[0] = AsusHid.INPUT_ID;
        r[1] = 0xD1;
        r[2] = 0x15;
        r[3] = (byte)stick;
        r[4] = 0x04;
        r[5] = 0x00;

        short sx = ToInt16(x);
        short sy = ToInt16(y);
        r[6] = (byte)((sx >> 8) & 0xFF);
        r[7] = (byte)(sx & 0xFF);
        r[8] = (byte)((sy >> 8) & 0xFF);
        r[9] = (byte)(sy & 0xFF);

        AsusHid.WriteInput(r, "Ally Stick");
    }

    private static short ToInt16(float v)
    {
        if (v > 1f)
            v = 1f;
        if (v < -1f)
            v = -1f;
        return v <= -1f ? short.MinValue : (short)(v * 32767f);
    }

    // ---------------------------------------------------------------------
    // Backlight cycle - Ally has 4 brightness levels (0..3). The Ally body
    // hotkey naturally cycles them; this gives the UI a button equivalent.
    // ---------------------------------------------------------------------

    public void ToggleBacklight()
    {
        if (!Helpers.AppConfig.IsAlly() || App.Wmi == null)
            return;

        int current = App.Wmi.GetKeyboardBrightness();
        int next = (current + 1) % 4;
        App.Wmi.SetKeyboardBrightness(next);
    }

    /// <summary>Direct-set the backlight (0..3). Used by HandheldWindow.</summary>
    public static int GetBacklight() => App.Wmi?.GetKeyboardBrightness() ?? 0;
    public static void SetBacklight(int level) => App.Wmi?.SetKeyboardBrightness(Math.Clamp(level, 0, 3));

    // ---------------------------------------------------------------------
    // Stick / trigger auto-capture via evdev.
    //
    // The Ally exposes its gamepad as a standard XInput device under
    // /dev/input/eventN. EV_ABS events report raw axis positions in the
    // range the kernel publishes (typically 0..255 for triggers, signed
    // -32768..32767 for sticks - but the EC's own calibration packet expects
    // 0..4095, so we have to map the captured kernel range into the EC's
    // 12-bit space before calling Apply*Calibration).
    //
    // Auto-capture flow (driven from HandheldWindow):
    //   1. User clicks "Capture (3s)" → CaptureAxes(3000) blocks for 3s
    //      reading EV_ABS events from the Ally controller node.
    //   2. We track per-axis (min, max, last) values during that window
    //      plus a "stable" estimate = the value held for the longest
    //      stretch (median fallback).
    //   3. UI populates the Apply form with the captured numbers; user
    //      reviews and clicks Apply.
    //
    // No HW available for testing at port time, so this code path is
    // structurally complete but unverified. On non-Ally systems
    // FindAllyControllerEvent returns null and CaptureAxes is a no-op.
    // ---------------------------------------------------------------------

    /// <summary>Range a single axis observed during the capture window.</summary>
    public readonly struct AxisRange
    {
        public int Min { get; }
        public int Max { get; }
        public int Stable { get; }   // best guess at "rest" position
        public int Samples { get; }
        public AxisRange(int min, int max, int stable, int samples)
        {
            Min = min;
            Max = max;
            Stable = stable;
            Samples = samples;
        }
        public bool IsValid => Samples > 0;
    }

    /// <summary>
    /// Per-stick / per-trigger axis snapshot returned by CaptureAxes.
    /// Values are raw kernel ABS_* values - convert to EC 12-bit space
    /// via <see cref="MapKernelAxisToEc"/> before passing to Apply*.
    /// </summary>
    public sealed class CaptureResult
    {
        public AxisRange LeftX, LeftY;
        public AxisRange RightX, RightY;
        public AxisRange TriggerL, TriggerR;
        public bool DeviceFound;
        public string? DevicePath;
    }

    /// <summary>
    /// Find the Ally gamepad evdev node. Walks /dev/input/event* and matches
    /// any device whose USB parent has VID 0x0B05 + PID 0x1ABE / 0x1B4C
    /// (Ally / Ally X). Returns the path or null when not found.
    ///
    /// Implementation: read each event device's IDs via EVIOCGID.
    /// </summary>
    public static string? FindAllyControllerEvent()
    {
        try
        {
            foreach (var path in Directory.EnumerateFiles("/dev/input", "event*"))
            {
                int fd = EvdevInterop.open(path, EvdevInterop.O_RDONLY | EvdevInterop.O_NONBLOCK);
                if (fd < 0)
                    continue;
                try
                {
                    int sz = Marshal.SizeOf<EvdevInterop.InputId>();
                    IntPtr buf = Marshal.AllocHGlobal(sz);
                    try
                    {
                        if (EvdevInterop.ioctl_buf(fd, EvdevInterop.EVIOCGID, buf) < 0)
                            continue;
                        var id = Marshal.PtrToStructure<EvdevInterop.InputId>(buf);
                        // ASUS VID is 0x0B05; Ally PIDs: 0x1ABE (RC71L), 0x1B4C (RC72L).
                        if (id.vendor == 0x0B05 && (id.product == 0x1ABE || id.product == 0x1B4C))
                        {
                            return path;
                        }
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
                finally { EvdevInterop.close(fd); }
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"FindAllyControllerEvent: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// Read EV_ABS events from the Ally controller for <paramref name="durationMs"/>
    /// milliseconds. Returns per-axis min/max/stable estimates. "Stable" is
    /// the modal value rounded to a 32-LSB bucket - proxy for "user held it
    /// at this position the longest", suitable as the calibration center.
    ///
    /// Blocks the calling thread; UI callers should run this on a worker.
    /// </summary>
    public static CaptureResult CaptureAxes(int durationMs = 3000)
    {
        var r = new CaptureResult();
        var path = FindAllyControllerEvent();
        if (path == null)
            return r;
        r.DeviceFound = true;
        r.DevicePath = path;

        int fd = EvdevInterop.open(path, EvdevInterop.O_RDONLY);
        if (fd < 0)
            return r;

        // Per-axis tally. histo[axis_code]:: bucket → count for stable estimation.
        var min = new Dictionary<ushort, int>();
        var max = new Dictionary<ushort, int>();
        var samples = new Dictionary<ushort, int>();
        var histo = new Dictionary<ushort, Dictionary<int, int>>();

        var buf = Marshal.AllocHGlobal(EvdevInterop.InputEventSize);
        try
        {
            long deadline = Environment.TickCount64 + durationMs;
            var pollFds = new EvdevInterop.Pollfd[1];
            pollFds[0].fd = fd;
            pollFds[0].events = EvdevInterop.POLLIN;

            while (Environment.TickCount64 < deadline)
            {
                int remaining = (int)(deadline - Environment.TickCount64);
                if (remaining <= 0)
                    break;
                int ready = EvdevInterop.poll(pollFds, 1, Math.Min(remaining, 100));
                if (ready <= 0)
                    continue;

                long n = EvdevInterop.read(fd, buf, (ulong)EvdevInterop.InputEventSize);
                if (n != EvdevInterop.InputEventSize)
                    continue;

                var ev = Marshal.PtrToStructure<EvdevInterop.InputEvent>(buf);
                if (ev.type != EvdevInterop.EV_ABS)
                    continue;

                // Only the 6 axis codes we care about.
                if (ev.code != EvdevInterop.ABS_X &&
                    ev.code != EvdevInterop.ABS_Y &&
                    ev.code != EvdevInterop.ABS_Z &&
                    ev.code != EvdevInterop.ABS_RX &&
                    ev.code != EvdevInterop.ABS_RY &&
                    ev.code != EvdevInterop.ABS_RZ)
                    continue;

                int v = ev.value;
                if (!samples.ContainsKey(ev.code))
                {
                    min[ev.code] = v;
                    max[ev.code] = v;
                    samples[ev.code] = 0;
                    histo[ev.code] = new Dictionary<int, int>();
                }
                if (v < min[ev.code])
                    min[ev.code] = v;
                if (v > max[ev.code])
                    max[ev.code] = v;
                samples[ev.code]++;

                // Bucket for stable estimation (32 LSB) - coarse enough that
                // sensor noise lands in the same bucket while gross movement
                // does not.
                int bucket = v >> 5;
                histo[ev.code][bucket] = histo[ev.code].TryGetValue(bucket, out var c) ? c + 1 : 1;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
            EvdevInterop.close(fd);
        }

        AxisRange Pack(ushort code)
        {
            if (!samples.TryGetValue(code, out int s) || s == 0)
                return default;
            int mostBucket = -1, mostCount = -1;
            foreach (var kv in histo[code])
                if (kv.Value > mostCount)
                { mostCount = kv.Value; mostBucket = kv.Key; }
            int stable = (mostBucket << 5) + 16;  // mid-point of bucket
            return new AxisRange(min[code], max[code], stable, s);
        }

        r.LeftX = Pack(EvdevInterop.ABS_X);
        r.LeftY = Pack(EvdevInterop.ABS_Y);
        r.RightX = Pack(EvdevInterop.ABS_RX);
        r.RightY = Pack(EvdevInterop.ABS_RY);
        r.TriggerL = Pack(EvdevInterop.ABS_Z);
        r.TriggerR = Pack(EvdevInterop.ABS_RZ);
        return r;
    }

    /// <summary>
    /// Map a captured kernel ABS value into the EC's 12-bit (0..4095)
    /// calibration space. Kernel exposes triggers as 0..255 by default and
    /// sticks as -32768..32767 (signed s16). We linearly rescale.
    /// </summary>
    public static int MapKernelAxisToEc(int kernelValue, int kernelMin, int kernelMax)
    {
        if (kernelMax <= kernelMin)
            return 2048; // pathological; default to mid
        long shifted = (long)kernelValue - kernelMin;
        long scaled = shifted * 4095 / (kernelMax - kernelMin);
        return (int)Math.Clamp(scaled, 0, 4095);
    }
}
