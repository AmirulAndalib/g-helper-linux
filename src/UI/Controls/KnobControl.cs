using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace GHelper.Linux.UI.Controls;

/// <summary>
/// Visual style for <see cref="KnobControl"/>. Switching styles changes only
/// the cosmetic render path; interaction, layout, and value semantics are
/// identical. Add new entries here and a matching Render method in the
/// control itself.
/// </summary>
public enum KnobStyle
{
    /// <summary>
    /// Black injection-moulded body, chrome vertical-gradient bezel, amber
    /// pointer line + arc, tick marks at every 30 deg. 1970s synth look.
    /// </summary>
    Moog,

    /// <summary>
    /// Dark recessed body with a bright LED arc around the periphery.
    /// Off segments stay dim; the lit portion of the ring glows with a
    /// faked bloom (stacked semi-transparent strokes). NI Maschine /
    /// modern controller aesthetic; reads cleanly on dark UIs.
    /// </summary>
    LedRing,
}

/// <summary>
/// A custom-drawn rotary potentiometer. Default style is <see cref="KnobStyle.Moog"/>:
/// black moulded body with a chrome bezel, single warm indicator line, and
/// an arc that fills around the periphery to show the current setting.
/// Switchable via <see cref="Style"/> to other looks (see <see cref="KnobStyle"/>).
///
/// Interaction model (matches established audio-plugin conventions, applies
/// regardless of style):
///   - Drag vertically: up increases, down decreases. Full range = 180 px.
///     Hold Shift while dragging for fine adjustment (10x slower).
///   - Mouse wheel: bumps by <see cref="WheelStep"/>; Shift+wheel = 1/10.
///   - Double-click: resets to <see cref="DefaultValue"/>.
///   - Right-click: same as double-click (alt reset gesture).
///
/// All values are stored as integers in <see cref="Value"/> for protocol
/// friendliness (we always speak per-mille over IPC). The visual rotation
/// maps Value -> 0..1 -> angle ~135 .. +135 degrees relative to 12 o'clock.
/// </summary>
public sealed class KnobControl : Control
{
    // ---- Public bindable-ish state (no DPs, callers set + invalidate) ----

    public int Minimum { get; set; }
    public int Maximum { get; set; } = 2000;
    public int DefaultValue { get; set; } = 1000;
    public int WheelStep { get; set; } = 25;       // ~2.5% per notch

