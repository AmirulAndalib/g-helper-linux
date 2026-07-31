using System.Text;
using GHelper.Linux.AnimeMatrix.Communication;
using GHelper.Linux.Helpers;
using HidSharp.Reports;

namespace GHelper.Linux.AnimeMatrix;

/// <summary>Animation mode identifiers for the Slash LED bar.</summary>
public enum SlashMode
{
    Bounce,
    Slash,
    Loading,
    BitStream,
    Transmission,
    Flow,
    Flux,
    Phantom,
    Spectrum,
    Hazard,
    Interfacing,
    Ramp,
    GameOver,
    Start,
    Buzzer,
    Static,
    FX1,
    FX2,
    FX3,
    BatteryLevel,
    Audio,
    AudioSpectrum
}

/// <summary>
/// HID packet for Slash LED bar commands. Default report ID 0x5E, 128 bytes.
/// </summary>
public class SlashPacket : Packet
{
    public SlashPacket(byte[] command, byte reportID = SlashDevice.ReportIdStandard)
        : base(reportID, 128, command)
    {
    }
}

/// <summary>
/// Controls the ASUS ROG "Slash" LED bar on the laptop lid via HID.
/// Standard (PID 0x193B, 0x5E) or Aura (PID 0x19B6, 0x5D) variants.
/// Length is 7 segments (standard) or 35 (long models).
/// </summary>
public class SlashDevice : Device
{
    public const ushort ProductIdStandard = 0x193B;
    public const ushort ProductIdAura = 0x19B6;
    public const byte ReportIdStandard = 0x5E;
    public const byte ReportIdAura = 0x5D;

    /// <inheritdoc />
    protected override string LogName => "Slash";

    /// <summary>0x5E for standard, 0x5D for Aura.</summary>
    protected virtual byte ReportID => ReportIdStandard;

    /// <summary>Number of LED segments: 35 for long models, 7 for standard.</summary>
    protected int Length { get; private set; }

    /// <summary>Maps SlashMode values to firmware mode bytes.</summary>
    public static readonly Dictionary<SlashMode, byte> ModeCodes = new()
    {
        { SlashMode.Static, 0x06 },
        { SlashMode.Bounce, 0x10 },
        { SlashMode.Slash, 0x12 },
        { SlashMode.Loading, 0x13 },

        { SlashMode.BitStream, 0x1D },
        { SlashMode.Transmission, 0x1A },

        { SlashMode.Flow, 0x19 },
        { SlashMode.Flux, 0x25 },
        { SlashMode.Phantom, 0x24 },
        { SlashMode.Spectrum, 0x26 },

        { SlashMode.Hazard, 0x32 },
        { SlashMode.Interfacing, 0x33 },
        { SlashMode.Ramp, 0x34 },

        { SlashMode.GameOver, 0x42 },
        { SlashMode.Start, 0x43 },
        { SlashMode.Buzzer, 0x44 },

        { SlashMode.FX1, 0x60 },
        { SlashMode.FX2, 0x61 },
        { SlashMode.FX3, 0x62 },
    };

    // Raw command payloads, report ID excluded. Single source of protocol bytes
    // shared by this device class and the hidraw facade in USB/Slash.cs.

    public static byte[][] CommandInit() =>
        [[0xD7, 0x00, 0x00, 0x01, 0xAC], [0xD2, 0x02, 0x01, 0x08, 0xAB]];

    public static byte[] CommandEnable(bool status) =>
        [0xD8, 0x02, 0x00, 0x01, status ? (byte)0x00 : (byte)0x80];

    public static byte[] CommandSave() =>
        [0xD4, 0x00, 0x00, 0x01, 0xAB];

    public static byte[] CommandOptions(bool status, byte brightness, byte interval) =>
        [0xD3, 0x03, 0x01, 0x08, 0xAB, 0xFF, 0x01,
         status ? (byte)0x01 : (byte)0x00,
         0x06, brightness, 0xFF, interval];

    private static byte[] CommandSetMode(byte modeCode, byte slot2Mode) =>
        [0xD3, 0x04, 0x00, 0x0C, 0x01, modeCode, 0x02, slot2Mode,
         0x03, 0x13, 0x04, 0x11, 0x05, 0x12, 0x06, 0x13];

    public static byte[] CommandSetMode(byte modeCode) => CommandSetMode(modeCode, 0x42);

    /// <summary>Mode packet variant used by the hidraw facade (legacy slot-2 byte).</summary>
    public static byte[] CommandSetModeLegacy(byte modeCode) => CommandSetMode(modeCode, 0x19);

    public static byte[] CommandShowOnBoot(bool status) =>
        [0xD3, 0x03, 0x01, 0x08, 0xA0, 0x04, 0xFF,
         status ? (byte)0x01 : (byte)0x00,
         0x01, 0xFF, 0x00];

