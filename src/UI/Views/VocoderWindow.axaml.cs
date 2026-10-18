using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GHelper.Linux.Helpers;
using GHelper.Linux.I18n;

namespace GHelper.Linux.UI.Views;

/// <summary>
/// Per-effect window for the vocoder and the voice effects that ride on it
/// (pitch shift, autotune, band-pass, bit crush, stutter). Every control
/// pushes through <see cref="AudioHelper"/>, which also writes through to
/// <see cref="AudioState"/> + AppConfig.
///
/// The helper bypasses all of it while the vocoder is off, so the header
/// checkbox mirrors that switch, and picking a preset or moving any control
/// turns it on: otherwise the window looks alive and nothing is heard.
///
/// The "Follow voice pitch" toggle reshapes the carrier slider in place:
/// when off it's the fixed carrier frequency 50..440 Hz, when on it
/// becomes a -24..+24 semitone transposition relative to the tracked
/// voice pitch (0 = robot at your pitch).
/// </summary>
public partial class VocoderWindow : Window
{
    private bool _loading = true;

    // Frames can still carry the old on/off flag for a moment after a local
    // toggle; ignore them briefly so the header checkbox does not bounce.
    private DateTime _ignoreFlagsUntil = DateTime.MinValue;

    // High cut slider: the right end means "off" (helper value 0).
    private const int HighCutOff = 12000;

    /// <summary>
    /// One canned voice. A preset stamps every control in the window:
    ///
    ///   - vocoder (Mix &gt; 0): formant carrier voices (Daft Punk,
    ///     Kraftwerk, Matrix, Cylon)
    ///   - pitch shifter / autotune: time-domain re-pitching of the voice
    ///     (Vader = -5 st, Chipmunk = +12 st)
    ///   - band-pass, bit crush, stutter: telephone band, lo-fi and the
    ///     Cylon chop
    /// </summary>
    private readonly record struct VocoderPreset(
        string Name,
        int Mix,                       // 0..1000 per-mille (vocoder dry/wet)
        int CarrierHz,                 // 50..440 (only used when Follow=false)
        int Detune,                    // 0..200 per-mille
        int AttackMs,                  // 1..100
        int ReleaseMs,                 // 5..500
        bool Follow,
        int VocoderPitchShift,         // -24..+24 (only used when Follow=true)
        int PitchShiftCentisemis,      // -2400..+2400 voice pitch shift
        bool Autotune,                 // pitch-shifter driven by autotune
        int AutotuneTargetHz,          // 0 = chromatic snap, >0 = monotone
        int BitcrushBits,              // 0 = bypass, 1..15 levels
        int BitcrushDownsample,        // 1 = no downsampling
        int MatrixIntensity,           // 0 = clean carrier .. 1000 = full Sentinel
        int BandpassHpfHz,             // 0 = bypass; e.g. 300 for telephone
        int BandpassLpfHz,             // 0 = bypass; e.g. 3000 for muffled
        int StutterHz                  // 0 = bypass; 4..8 for Cylon cadence
    );