    private KnobStyle _style = KnobStyle.Moog;
    /// <summary>Visual style. Changes trigger a redraw.</summary>
    public KnobStyle Style
    {
        get => _style;
        set
        {
            if (_style == value)
                return;
            _style = value;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Accent colour used by styles that take a hue (e.g. <see cref="KnobStyle.LedRing"/>
    /// uses it for the lit arc). Ignored by styles with fixed palettes
    /// (e.g. <see cref="KnobStyle.Moog"/> always uses amber). Default is
    /// the G-Helper cyan blue so the knob matches the rest of the UI.
    /// </summary>
    public Color AccentColor { get; set; } = Color.Parse("#4CC2FF");

    private int _value;
    public int Value
    {
        get => _value;
        set
        {
            int clamped = value;
            if (clamped < Minimum)
                clamped = Minimum;
            if (clamped > Maximum)
                clamped = Maximum;
            if (clamped == _value)
                return;
            _value = clamped;
            ValueChanged?.Invoke(clamped);
            InvalidateVisual();
        }
    }

    /// <summary>Fired whenever Value moves, including programmatic sets.</summary>
    public event Action<int>? ValueChanged;

    /// <summary>
    /// Optional formatter for the numeric label drawn under the knob. By
    /// default we show "{value}" verbatim; callers wanting "100%" or
    /// "+3 dB" supply a custom formatter.
    /// </summary>
    public Func<int, string>? LabelFormatter { get; set; }

    /// <summary>Optional short caption drawn above the knob (e.g. "Master").</summary>
    public string? Caption { get; set; }

    // ---- Drawing constants ------------------------------------------------

    private static readonly Color KnobInnerColor = Color.Parse("#0A0A0A");
    private static readonly Color KnobOuterColor = Color.Parse("#1F1F22");
    private static readonly Color BezelLight = Color.Parse("#8A8E94");
    private static readonly Color BezelDark = Color.Parse("#26282C");
    private static readonly Color PointerColor = Color.Parse("#FFC868");
    private static readonly Color ArcOff = Color.FromArgb(40, 255, 200, 104);
    private static readonly Color ArcOn = Color.FromArgb(180, 255, 200, 104);
    private static readonly Color TickColor = Color.FromArgb(120, 160, 160, 160);
    private static readonly Color CaptionColor = Color.Parse("#AAAAAA");
    private static readonly Color ValueColor = Color.Parse("#4CC2FF");

    /// <summary>Rotation sweep in degrees, centred on 12 o'clock. 270° total.</summary>
    private const double SweepDegrees = 270.0;

    // ---- Drag state -------------------------------------------------------

    private bool _dragging;
    private Point _dragStart;
    private int _dragStartValue;
    private bool _hover;

    public KnobControl()
    {
        Focusable = true;
        // Compact layout: caption (10 px) + knob box (~46 px) + value label
        // (10 px) + ~10 px of vertical breathing room = 76 px tall. Width
        // hugs the knob and its bezel ring.
        Width = 50;
        Height = 76;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    // ---- Input handlers ---------------------------------------------------

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var p = e.GetCurrentPoint(this);
        if (p.Properties.IsRightButtonPressed)
        {
            // Right-click resets, matches "scroll to default" muscle memory
            // from plugin UIs that don't have a double-click handler.
            Value = DefaultValue;
            e.Handled = true;
            return;
        }
        if (e.ClickCount >= 2)
        {
            Value = DefaultValue;
            e.Handled = true;
            return;
        }
        if (p.Properties.IsLeftButtonPressed)
        {
            _dragging = true;
            _dragStart = p.Position;
            _dragStartValue = Value;
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging)
            return;
        var pos = e.GetPosition(this);
        double dy = _dragStart.Y - pos.Y; // up = positive
        // Full range covered by 180 px of vertical drag. Shift slows 10x for
        // precision passes - same UX as most DAW plugin knobs.
        double pxPerFull = 180.0;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            pxPerFull *= 10.0;
        double frac = dy / pxPerFull;
        int delta = (int)Math.Round(frac * (Maximum - Minimum));
        Value = _dragStartValue + delta;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging)
        {
            _dragging = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        int step = WheelStep;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            step = Math.Max(1, step / 10);
        int delta = e.Delta.Y > 0 ? step : -step;
        Value = Value + delta;
        e.Handled = true;
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _hover = true;
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = false;
        InvalidateVisual();
    }

    // ---- Rendering --------------------------------------------------------

    /// <summary>
    /// Shared layout values reused by every style-specific renderer. Computed
    /// from <see cref="Bounds"/> + the presence of a caption.
    /// </summary>
    private readonly record struct KnobLayout(
        double W, double H,
        double KnobCx, double KnobCy, double KnobR,
        double CaptionH, double ValueH);

    private KnobLayout ComputeLayout()
    {
        double w = Bounds.Width;
        double h = Bounds.Height;
        double captionH = string.IsNullOrEmpty(Caption) ? 0 : 12;
        double valueH = 12;
        double knobBoxH = h - captionH - valueH - 2;
        double knobBoxY = captionH + 1;
        double knobBoxW = Math.Min(w, knobBoxH);
        double knobCx = w * 0.5;
        double knobCy = knobBoxY + knobBoxH * 0.5;
        double knobR = knobBoxW * 0.42;
        return new KnobLayout(w, h, knobCx, knobCy, knobR, captionH, valueH);
    }

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        var l = ComputeLayout();

        // Caption + value label are shared by all styles so they live up
        // here, not inside each style renderer.
        DrawCaption(ctx, l);
        switch (_style)
        {
            case KnobStyle.LedRing:
                RenderLedRing(ctx, l);
                break;
            case KnobStyle.Moog:
            default:
                RenderMoog(ctx, l);
                break;
        }
        DrawValueLabel(ctx, l);
    }

    private void DrawCaption(DrawingContext ctx, KnobLayout l)
    {
        if (string.IsNullOrEmpty(Caption))
            return;
        var ft = new FormattedText(
            Caption!,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            9,
            new SolidColorBrush(CaptionColor));
        ctx.DrawText(ft, new Point(l.KnobCx - ft.Width * 0.5, 0));
    }

    private void DrawValueLabel(DrawingContext ctx, KnobLayout l)
    {
        string label = LabelFormatter != null
            ? LabelFormatter(_value)
            : _value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var ft = new FormattedText(
            label,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            10,
            new SolidColorBrush(ValueColor));
        ctx.DrawText(ft, new Point(l.KnobCx - ft.Width * 0.5, l.H - l.ValueH - 1));
    }

    private double ValueFrac =>
        (double)(_value - Minimum) / Math.Max(1, Maximum - Minimum);

    // ----- Moog (current default style) -----------------------------------

    /// <summary>
    /// 1970s synth: chrome bezel + black moulded body + warm amber pointer
    /// + tick marks. Pure cosmetic; no interaction differences from any
    /// other style.
    /// </summary>
    private void RenderMoog(DrawingContext ctx, KnobLayout l)
    {
        double knobCx = l.KnobCx, knobCy = l.KnobCy, knobR = l.KnobR;

        // 1) Outer bezel: faux chrome ring with a vertical gradient so the
        //    upper rim catches highlight and the lower rim falls into shadow.
        var bezelGrad = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
        };
        bezelGrad.GradientStops.Add(new GradientStop(BezelLight, 0));
        bezelGrad.GradientStops.Add(new GradientStop(BezelDark, 1));
        double bezelR = knobR * 1.12;
        ctx.DrawEllipse(bezelGrad, null,
            new Point(knobCx, knobCy), bezelR, bezelR);

        // 2) Arc indicator: faint full track + bright fill to current angle.
        const double startAngleDeg = 270 - SweepDegrees / 2;
        double sweepDeg = SweepDegrees * ValueFrac;
        DrawArc(ctx, knobCx, knobCy, bezelR * 0.94, startAngleDeg, SweepDegrees,
                new Pen(new SolidColorBrush(ArcOff), 2.4));
        if (sweepDeg > 0.5)
            DrawArc(ctx, knobCx, knobCy, bezelR * 0.94, startAngleDeg, sweepDeg,
                    new Pen(new SolidColorBrush(ArcOn), 2.4));

        // 3) Knob body: radial gradient mimicking moulded plastic.
        var bodyGrad = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.35, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.5, 0.35, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.6, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.6, RelativeUnit.Relative),
        };
        bodyGrad.GradientStops.Add(new GradientStop(KnobOuterColor, 0));
        bodyGrad.GradientStops.Add(new GradientStop(KnobInnerColor, 1));
        ctx.DrawEllipse(bodyGrad, new Pen(new SolidColorBrush(BezelDark), 1),
            new Point(knobCx, knobCy), knobR, knobR);

