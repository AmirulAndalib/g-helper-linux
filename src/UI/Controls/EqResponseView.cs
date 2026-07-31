using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace GHelper.Linux.UI.Controls;

/// <summary>
/// FL Studio Parametric EQ 2 style display. Shows the combined frequency
/// response of N biquad bands across a log-frequency axis. Each band has a
/// colored handle which can be dragged: X = frequency, Y = gain. The
/// <see cref="BandChanged"/> event fires after a drag commits.
/// </summary>
public sealed class EqResponseView : Control
{
    public const int NumBands = 9;

    /// <summary>Per-band tuple: (type, freqHz, qMille, gainCentiDb).</summary>
    public (int type, int freqHz, int qMille, int gainCentiDb)[] Bands { get; set; }
        = new (int, int, int, int)[NumBands];

    /// <summary>
    /// Minimum / maximum Q values (in mille = Q * 1000). 100 → very wide
    /// (gentle slope), 10000 → very narrow / sharp resonance. Matches the
    /// range users expect from FL Studio's Parametric EQ 2.
    /// </summary>
    public const int MinQMille = 100;
    public const int MaxQMille = 10000;

    /// <summary>Live pre-EQ spectrum overlay (64 bins, -80..0 dB).</summary>
    public float[]? SpectrumIn { get; set; }

    /// <summary>Live post-chain spectrum overlay (64 bins, -80..0 dB).</summary>
    public float[]? SpectrumOut { get; set; }

    public event Action<int>? BandChanged;

    public int PostGainCentiDb { get; set; }

    /// <summary>Fires after a line-drag tick changes the post-EQ gain.</summary>
    public event Action? PostGainChanged;

    private int _dragBand = -1;
    private int _hoverBand = -1;

    private bool _dragLineOnly;
    private double _dragInitialPressY;
    private int _dragInitialPostGainCentiDb;

    private const int PostGainMinCentiDb = -3600;
    private const int PostGainMaxCentiDb = 3600;

    /// <summary>
    /// Fires after a Q change via scroll wheel (or other internal mutator)
    /// so the host window can persist + push to the helper. The argument is
    /// the band index that changed.
    /// </summary>
    public event Action<int>? BandQChanged;

    private static readonly Color[] BandColors =
    {
        Color.Parse("#FF6B6B"), // red
        Color.Parse("#FFA500"), // orange
        Color.Parse("#FFD700"), // gold
        Color.Parse("#FFE066"), // light yellow
        Color.Parse("#50C878"), // green
        Color.Parse("#4CC2FF"), // cyan
        Color.Parse("#7FB3FF"), // light blue
        Color.Parse("#C778DD"), // magenta
        Color.Parse("#E060A0"), // pink
    };

    private const double FMin = 20.0;
    private const double FMax = 20000.0;
    private const double GMin = -18.0;
    private const double GMax = 18.0;

    public EqResponseView()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    private double FreqToX(double f, double w)
    {
        double t = (Math.Log10(f) - Math.Log10(FMin)) / (Math.Log10(FMax) - Math.Log10(FMin));
        return t * w;
    }
    private double XToFreq(double x, double w)
    {
        double t = x / w;
        return Math.Pow(10.0, Math.Log10(FMin) + t * (Math.Log10(FMax) - Math.Log10(FMin)));
    }
    private double GainToY(double g, double h) => h * (1.0 - (g - GMin) / (GMax - GMin));
    private double YToGain(double y, double h) => GMin + (1.0 - y / h) * (GMax - GMin);

