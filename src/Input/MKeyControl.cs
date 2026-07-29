using GHelper.Linux.Helpers;
using GHelper.Linux.I18n;
using GHelper.Linux.USB;

namespace GHelper.Linux.Input;

/// <summary>
/// Firmware (EC) rebinding of the Strix M1/M2/M3/M5 macro keys. Instead of
/// intercepting the key in software, this writes an EC opcode into the
/// keyboard controller so the key itself emits the chosen hardware function
/// (volume, backlight, media, etc.) - it keeps working the same on any DE and
/// even while g-helper is closed. Port of upstream MKeyControl (#5691).
///
/// Wire protocol (AURA HID feature reports):
///   probe:  SetFeature [AURA_ID, 0x9F, 0x02, 0x00] then GetFeature; reply
///           byte[4] = slot count, byte[5..] = per-slot default opcodes.
///   set:    SetFeature [AURA_ID, 0x9F, 0x03, 0x01, slotKey, opcode]  (0 = off)
/// </summary>
public static class MKeyControl
{
    // EC function opcodes. 0 disables a key. Names match the software key
    // action ids used by InputDispatcher where they overlap.
    private static readonly Dictionary<string, byte> OpcodeActions = new()
    {
        { "micmute_hw", 1 },
        { "volume_down", 2 },
        { "volume_up", 3 },
        { "rog", 4 },
        { "mute", 5 },
        { "backlight_down", 6 },
        { "backlight_up", 7 },
        { "aura_previous", 8 },
        { "aura_next", 9 },
        { "media_previous", 10 },
        { "media_next", 11 },
        { "play", 12 },
        { "media_stop", 13 },
        { "performance", 14 },
        { "brightness_down", 15 },
        { "brightness_up", 16 },
        { "display_mode", 17 },
        { "touchpad", 18 },
        { "sleep", 19 },
        { "airplane", 20 },
        { "calculator", 21 },
        { "screen_off", 22 },
    };

    private static bool? _supported;
    private static readonly Dictionary<string, int> _slots = new();
    private static byte[] _defaults = Array.Empty<byte>();

    // Models without remappable M-keys, or where the EC block is owned elsewhere.
    private static bool Skip =>
        AppConfig.IsZ13() || AppConfig.IsAlly() || AppConfig.IsVivoZenPro()
        || AppConfig.NoMKeys() || AppConfig.IsARCNM();

    /// <summary>Apply every configured slot binding. Call at startup and on
    /// config change.</summary>
    public static void ApplyAll()
    {
        if (Skip || !IsSupported())
            return;
        foreach (string name in _slots.Keys)
            Apply(name);
    }

    /// <summary>Restore all slots to their firmware defaults.</summary>
    public static void Reset()
    {
        if (Skip || !IsSupported())
            return;
        foreach (int key in _slots.Values)
            SetOpcode(key, DefaultOpcode(key));
    }

    private static void Apply(string name)
    {
        if (!_slots.TryGetValue(name, out int key))
            return;

        string? action = AppConfig.GetString(name);

        // Chosen opcode, or the firmware default when unset / not applied.
        if (string.IsNullOrEmpty(action) || !OpcodeActions.TryGetValue(action, out byte opcode))
            SetOpcode(key, DefaultOpcode(key));
        else
            SetOpcode(key, opcode);
    }

    private static byte DefaultOpcode(int key) =>
        key >= 0 && key < _defaults.Length ? _defaults[key] : (byte)0;

    private static bool SetOpcode(int key, byte opcode) =>
        HidrawHelper.WriteAll(
            AsusHid.AURA_ID,
            new byte[] { AsusHid.AURA_ID, 0x9F, 0x03, 0x01, (byte)key, opcode },
            $"MKey {key} opcode {opcode}");

    /// <summary>Probe once and cache whether the firmware supports remapping.</summary>
    public static bool IsSupported()
    {
        if (_supported is null)
        {
            _supported = Probe();
            Logger.WriteLine($"MKey remap supported: {_supported}");
        }
        return _supported.Value;
    }

    private static bool Probe()
    {
        var defaults = HidrawHelper.MKeyProbe();
        if (defaults is null || defaults.Length == 0)
            return false;

        _defaults = defaults;
        MapSlots(defaults.Length);
        return true;
    }

    // Slot index layout by reported count. On Strix the physical M4 sits at
    // count-2 (exposed as "m5"). The last slot (count-1) is the ROG key, which
    // Linux binds through the software key-binding system ("m4" config), so we
    // deliberately do NOT firmware-map it here to avoid clashing with that.
    private static void MapSlots(int count)
    {
        _slots.Clear();
        if (count > 0)
            _slots["m1"] = 0;
        if (count > 1)
            _slots["m2"] = 1;
        if (count > 2)
            _slots["m3"] = 2;
        if (count >= 5)
            _slots["m5"] = count - 2;
    }

    /// <summary>True once <see cref="IsSupported"/> mapped a given slot.</summary>
    public static bool HasSlot(string name) => _slots.ContainsKey(name);

    // Curated opcode choices offered in the UI (id order = dropdown order).
    // "" is the firmware default for that slot. Label keys reuse existing
    // action_* entries where they overlap, else a dedicated mkey_* entry.
    private static readonly (string Id, string LabelKey)[] Choices =
    {
        ("", "mkey_default"),
        ("volume_up", "mkey_volume_up"),
        ("volume_down", "mkey_volume_down"),
        ("mute", "action_mute"),
        ("micmute_hw", "action_micmute"),
        ("play", "mkey_play"),
        ("media_next", "mkey_media_next"),
        ("media_previous", "mkey_media_prev"),
        ("backlight_up", "action_brightness_up"),
        ("backlight_down", "action_brightness_down"),
        ("performance", "action_performance"),
        ("touchpad", "action_touchpad"),
        ("sleep", "mkey_sleep"),
        ("calculator", "mkey_calculator"),
        ("screen_off", "mkey_screen_off"),
    };

    /// <summary>Ordered (id, label) choices for a firmware M-key dropdown.</summary>
    public static IEnumerable<(string Id, string Label)> OpcodeChoices()
    {
        foreach (var (id, key) in Choices)
            yield return (id, Labels.Get(key));
    }
}