        // 4) Tick marks at every 30°.
        for (int i = 0; i <= 9; i++)
        {
            double a = startAngleDeg + (SweepDegrees / 9.0) * i;
            double ar = a * Math.PI / 180.0;
            double rx0 = knobCx + Math.Cos(ar) * (knobR * 1.04);
            double ry0 = knobCy + Math.Sin(ar) * (knobR * 1.04);
            double rx1 = knobCx + Math.Cos(ar) * (knobR * 1.10);
            double ry1 = knobCy + Math.Sin(ar) * (knobR * 1.10);
            ctx.DrawLine(new Pen(new SolidColorBrush(TickColor), 1),
                new Point(rx0, ry0), new Point(rx1, ry1));
        }

        // 5) Pointer line: warm amber, 25..90% of radius.
        double pointerAngle = startAngleDeg + sweepDeg;
        double pa = pointerAngle * Math.PI / 180.0;
        double px0 = knobCx + Math.Cos(pa) * (knobR * 0.25);
        double py0 = knobCy + Math.Sin(pa) * (knobR * 0.25);
        double px1 = knobCx + Math.Cos(pa) * (knobR * 0.90);
        double py1 = knobCy + Math.Sin(pa) * (knobR * 0.90);
        ctx.DrawLine(
            new Pen(new SolidColorBrush(PointerColor), 3,
                lineCap: PenLineCap.Round),
            new Point(px0, py0), new Point(px1, py1));