    /// <summary>
    /// Evaluate the combined magnitude response (dB) at frequency f. Sums
    /// the analytic per-band shapes (peak, shelves, hp/lp/notch) and adds
    /// the uniform post-EQ gain. The returned value is what the line on
    /// screen represents - bands plus makeup gain - so the line always
    /// matches the audible signal.
    /// </summary>
    private double Response(double f, double fs = 48000.0)
    {
        double total = 0.0;
        for (int i = 0; i < NumBands; i++)
        {
            var b = Bands[i];
            double freq = b.freqHz <= 0 ? 1000 : b.freqHz;
            double q = (b.qMille <= 0 ? 707 : b.qMille) / 1000.0;
            double gainDb = b.gainCentiDb / 100.0;
            double ratio = f / freq;
            double mag = 0.0;
            switch (b.type)
            {
                case 0: // peak
                    {
                        double bw = freq / q;
                        double x = (f * f - freq * freq) / (f * bw);
                        mag = gainDb / (1.0 + x * x);
                        break;
                    }
                case 1: // low-shelf
                    {
                        double t = 1.0 / (1.0 + Math.Pow(ratio, 2.0));
                        mag = gainDb * t;
                        break;
                    }
                case 2: // high-shelf
                    {
                        double t = Math.Pow(ratio, 2.0) / (1.0 + Math.Pow(ratio, 2.0));
                        mag = gainDb * t;
                        break;
                    }
                case 3: // high-pass: -12 dB/oct below f0
                    if (f < freq)
                        mag = -40.0 * Math.Log10(freq / f);
                    if (mag < -80)
                        mag = -80;
                    break;
                case 4: // low-pass
                    if (f > freq)
                        mag = -40.0 * Math.Log10(f / freq);
                    if (mag < -80)
                        mag = -80;
                    break;
                case 5: // notch
                    {
                        double bw = freq / q;
                        double x = (f * f - freq * freq) / (f * bw);
                        mag = -18.0 / (1.0 + x * x);
                        break;
                    }
            }
            total += mag;
        }
        // Uniform post-EQ gain: shifts the entire line by a constant dB.
        // This is the parameter the user adjusts when dragging the line.
        return total + PostGainCentiDb / 100.0;
    }

    /// <summary>
    /// Draw one spectrum as a translucent bar set spanning the log-frequency
    /// X axis. <paramref name="color"/> alpha controls how see-through the
    /// bars are; both layers should use modest alpha so they stack cleanly.
    /// </summary>
    private void DrawSpectrum(DrawingContext context, float[]? bins, Color color,
                              double w, double h)
    {
        if (bins == null || bins.Length < 2)
            return;
        var fill = new SolidColorBrush(color);
        const double binFmin = 50.0;
        const double binFmax = 16000.0;
        double barW = w / bins.Length;
        for (int i = 0; i < bins.Length; i++)
        {
            double t = (double)i / (bins.Length - 1);
            double f = binFmin * Math.Pow(binFmax / binFmin, t);
            double x = FreqToX(f, w);
            double db = bins[i];
            if (db < -80)
                db = -80;
            double mag = (db + 80.0) / 80.0;       // 0..1
            double bh = h * mag * 0.7;             // dampen to leave room for curve
            context.FillRectangle(fill, new Rect(x - barW * 0.5, h - bh, barW, bh));
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        double w = Bounds.Width;
        double h = Bounds.Height;
        if (w <= 0 || h <= 0)
            return;

        // background
        context.FillRectangle(new SolidColorBrush(Color.Parse("#0F1216")),
                              new Rect(0, 0, w, h));

        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(80, 160, 160, 160)), 1);
        var axisPen = new Pen(new SolidColorBrush(Color.FromArgb(140, 200, 200, 200)), 1);

        // Vertical decade lines
        double[] decades = { 50, 100, 200, 500, 1000, 2000, 5000, 10000 };
        foreach (var f in decades)
        {
            double x = FreqToX(f, w);
            context.DrawLine(gridPen, new Point(x, 0), new Point(x, h));
        }

        // Horizontal gain lines: 0, ±6, ±12 dB
        foreach (var g in new[] { -12.0, -6.0, 0.0, 6.0, 12.0 })
        {
            double y = GainToY(g, h);
            var pen = (g == 0.0) ? axisPen : gridPen;
            context.DrawLine(pen, new Point(0, y), new Point(w, y));
        }

        // Spectrum overlays: input (cyan) and output (green) rendered as
        // semi-transparent stacked bars so both spectra are visible at the
        // same time. Output drawn last (on top) so the post-chain shape is
        // emphasized while raw input remains visible underneath.
        DrawSpectrum(context, SpectrumIn, Color.FromArgb(110, 76, 194, 255), w, h);
        DrawSpectrum(context, SpectrumOut, Color.FromArgb(140, 80, 200, 120), w, h);