    private static readonly VocoderPreset[] Presets =
    [
        // Daft Punk - "Around the World" / "Get Lucky": carrier vocoder
        // following the singer's melody. Clean two-osc carrier (no ring
        // mod), tight detune, fast envelopes. No band-pass; the vocoder
        // bands themselves shape the spectrum.
        //                     mix car  det atk rel  follow vshift psh   AT  ATtgt bcb bcds  matrix  hpf  lpf  stut
        new("Daft Punk",       900, 110,  50,  2,  12, true,    0,     0, false,    0,  0,    1,    150,    0,    0,   0),

        // Chipmunk - tape sped up 2x = +12 st pitch with formants riding
        // along. Pure granular shifter, no vocoder. Light HPF trims the
        // chest rumble that doesn't survive an octave-up shift sensibly.
        new("Chipmunk",          0, 110,   0,  2,  15, false,   0,  1200, false,    0,  0,    1,      0,  150,    0,   0),

        // Cylon - Battlestar Galactica machine voice. Stutter gate at 6 Hz
        // plus medium Matrix intensity gives the rhythmic robot speech.
        // Fixed deep carrier, slow envelope so each chop sustains.
        new("Cylon",           900,  90, 160, 10,  70, false,   0,     0, false,    0,  0,    1,    450,    0,    0,   6),

        // Darth Vader - pitched down 5 st with a 2500 Hz LPF for the
        // "speaking through a helmet" character. Vocoder OFF - Vader is the
        // speaker, not a robot. Slight HPF (80 Hz) trims the rumble.
        new("Darth Vader",       0, 110,   0,  5,  30, false,   0,  -500, false,    0,  0,    1,      0,   80, 2500,   0),

        // Kraftwerk - "Trans-Europe Express" analog EMS Vocoder. Clean
        // saw+square carrier (Matrix = 0), fixed 140 Hz, tight detune,
        // snappy envelope. Mostly wet so the formants dominate.
        new("Kraftwerk",       950, 140,  10,  3,  15, false,   0,     0, false,    0,  0,    1,      0,    0,    0,   0),

        // Matrix Agent - Sentinel comm-rig. Full Matrix intensity (ring mod
        // + sub-octave + tanh drive + tremolo) on a deep 70 Hz carrier with
        // the voice pitched down 2 st, over a 300-3400 Hz comm channel.
        new("Matrix Agent",    900,  70,  90, 10,  55, false,   0,  -200, false,    0,  0,    1,   1000,  300, 3400,   0),

        // Robot Phone - 300-3400 Hz telephone band + 8-bit / 2x downsample
        // crush for the quantisation noise. No vocoder, no shift.
        new("Robot Phone",       0, 110,   0,  5,  25, false,   0,     0, false,    0,  8,    2,      0,  300, 3400,   0),

        // Sci-Fi Alien - "almost forming words". Pitch shift +7 st + heavy
        // Matrix vocoder + tinny-radio band-pass (700-3200 Hz).
        new("Sci-Fi Alien",    500, 110, 130,  2,  10, false,   0,   700, false,    0,  0,    1,    700,  700, 3200,   0),
    ];

    public VocoderWindow()
    {
        InitializeComponent();
        comboPreset.ItemsSource = Presets.Select(p => p.Name).ToList();
        ApplyLabels();
        HydrateFromState();
        Labels.LanguageChanged += OnLanguageChanged;
        AudioHelper.Instance.FrameReceived += OnFrame;
        Closed += (_, _) =>
        {
            Labels.LanguageChanged -= OnLanguageChanged;
            AudioHelper.Instance.FrameReceived -= OnFrame;
        };
        _loading = false;
    }

    private void OnLanguageChanged() => Dispatcher.UIThread.Post(() =>
    {
        ApplyLabels();
        UpdateLabels();
    });

    private void ApplyLabels()
    {
        Title = Labels.Get("audio_vocoder_title");
        checkEnabled.Content = Labels.Get("audio_vocoder_title");
        labelDescription.Text = Labels.Get("audio_vocoder_description");
        labelPresetHeader.Text = Labels.Get("audio_vocoder_preset");
        labelDryWet.Text = Labels.Get("audio_vocoder_drywet");
        labelDetuneHeader.Text = Labels.Get("audio_vocoder_detune");
        labelMatrixHeader.Text = Labels.Get("audio_vocoder_matrix");
        labelAttackHeader.Text = Labels.Get("audio_vocoder_attack");
        labelReleaseHeader.Text = Labels.Get("audio_vocoder_release");
        labelVoiceHeader.Text = Labels.Get("audio_vocoder_voice");
        labelPitchHeader.Text = Labels.Get("audio_vocoder_pitch");
        checkAutotune.Content = Labels.Get("audio_vocoder_autotune");
        labelLowCutHeader.Text = Labels.Get("audio_vocoder_lowcut");
        labelHighCutHeader.Text = Labels.Get("audio_vocoder_highcut");
        labelCrushHeader.Text = Labels.Get("audio_vocoder_crush");
        labelDownsampleHeader.Text = Labels.Get("audio_vocoder_downsample");
        labelStutterHeader.Text = Labels.Get("audio_vocoder_stutter");
        buttonReset.Content = Labels.Get("audio_reset");
        checkFollow.Content = Labels.Get("audio_vocoder_follow");
        ToolTip.SetTip(checkFollow, Labels.Get("audio_vocoder_follow_tooltip"));
        labelCarrierHeader.Text = Labels.Get(checkFollow.IsChecked == true
            ? "audio_vocoder_transpose"
            : "audio_vocoder_carrier");
    }