        // 6) Centre dot - subtle inset, slightly brighter on hover.
        byte dotAlpha = (byte)(_hover ? 110 : 80);
        ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(dotAlpha, 255, 255, 255)),
                        null,
                        new Point(knobCx, knobCy),
                        knobR * 0.07, knobR * 0.07);
    }

    // ----- LED ring (modern controller style) ------------------------------

    /// <summary>
    /// Modern controller look: dark recessed body with a bright LED arc
    /// around the periphery in <see cref="AccentColor"/>. The ring is split
    /// into a dim "off" track (faint outline) and a "lit" arc that fills
    /// from the 12 o'clock min angle to the current value angle. A short
    /// pointer line at the value angle, centred dot for pivot.
    ///
    /// Bloom is faked by drawing the lit arc three times: outer wide low-
    /// alpha (haze), mid medium-alpha, inner thin solid (the LED itself).
    /// No Avalonia effect chain needed; pure DrawingContext primitives.
    /// </summary>
    private void RenderLedRing(DrawingContext ctx, KnobLayout l)
    {
        double knobCx = l.KnobCx, knobCy = l.KnobCy, knobR = l.KnobR;
        const double startAngleDeg = 270 - SweepDegrees / 2;
        double sweepDeg = SweepDegrees * ValueFrac;

        Color accent = AccentColor;
        // Off-track: very dim cool grey-blue, sits just outside the recessed
        // body so the lit portion gets clear contrast against it.
        var offTrack = new SolidColorBrush(Color.FromArgb(60, 80, 100, 120));
        // Lit arc layered for bloom.
        var litHaze = new SolidColorBrush(Color.FromArgb(36, accent.R, accent.G, accent.B));
        var litMid = new SolidColorBrush(Color.FromArgb(110, accent.R, accent.G, accent.B));
        var litCore = new SolidColorBrush(Color.FromArgb(255, accent.R, accent.G, accent.B));

        double ringR = knobR * 1.10;

        // 1) Recessed body: radial gradient that goes darker at the centre
        //    than at the edges, creating a sunken-into-the-panel illusion.
        //    Edge gets a thin lighter stroke for the "lens" rim.
        var bodyGrad = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.55, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.55, RelativeUnit.Relative),
        };
        bodyGrad.GradientStops.Add(new GradientStop(Color.Parse("#070A0D"), 0));
        bodyGrad.GradientStops.Add(new GradientStop(Color.Parse("#1A2129"), 1));
        ctx.DrawEllipse(bodyGrad,
            new Pen(new SolidColorBrush(Color.FromArgb(110, 80, 100, 120)), 1),
            new Point(knobCx, knobCy), knobR, knobR);

        // 2) Off-track ring: full 270° sweep, thin, dim.
        DrawArc(ctx, knobCx, knobCy, ringR, startAngleDeg, SweepDegrees,
                new Pen(offTrack, 2.0, lineCap: PenLineCap.Round));

        // 3) Lit arc with stacked bloom layers. Order matters: haze first
        //    (widest stroke, low alpha) so the core sits on top.
        if (sweepDeg > 0.3)
        {
            DrawArc(ctx, knobCx, knobCy, ringR, startAngleDeg, sweepDeg,
                    new Pen(litHaze, 7.0, lineCap: PenLineCap.Round));
            DrawArc(ctx, knobCx, knobCy, ringR, startAngleDeg, sweepDeg,
                    new Pen(litMid, 4.0, lineCap: PenLineCap.Round));
            DrawArc(ctx, knobCx, knobCy, ringR, startAngleDeg, sweepDeg,
                    new Pen(litCore, 2.0, lineCap: PenLineCap.Round));
        }

        // 4) Indicator dot at the head of the lit arc. Sits exactly where
        //    the lit portion ends, gives a clear "you are here" anchor.
        double headAngle = startAngleDeg + sweepDeg;
        double ha = headAngle * Math.PI / 180.0;
        double hx = knobCx + Math.Cos(ha) * ringR;
        double hy = knobCy + Math.Sin(ha) * ringR;
        // Bigger soft outer glow on hover for tactile feedback.
        double glowR = _hover ? 4.5 : 3.5;
        ctx.DrawEllipse(litHaze, null, new Point(hx, hy), glowR + 2, glowR + 2);
        ctx.DrawEllipse(litMid, null, new Point(hx, hy), glowR + 1, glowR + 1);
        ctx.DrawEllipse(litCore, null, new Point(hx, hy), glowR - 1, glowR - 1);

        // 5) Short inner pointer line so the body itself shows the value
        //    direction without needing to look at the ring. Runs ~40..80%
        //    of radius, same warm accent. Smaller than the Moog pointer.
        double pa = headAngle * Math.PI / 180.0;
        double px0 = knobCx + Math.Cos(pa) * (knobR * 0.40);
        double py0 = knobCy + Math.Sin(pa) * (knobR * 0.40);
        double px1 = knobCx + Math.Cos(pa) * (knobR * 0.80);
        double py1 = knobCy + Math.Sin(pa) * (knobR * 0.80);
        ctx.DrawLine(
            new Pen(litCore, 2.0, lineCap: PenLineCap.Round),
            new Point(px0, py0), new Point(px1, py1));

        // 6) Subtle pivot dot at the centre - faint accent-tinted disc so
        //    the body still feels alive rather than empty. Slightly
        //    brighter on hover.
        byte pivotAlpha = (byte)(_hover ? 90 : 55);
        ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(pivotAlpha, accent.R, accent.G, accent.B)),
                        null,
                        new Point(knobCx, knobCy),
                        knobR * 0.08, knobR * 0.08);
    }

    /// <summary>
    /// Draw an arc stroke using <see cref="StreamGeometry"/> + ArcSegment.
    /// Angles are in degrees, increasing clockwise, 0 = east. We sweep CCW
    /// when the angle delta is negative.
    /// </summary>
    private static void DrawArc(DrawingContext ctx, double cx, double cy,
                                double r, double startDeg, double sweepDeg, Pen pen)
    {
        double endDeg = startDeg + sweepDeg;
        double sRad = startDeg * Math.PI / 180.0;
        double eRad = endDeg * Math.PI / 180.0;
        var start = new Point(cx + Math.Cos(sRad) * r, cy + Math.Sin(sRad) * r);
        var end = new Point(cx + Math.Cos(eRad) * r, cy + Math.Sin(eRad) * r);

        var sg = new StreamGeometry();
        using (var s = sg.Open())
        {
            s.BeginFigure(start, false);
            s.ArcTo(end,
                new Size(r, r),
                0,
                sweepDeg > 180,
                SweepDirection.Clockwise);
            s.EndFigure(false);
        }
        ctx.DrawGeometry(null, pen, sg);
    }
}