    public static byte[] CommandShowOnSleep(bool status) =>
        [0xD3, 0x03, 0x01, 0x08, 0xA1, 0x00, 0xFF,
         status ? (byte)0x01 : (byte)0x00,
         0x02, 0xFF, 0xFF];

    public static byte[] CommandShowLowBattery(bool status) =>
        [0xD3, 0x03, 0x01, 0x08, 0xA2, 0x01, 0xFF,
         status ? (byte)0x01 : (byte)0x00,
         0x02, 0xFF, 0xFF];

    public static byte[] CommandShowOnShutdown(bool status) =>
        [0xD3, 0x03, 0x01, 0x08, 0xA4, 0x05, 0xFF,
         status ? (byte)0x01 : (byte)0x00,
         0x01, 0xFF, 0x00];

    public static byte[] CommandBatterySaver(bool status) =>
        [0xD8, 0x01, 0x00, 0x01, status ? (byte)0x80 : (byte)0x00];

    public static byte[] CommandLidCloseAnimation(bool status) =>
        [0xD8, 0x00, 0x00, 0x02, 0xA5, status ? (byte)0x00 : (byte)0x80];

    /// <summary>Initialises a Slash device. Packet length is always 128 bytes.</summary>
    public SlashDevice(ushort productId = ProductIdStandard) : base(0x0B05, productId, 128)
    {
        Length = AppConfig.IsSlashLong() ? 35 : 7;
    }

    /// <summary>Creates a SlashPacket with the correct report ID (AOT-safe).</summary>
    protected virtual SlashPacket CreatePacket(byte[] command)
    {
        return new SlashPacket(command, ReportID);
    }

    /// <summary>Detects Slash hardware. Returns Aura or standard variant, or null.</summary>
    public static SlashDevice? Detect()
    {
        if (HasSlashInterface(ProductIdAura, ReportIdAura))
            return new SlashDeviceAura();

        if (HasSlashInterface(ProductIdStandard, ReportIdStandard))
            return new SlashDevice();

        return null;
    }