    // --- state <-> controls -------------------------------------------------

    private static int CrushToSlider(int bits) => bits <= 0 ? 0 : 16 - Math.Clamp(bits, 1, 15);
    private static int SliderToCrush(int v) => v <= 0 ? 0 : 16 - v;
    private static int HighCutToSlider(int lpf) => lpf <= 0 || lpf >= HighCutOff ? HighCutOff : Math.Clamp(lpf, 1000, HighCutOff - 100);
    private static int SliderToHighCut(int v) => v >= HighCutOff ? 0 : v;

    private void HydrateFromState()
    {
        bool wasLoading = _loading;
        _loading = true;
        var s = AudioState.Instance;
        checkEnabled.IsChecked = s.VocoderOn;
        comboPreset.SelectedIndex = Array.FindIndex(Presets, p => p.Name == s.VocoderPreset);
        sliderMix.Value = s.VocoderMix;
        sliderDetune.Value = s.VocoderDetune;
        sliderAttack.Value = s.VocoderAttackMs;
        sliderRelease.Value = s.VocoderReleaseMs;
        sliderMatrix.Value = s.VocoderMatrixIntensity;
        checkFollow.IsChecked = s.VocoderFollow;
        ConfigureCarrierSlider(s.VocoderFollow);
        sliderPitch.Value = Math.Round(s.VoicePitchShiftCentisemis / 100.0);
        checkAutotune.IsChecked = s.VoiceAutotuneOn;
        sliderLowCut.Value = s.VoiceBandpassHpfHz;
        sliderHighCut.Value = HighCutToSlider(s.VoiceBandpassLpfHz);
        sliderCrush.Value = CrushToSlider(s.VoiceBitcrushBits);
        sliderDownsample.Value = Math.Max(1, s.VoiceBitcrushDownsample);
        sliderStutter.Value = s.VoiceStutterHz;
        UpdateLabels();
        _loading = wasLoading;
    }

    /// <summary>
    /// Reconfigure the carrier slider in place to either Hz mode (50..440)
    /// or semitone mode (-24..+24), reading the matching value from state.
    /// </summary>
    private void ConfigureCarrierSlider(bool follow)
    {
        bool wasLoading = _loading;
        _loading = true;
        var s = AudioState.Instance;
        if (follow)
        {
            sliderCarrier.Minimum = -24;
            sliderCarrier.Maximum = 24;
            sliderCarrier.TickFrequency = 1;
            sliderCarrier.Value = s.VocoderPitchShift;
            labelCarrierHeader.Text = Labels.Get("audio_vocoder_transpose");
        }
        else
        {
            sliderCarrier.Minimum = 50;
            sliderCarrier.Maximum = 440;
            sliderCarrier.TickFrequency = 10;
            sliderCarrier.Value = s.VocoderCarrierHz;
            labelCarrierHeader.Text = Labels.Get("audio_vocoder_carrier");
        }
        _loading = wasLoading;
    }

    private static string FormatSemitones(int s) => s == 0 ? "0 st" : (s > 0 ? "+" : "") + s + " st";