        // Frequency response curve. <see cref="Response"/> already includes
        // the post-EQ uniform gain so dragging the line up or down is a
        // literal vertical translation - shape preserved by construction
        // both during and after a drag.
        var curvePen = new Pen(new SolidColorBrush(Color.Parse("#FFFFFF")), 2);
        Point? prev = null;
        for (int px = 0; px <= (int)w; px += 2)
        {
            double f = XToFreq(px, w);
            double g = Response(f);
            double y = GainToY(g, h);
            var pt = new Point(px, y);
            if (prev.HasValue)
                context.DrawLine(curvePen, prev.Value, pt);
            prev = pt;
        }

        // Band handles - always drawn (even at 0 gain) so the user can
        // grab them; hovered or active band is enlarged + shows Q readout.
        // Each handle gets an outer "aura" ring whose arc length encodes
        // the absolute gain (0..±18 dB -> 0..360 deg) - reminiscent of a
        // circular progress bar around a synth knob.
        double postGainDb = PostGainCentiDb / 100.0;
        for (int i = 0; i < NumBands; i++)
        {
            var b = Bands[i];
            double fx = FreqToX(b.freqHz, w);
            // Handles ride the response curve at the band's frequency, so
            // the visual Y reflects band gain + the uniform post-EQ offset.
            // This keeps each handle vertically aligned with the curve as
            // the user drags the whole line up or down.
            double yy = GainToY(b.gainCentiDb / 100.0 + postGainDb, h);
            var bandColor = BandColors[i];
            var bandBrush = new SolidColorBrush(bandColor);
            bool emph = (i == _hoverBand) || (i == _dragBand);
            double radius = emph ? 10 : 8;
            double auraRadius = radius + 6;

            // Aura: faint backdrop ring + bright arc proportional to Q.
            // Q (scroll wheel) ranges 0.1 .. 10 (per-mille 100..10000), log
            // scale - each decade is a half-revolution so wide-Q bands have
            // a stub and narrow-Q resonances complete the ring.
            var auraBg = new Pen(
                new SolidColorBrush(Color.FromArgb(60, bandColor.R, bandColor.G, bandColor.B)),
                2.0);
            context.DrawEllipse(null, auraBg, new Point(fx, yy), auraRadius, auraRadius);

            double qClamped = Math.Clamp(b.qMille, MinQMille, MaxQMille);
            double qNorm = (Math.Log10(qClamped) - Math.Log10(MinQMille)) /
                           (Math.Log10(MaxQMille) - Math.Log10(MinQMille));
            double sweepDeg = qNorm * 360.0;
            if (sweepDeg > 0.5)
            {
                var arcPen = new Pen(new SolidColorBrush(bandColor), emph ? 3.0 : 2.4)
                {
                    LineCap = PenLineCap.Round,
                };
                if (sweepDeg >= 359.0)
                {
                    // Full revolution: an ArcTo whose start and end coincide
                    // is geometrically degenerate (no unique circle through
                    // one point) and Skia draws nothing. Render a complete
                    // ellipse stroke instead.
                    context.DrawEllipse(null, arcPen, new Point(fx, yy),
                                        auraRadius, auraRadius);
                }
                else
                {
                    var arc = BuildArc(new Point(fx, yy), auraRadius, sweepDeg,
                                       clockwise: true);
                    context.DrawGeometry(null, arcPen, arc);
                }
            }

            // The point itself.
            context.DrawEllipse(bandBrush,
                new Pen(emph ? Brushes.White : new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                        emph ? 2.0 : 1.2),
                new Point(fx, yy), radius, radius);
            var txt = new FormattedText(
                (i + 1).ToString(),
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                10, Brushes.Black);
            context.DrawText(txt, new Point(fx - 3, yy - 7));

            // Persistent compact readout under every handle: gain dB on top,
            // Q on the line below in the band's accent color so values stay
            // visible at a glance, without having to wiggle the point to
            // surface the hover tooltip. Drawn outside the aura ring.
            double gainDb = b.gainCentiDb / 100.0;
            double qVal = b.qMille / 1000.0;
            var compact = new FormattedText(
                $"{gainDb:+0.0;-0.0;0.0} dB",
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                10, new SolidColorBrush(bandColor));
            var qLine = new FormattedText(
                $"Q {qVal:F2}",
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                9, new SolidColorBrush(Color.FromArgb(180, 200, 200, 200)));
            // Stack the two lines below the aura. Flip above the handle if
            // there isn't room below (near the bottom edge of the panel).
            double labelTop = yy + auraRadius + 4;
            bool labelAbove = labelTop + compact.Height + qLine.Height + 4 > h - 2;
            if (labelAbove)
                labelTop = yy - auraRadius - 6 - compact.Height - qLine.Height;
            double labelW = Math.Max(compact.Width, qLine.Width);
            double labelX = Math.Clamp(fx - labelW / 2, 2, w - labelW - 2);
            // Subtle backdrop so the text reads over the response curve.
            context.FillRectangle(
                new SolidColorBrush(Color.FromArgb(140, 16, 20, 24)),
                new Rect(labelX - 3, labelTop - 1,
                         labelW + 6, compact.Height + qLine.Height + 2));
            context.DrawText(compact, new Point(labelX, labelTop));
            context.DrawText(qLine, new Point(labelX, labelTop + compact.Height));

            // Hover/drag still gets the expanded box with freq included.
            if (emph)
            {
                var info = new FormattedText(
                    $"Q {qVal:F2}  {b.freqHz} Hz  {gainDb:+0.0;-0.0;0.0} dB",
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    Typeface.Default,
                    11, Brushes.White);
                double tx = Math.Clamp(fx + 18, 4, w - info.Width - 4);
                double ty = Math.Clamp(yy - 24, 4, h - 18);
                context.FillRectangle(
                    new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
                    new Rect(tx - 4, ty - 2, info.Width + 8, info.Height + 4));
                context.DrawText(info, new Point(tx, ty));
            }
        }
    }

    /// <summary>
    /// Build a circular arc starting at 12 o'clock around <paramref name="center"/>
    /// with the given <paramref name="radius"/>. The arc spans
    /// <paramref name="sweepDeg"/> degrees, drawn clockwise when
    /// <paramref name="clockwise"/> is true (boost) and counter-clockwise
    /// when false (cut). Used for the per-band "gain aura" ring.
    /// </summary>
    private static StreamGeometry BuildArc(Point center, double radius,
                                           double sweepDeg, bool clockwise)
    {
        var geom = new StreamGeometry();
        using var ctx = geom.Open();
        double startAngle = -Math.PI / 2;                  // top
        double sweepRad = sweepDeg * Math.PI / 180.0;
        double endAngle = clockwise ? startAngle + sweepRad
                                      : startAngle - sweepRad;
        var start = new Point(center.X + radius * Math.Cos(startAngle),
                              center.Y + radius * Math.Sin(startAngle));
        var end = new Point(center.X + radius * Math.Cos(endAngle),
                              center.Y + radius * Math.Sin(endAngle));
        ctx.BeginFigure(start, false);
        ctx.ArcTo(end,
                  new Size(radius, radius),
                  rotationAngle: 0,
                  isLargeArc: sweepDeg > 180,
                  sweepDirection: clockwise ? SweepDirection.Clockwise
                                            : SweepDirection.CounterClockwise);
        ctx.EndFigure(false);
        return geom;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var pt = e.GetPosition(this);
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0)
            return;

        // Handles win when in range so users can grab them precisely.
        int handle = HitTest(pt, w, h);
        if (handle >= 0)
        {
            _dragBand = handle;
            _dragLineOnly = false;
            return;
        }

        // Otherwise check the response curve itself. A click within a small
        // vertical tolerance of the line starts a uniform-translation drag
        // driven by the post-EQ gain. Band gains are NOT touched, so the
        // shape is preserved exactly during AND after the drag.
        if (LineHitTest(pt, w, h))
        {
            _dragLineOnly = true;
            _dragInitialPressY = pt.Y;
            _dragInitialPostGainCentiDb = PostGainCentiDb;
            InvalidateVisual();
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pt = e.GetPosition(this);
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0)
            return;

        if (_dragLineOnly)
        {
            double deltaPx = pt.Y - _dragInitialPressY;
            double deltaDb = -(deltaPx / h) * (GMax - GMin);
            int deltaCentiDb = (int)Math.Round(deltaDb * 100);
            int newPostGain = _dragInitialPostGainCentiDb + deltaCentiDb;
            if (newPostGain < PostGainMinCentiDb)
                newPostGain = PostGainMinCentiDb;
            if (newPostGain > PostGainMaxCentiDb)
                newPostGain = PostGainMaxCentiDb;
            if (newPostGain != PostGainCentiDb)
            {
                PostGainCentiDb = newPostGain;
                PostGainChanged?.Invoke();
            }
            InvalidateVisual();
            return;
        }

        if (_dragBand >= 0)
        {
            double f = XToFreq(Math.Clamp(pt.X, 1, w - 1), w);
            // Cursor Y is in "screen" space which already includes the
            // post-EQ offset. Subtract it before storing as the band gain so
            // the handle stays under the cursor as it is dragged.
            double screenGain = YToGain(Math.Clamp(pt.Y, 0, h), h);
            double bandGain = screenGain - PostGainCentiDb / 100.0;
            var b = Bands[_dragBand];
            b.freqHz = (int)Math.Round(f);
            b.gainCentiDb = (int)Math.Round(bandGain * 100);
            // Clamp the band gain itself to the per-band range; the global
            // post-gain can still push the visual line past the chart edge
            // independently.
            if (b.gainCentiDb < (int)(GMin * 100))
                b.gainCentiDb = (int)(GMin * 100);
            if (b.gainCentiDb > (int)(GMax * 100))
                b.gainCentiDb = (int)(GMax * 100);
            Bands[_dragBand] = b;
            InvalidateVisual();
            BandChanged?.Invoke(_dragBand);
            return;
        }

        // Otherwise, track which band the pointer hovers near so the wheel
        // handler knows which Q to adjust. Same hit-test radius as press.
        int newHover = HitTest(pt, w, h);
        if (newHover != _hoverBand)
        {
            _hoverBand = newHover;
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragBand = -1;
        _dragLineOnly = false;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hoverBand != -1)
        {
            _hoverBand = -1;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Scroll wheel over a band point adjusts its Q (steepness / sharpness)
    /// like FL Studio Parametric EQ 2. Hold Shift to scroll in larger steps.
    /// </summary>
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var pt = e.GetPosition(this);
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0)
            return;

        int hit = HitTest(pt, w, h);
        if (hit < 0)
        {
            // Wheel without a band under the cursor: no-op, let the event
            // bubble so a wrapping ScrollViewer can react if present.
            return;
        }

        // Sticky hover: keep the band under the cursor highlighted so its
        // expanded readout box stays visible across the wheel gesture.
        _hoverBand = hit;

        bool shift = (e.KeyModifiers & Avalonia.Input.KeyModifiers.Shift) != 0;
        double delta = e.Delta.Y; // +1 = up, -1 = down
        // Geometric step so Q feels uniform across the 100..10000 range.
        // x1.1 per notch by default, x1.3 with Shift for coarser changes.
        double factor = Math.Pow(shift ? 1.3 : 1.1, delta);

        var b = Bands[hit];
        int newQ = (int)Math.Round(b.qMille * factor);
        if (newQ < MinQMille)
            newQ = MinQMille;
        if (newQ > MaxQMille)
            newQ = MaxQMille;
        if (newQ != b.qMille)
        {
            b.qMille = newQ;
            Bands[hit] = b;
            InvalidateVisual();
            BandQChanged?.Invoke(hit);
        }
        e.Handled = true;
    }

    /// <summary>
    /// Pixel hit-test: return the band index whose handle is closest to
    /// <paramref name="pt"/> within a small radius, or -1 if none qualify.
    /// </summary>
    private int HitTest(Point pt, double w, double h)
    {
        int hit = -1;
        double bestDist = 16;
        double postGainDb = PostGainCentiDb / 100.0;
        for (int i = 0; i < NumBands; i++)
        {
            var b = Bands[i];
            double fx = FreqToX(b.freqHz, w);
            // Match the render: handles include the post-EQ offset in Y.
            double yy = GainToY(b.gainCentiDb / 100.0 + postGainDb, h);
            double d = Math.Sqrt(Math.Pow(fx - pt.X, 2) + Math.Pow(yy - pt.Y, 2));
            if (d < bestDist)
            { bestDist = d; hit = i; }
        }
        return hit;
    }

    /// <summary>
    /// Curve hit-test: true when the pointer is within a small vertical
    /// tolerance of the response curve at the cursor X. Used to detect
    /// "click on the line" gestures that initiate a whole-line drag.
    /// </summary>
    private bool LineHitTest(Point pt, double w, double h)
    {
        if (pt.X < 0 || pt.X > w)
            return false;
        double f = XToFreq(pt.X, w);
        double curveDb = Response(f);
        double curveY = GainToY(curveDb, h);
        return Math.Abs(pt.Y - curveY) <= 14.0;
    }
}