    /// <summary>Checks whether a matching HID device is present.</summary>
    private static bool HasSlashInterface(ushort productId, byte reportId)
    {
        try
        {
            return HidSharp.DeviceList.Local
                .GetHidDevices(0x0B05, productId)
                .Any(d =>
                {
                    try
                    {
                        return d.GetMaxFeatureReportLength() >= 128
                            && d.GetReportDescriptor().TryGetReport(ReportType.Feature, reportId, out _);
                    }
                    catch
                    {
                        return false;
                    }
                });
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Sends the wake-up handshake sequence (3 packets).</summary>
    public void WakeUp()
    {
        lock (USB.AsusHid.HidLock)
        {
            Set(CreatePacket(Encoding.ASCII.GetBytes("ASUS Tech.Inc.")), "SlashWakeUp");
            Set(CreatePacket([0xC2]), "SlashWakeUp");
            Set(CreatePacket([0xD1, 0x01, 0x00, 0x01]), "SlashWakeUp");
        }
    }

    /// <summary>Sends the init sequence (2 packets). Call once after boot.</summary>
    public void Init()
    {
        lock (USB.AsusHid.HidLock)
        {
            foreach (var command in CommandInit())
                Set(CreatePacket(command), "SlashInit");
        }
    }

    /// <summary>Enables or disables the Slash LED bar.</summary>
    public void SetEnabled(bool status = true)
    {
        Set(CreatePacket(CommandEnable(status)), $"SlashEnable {status}");
    }

    /// <summary>Saves current state to firmware NVRAM.</summary>
    public void Save()
    {
        Set(CreatePacket(CommandSave()), "SlashSave");
    }

    /// <summary>Sets the firmware animation mode from ModeCodes lookup.</summary>
    public void SetMode(SlashMode mode)
    {
        byte modeByte;

        try
        {
            modeByte = ModeCodes[mode];
        }
        catch (Exception)
        {
            modeByte = 0x00;
        }

        lock (USB.AsusHid.HidLock)
        {
            Set(CreatePacket([0xD2, 0x03, 0x00, 0x0C]), "SlashMode");
            Set(CreatePacket(CommandSetMode(modeByte)), "SlashMode");
        }
    }

    /// <summary>Sets enabled state, brightness (0-3), and animation interval (0-5).</summary>
    public void SetOptions(bool status, int brightness = 0, int interval = 0)
    {
        byte brightnessByte = (byte)(brightness * 85.333);

        Set(CreatePacket(CommandOptions(status, brightnessByte, (byte)interval)), "SlashOptions");
    }

    /// <summary>Enables or disables battery saver (bar off on battery).</summary>
    public void SetBatterySaver(bool status)
    {
        Set(CreatePacket(CommandBatterySaver(status)), $"SlashBatterySaver {status}");
    }

    /// <summary>Enables or disables the lid-close animation.</summary>
    public void SetLidCloseAnimation(bool status)
    {
        Set(CreatePacket(CommandLidCloseAnimation(status)), $"SlashLidCloseAnimation {status}");
    }

    /// <summary>Enables or disables the sleep animation.</summary>
    public void SetSleepActive(bool status)
    {
        lock (USB.AsusHid.HidLock)
        {
            Set(CreatePacket([0xD2, 0x02, 0x01, 0x08, 0xA1]), "SlashSleepInit");
            Set(CreatePacket(CommandShowOnSleep(status)), $"SlashSleep {status}");
        }
    }

    /// <summary>Sends a custom LED pattern with setup preamble.</summary>
    public void SetCustom(byte[] data, string? log = "Static Data")
    {
        lock (USB.AsusHid.HidLock)
        {
            Set(CreatePacket([0xD2, 0x02, 0x01, 0x08, 0xAC]), null);
            Set(CreatePacket([0xD3, 0x03, 0x01, 0x08, 0xAC, 0xFF, 0xFF, 0x01, 0x05, 0xFF, 0xFF]), null);
            Set(CreatePacket([0xD4, 0x00, 0x00, 0x01, 0xAC]), null);
            ContinueCustom(data, log);
        }
    }

    /// <summary>Sends a custom data payload without setup. Used for streaming updates.</summary>
    public void ContinueCustom(byte[] data, string? log = null)
    {
        byte[] header = [0xD3, 0x00, 0x00, (byte)Length];
        byte[] payload = new byte[header.Length + Math.Min(data.Length, Length)];
        Array.Copy(header, payload, header.Length);
        Array.Copy(data, 0, payload, header.Length, Math.Min(data.Length, Length));
        Set(CreatePacket(payload), log);
    }

    /// <summary>Sets all LED segments to off.</summary>
    public void SetEmpty()
    {
        SetCustom(GetPercentagePattern(0, 0));
    }

    /// <summary>Shows the current battery level as a fill-bar pattern.</summary>
    public void SetBatteryPattern(int brightness)
    {
        int batteryPct = App.Power?.GetBatteryPercentage() ?? 100;
        int chargeLimit = Battery.BatteryControl.GetSavedChargeLimit();
        double fillRatio = 100.0 * batteryPct / Math.Max(1, chargeLimit);
        SetCustom(GetPercentagePattern(brightness, fillRatio), null);
    }

    /// <summary>Renders a dual-bar audio visualiser: bass from one end, treble from the other.</summary>
    public void SetAudioPattern(int brightness, double bass, double treble)
    {
        byte[] payload = new byte[Length];
        double step = 100.0 / Length;

        for (int i = 0; i < Length; i++)
        {
            double s = step * i;
            double e = step * (i + 1);

            if (bass > s)
                payload[Length - 1 - i] |= (byte)(Math.Min((bass - s) / (e - s), 1) * brightness * 0x20);

            if (treble > s)
                payload[Length - 1 - i] |= (byte)(Math.Min((treble - s) / (e - s), 1) * brightness * 0x50);
        }

        ContinueCustom(payload, null);
    }

    /// <summary>Sends a packet with optional diagnostic logging.</summary>
    public void Set(Packet packet, string? log)
    {
        lock (USB.AsusHid.HidLock)
            base.Set(packet);
        if (log is not null)
        {
            int len = Math.Min(24, packet.Data.Length);
            Logger.WriteLine($"{log}: {BitConverter.ToString(packet.Data, 0, len)}");
        }
    }

    /// <summary>Generates a fill-bar pattern. Partial segments are proportionally dimmed.</summary>
    private byte[] GetPercentagePattern(int brightness, double percentage)
    {
        double step = 100.0 / Length;
        int bracket = (int)Math.Floor(percentage / step);

        if (bracket >= Length)
            return Enumerable.Repeat((byte)(brightness * 85.333), Length).ToArray();

        byte[] pattern = new byte[Length];
        for (int i = Length - 1; i > Length - 1 - bracket; i--)
        {
            pattern[i] = (byte)(brightness * 85.333);
        }

        if (bracket < Length)
            pattern[Length - 1 - bracket] = (byte)(((percentage % step) * brightness * 85.333) / step);

        return pattern;
    }
}

/// <summary>Slash variant for the Aura interface (PID 0x19B6, report ID 0x5D).</summary>
public class SlashDeviceAura : SlashDevice
{
    /// <inheritdoc />
    protected override byte ReportID => ReportIdAura;

    public SlashDeviceAura() : base(ProductIdAura)
    {
    }

    /// <inheritdoc />
    protected override SlashPacket CreatePacket(byte[] command)
    {
        return new SlashPacket(command, ReportID);
    }
}