    private void UpdateLabels()
    {
        var s = AudioState.Instance;
        string off = Labels.Get("off");
        labelMix.Text = $"{(int)sliderMix.Value / 10}%";
        labelCarrier.Text = s.VocoderFollow ? FormatSemitones(s.VocoderPitchShift) : $"{s.VocoderCarrierHz} Hz";
        labelDetune.Text = $"{(int)sliderDetune.Value / 10}%";
        labelMatrix.Text = $"{(int)sliderMatrix.Value / 10}%";
        labelAttack.Text = $"{(int)sliderAttack.Value} ms";
        labelRelease.Text = $"{(int)sliderRelease.Value} ms";
        labelPitch.Text = FormatSemitones((int)sliderPitch.Value);
        int low = (int)sliderLowCut.Value;
        labelLowCut.Text = low == 0 ? off : $"{low} Hz";
        int high = SliderToHighCut((int)sliderHighCut.Value);
        labelHighCut.Text = high == 0 ? off : $"{high} Hz";
        int bits = SliderToCrush((int)sliderCrush.Value);
        labelCrush.Text = bits == 0 ? off : $"{bits} bit";
        int ds = (int)sliderDownsample.Value;
        labelDownsample.Text = ds <= 1 ? off : $"{ds}x";
        int stut = (int)sliderStutter.Value;
        labelStutter.Text = stut == 0 ? off : $"{stut} Hz";

        // Controls that cannot be heard right now are greyed out: the robot
        // section does nothing at 0% wet, and autotune drives the shifter
        // instead of the pitch slider.
        panelRobot.IsEnabled = sliderMix.Value > 0;
        panelPitch.IsEnabled = checkAutotune.IsChecked != true;
    }

    // --- user edits ---------------------------------------------------------

    /// <summary>Turn the vocoder on if a change was made while it was off.</summary>
    private void EnsureEnabled()
    {
        if (AudioState.Instance.VocoderOn)
            return;
        AudioHelper.Instance.SetVocoder(true);
        _ignoreFlagsUntil = DateTime.UtcNow.AddMilliseconds(500);
        bool wasLoading = _loading;
        _loading = true;
        checkEnabled.IsChecked = true;
        _loading = wasLoading;
    }

    /// <summary>A control moved away from the preset: drop the selection.</summary>
    private void UserEdited()
    {
        if (comboPreset.SelectedIndex >= 0)
        {
            bool wasLoading = _loading;
            _loading = true;
            comboPreset.SelectedIndex = -1;
            _loading = wasLoading;
        }
        AudioState.Instance.VocoderPreset = "";
        EnsureEnabled();
        UpdateLabels();
    }

    private void CheckEnabled_Changed(object? sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        _ignoreFlagsUntil = DateTime.UtcNow.AddMilliseconds(500);
        AudioHelper.Instance.SetVocoder(checkEnabled.IsChecked == true);
    }

    /// <summary>Mix, detune, attack, release: the VOP parameter set.</summary>
    private void OnVocoderChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_loading)
            return;
        PushVocoder();
        UserEdited();
    }

    /// <summary>
    /// Carrier slider: Hz when follow is off, semitones when it is on.
    /// </summary>
    private void SliderCarrier_Changed(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_loading)
            return;
        var s = AudioState.Instance;
        if (s.VocoderFollow)
            s.VocoderPitchShift = (int)sliderCarrier.Value;
        else
            s.VocoderCarrierHz = (int)sliderCarrier.Value;
        PushVocoder();
        UserEdited();
    }

    private void CheckFollow_Changed(object? sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        AudioState.Instance.VocoderFollow = checkFollow.IsChecked == true;
        ConfigureCarrierSlider(AudioState.Instance.VocoderFollow);
        PushVocoder();
        UserEdited();
    }

    /// <summary>Metallic, pitch, cuts, crush, stutter.</summary>
    private void OnVoiceChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_loading)
            return;
        PushVoice();
        UserEdited();
    }

    private void CheckAutotune_Changed(object? sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        var helper = AudioHelper.Instance;
        // The UI offers chromatic snap only; clear any monotone target.
        helper.SetVoiceAutotuneTarget(0);
        helper.SetAutotune(checkAutotune.IsChecked == true);
        UserEdited();
    }

    private void PushVocoder()
    {
        var s = AudioState.Instance;
        AudioHelper.Instance.SetVocoder((int)sliderMix.Value, s.VocoderCarrierHz,
                                        (int)sliderAttack.Value, (int)sliderRelease.Value,
                                        (int)sliderDetune.Value,
                                        s.VocoderFollow, s.VocoderPitchShift);
    }

    private void PushVoice()
    {
        var helper = AudioHelper.Instance;
        helper.SetVocoderMatrix((int)sliderMatrix.Value);
        helper.SetPitchShift((int)sliderPitch.Value * 100);
        helper.SetVoiceBandpass((int)sliderLowCut.Value, SliderToHighCut((int)sliderHighCut.Value));
        helper.SetBitcrush(SliderToCrush((int)sliderCrush.Value), (int)sliderDownsample.Value);
        helper.SetVoiceStutter((int)sliderStutter.Value, AudioState.Instance.VoiceStutterDutyMille);
    }

    // --- presets / reset ----------------------------------------------------

    private void ComboPreset_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading)
            return;
        int idx = comboPreset.SelectedIndex;
        if (idx < 0 || idx >= Presets.Length)
            return;
        ApplyPreset(Presets[idx]);
    }

    private void ApplyPreset(VocoderPreset p)
    {
        // The helper setters persist into AudioState; the controls are then
        // re-read from it so window and sound cannot disagree.
        var helper = AudioHelper.Instance;
        helper.SetVocoder(p.Mix, p.CarrierHz, p.AttackMs, p.ReleaseMs,
                          p.Detune, p.Follow, p.VocoderPitchShift);
        helper.SetPitchShift(p.PitchShiftCentisemis);
        helper.SetVoiceAutotuneTarget(p.AutotuneTargetHz);
        helper.SetAutotune(p.Autotune);
        helper.SetBitcrush(p.BitcrushBits, p.BitcrushDownsample);
        helper.SetVocoderMatrix(p.MatrixIntensity);
        helper.SetVoiceBandpass(p.BandpassHpfHz, p.BandpassLpfHz);
        helper.SetVoiceStutter(p.StutterHz, 500);
        AudioState.Instance.VocoderPreset = p.Name;
        HydrateFromState();
        EnsureEnabled();
    }

    private void ButtonReset_Click(object? sender, RoutedEventArgs e)
    {
        var s = AudioState.Instance;
        s.ResetVocoder();
        var helper = AudioHelper.Instance;
        helper.SetVocoder(s.VocoderMix, s.VocoderCarrierHz,
                          s.VocoderAttackMs, s.VocoderReleaseMs,
                          s.VocoderDetune,
                          s.VocoderFollow, s.VocoderPitchShift);
        helper.SetPitchShift(s.VoicePitchShiftCentisemis);
        helper.SetVoiceAutotuneTarget(s.VoiceAutotuneTargetHz);
        helper.SetAutotune(s.VoiceAutotuneOn);
        helper.SetBitcrush(s.VoiceBitcrushBits, s.VoiceBitcrushDownsample);
        helper.SetVocoderMatrix(s.VocoderMatrixIntensity);
        helper.SetVoiceBandpass(s.VoiceBandpassHpfHz, s.VoiceBandpassLpfHz);
        helper.SetVoiceStutter(s.VoiceStutterHz, s.VoiceStutterDutyMille);
        HydrateFromState();
    }

    // --- live feedback ------------------------------------------------------

    private void OnFrame(AudioFrame f)
    {
        float hz = f.TrackedPitchHz;
        bool on = f.VocoderOn;
        Dispatcher.UIThread.Post(() =>
        {
            // Live tracked pitch next to the follow toggle shows whether
            // the tracker has a lock.
            labelTrackedPitch.Text = hz > 1.0f ? $"~{hz:F0} Hz" : "-";

            // Keep the header in step with the Audio window tile and hotkey.
            if (DateTime.UtcNow >= _ignoreFlagsUntil && checkEnabled.IsChecked != on)
            {
                bool wasLoading = _loading;
                _loading = true;
                checkEnabled.IsChecked = on;
                _loading = wasLoading;
            }
        });
    }
}
