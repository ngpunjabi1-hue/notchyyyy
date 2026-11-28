using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Notchyyyy.Motion;
using Notchyyyy.Theme;

namespace Notchyyyy.Notch;

/// <summary>
/// A control whose look rides springs: it redraws every display frame until they all settle. Its whole box takes the
/// pointer (a custom-drawn control otherwise only gets clicks where it has painted something).
/// </summary>
public abstract class SpringControl : Control, Avalonia.Rendering.ICustomHitTest
{
    public virtual bool HitTest(Point p) => new Rect(Bounds.Size).Contains(p);

    protected readonly IClock Clock;
    private readonly List<SpringValue> _springs = new();
    private bool _ticking;
    protected SpringControl(IClock clock) { Clock = clock; }
    protected double Now => Clock.Now;

    protected SpringValue Spring(double start, double eps = 0.002) { var s = new SpringValue(start, eps); _springs.Add(s); return s; }
    protected void Go(SpringValue s, double to, SpringSpec spec) { if (Math.Abs(s.Target - to) > 1e-9) s.AnimateTo(to, spec, Now); Animate(); }

    protected void Animate()
    {
        InvalidateVisual();
        if (_ticking || TopLevel.GetTopLevel(this) is not { } top) return;
        _ticking = true;
        top.RequestAnimationFrame(_ =>
        {
            _ticking = false;
            InvalidateVisual();
            if (_springs.Any(s => !s.IsSettled(Now))) Animate();
        });
    }

    protected static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        (byte)Math.Round(a.A + (b.A - a.A) * t), (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));
    protected static FormattedText Text(string s, double size, Color c, FontWeight w = FontWeight.Normal) =>
        new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(LT.Ui, FontStyle.Normal, w), LT.TextSize(size), new SolidColorBrush(c));
    protected static double V(double a) => Math.Clamp(a, 0, 1);
}

/// The Mac's Switch: a 32 × 19 capsule, accent when on, with a white knob that slides on the snappy spring.
public sealed class Toggle : SpringControl
{
    private readonly SpringValue _on;
    private readonly Func<bool> _get;
    private readonly Action<bool> _set;
    public Toggle(IClock clock, Func<bool> get, Action<bool> set) : base(clock)
    {
        _get = get; _set = set;
        _on = Spring(get() ? 1 : 0);
        Width = 32; Height = 19;
        Cursor = new Cursor(StandardCursorType.Hand);
        VerticalAlignment = VerticalAlignment.Center;
    }
    public bool IsOn => _get();
    public void Flip() { bool next = !_get(); _set(next); Go(_on, next ? 1 : 0, LT.Snappy); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (new Rect(Bounds.Size).Contains(e.GetPosition(this))) Flip();
    }
    public override void Render(DrawingContext ctx)
    {
        double v = _on.Value(Now), c = V(v);
        ctx.DrawRectangle(new SolidColorBrush(Mix(LT.Wash.WithOpacity(0.14), LT.AccentColor, c)), null, new Rect(0, 0, 32, 19), 9.5, 9.5);
        double x = 2 + 7.5 + v * 13;          // knob centre slides from 9.5 to 22.5
        ctx.DrawEllipse(new SolidColorBrush(Colors.Black.WithOpacity(0.2)), null, new Point(x, 10.5), 7.5, 7.5);   // soft shadow
        ctx.DrawEllipse(Brushes.White, null, new Point(x, 9.5), 7.5, 7.5);
    }
}

/// The Mac's Segmented picker: options in a tinted well, the chosen one on a raised chip that slides.
public sealed class Segmented<T> : SpringControl where T : notnull
{
    private readonly T[] _options;
    private readonly Func<T, string> _label;
    private readonly Func<T> _get;
    private readonly Action<T> _set;
    private readonly SpringValue _x, _w;
    private double[] _xs = Array.Empty<double>(), _ws = Array.Empty<double>();

    public Segmented(IClock clock, T[] options, Func<T, string> label, Func<T> get, Action<T> set) : base(clock)
    {
        _options = options; _label = label; _get = get; _set = set;
        Layout();
        int i = Index();
        _x = Spring(_xs[i], 0.01); _w = Spring(_ws[i], 0.01);
        Height = 32;
        Cursor = new Cursor(StandardCursorType.Hand);
        VerticalAlignment = VerticalAlignment.Center;
    }
    private int Index() => Math.Max(0, Array.IndexOf(_options, _get()));
    private void Layout()
    {
        double x = 3;
        _xs = new double[_options.Length]; _ws = new double[_options.Length];
        for (int i = 0; i < _options.Length; i++)
        {
            double w = Text(_label(_options[i]), 11.5, Colors.White, FontWeight.Medium).Width + 24;
            _xs[i] = x; _ws[i] = w; x += w + 2;
        }
        Width = x - 2 + 3;
    }
    public void Choose(int i)
    {
        _set(_options[i]);
        Go(_x, _xs[i], LT.Snappy); Go(_w, _ws[i], LT.Snappy);
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var p = e.GetPosition(this);
        for (int i = 0; i < _options.Length; i++) if (p.X >= _xs[i] && p.X <= _xs[i] + _ws[i]) { Choose(i); return; }
    }
    /// Where option i sits (for tests and clicks).
    public Point OptionCentre(int i) => new(_xs[i] + _ws[i] / 2, 16);
    public override void Render(DrawingContext ctx)
    {
        ctx.DrawRectangle(new SolidColorBrush(LT.Wash.WithOpacity(0.05)), null, new Rect(0, 0, Bounds.Width, 32), 8, 8);
        var chip = new Rect(_x.Value(Now), 3, _w.Value(Now), 26);
        ctx.DrawRectangle(new SolidColorBrush(LT.SurfaceHi), new Pen(new SolidColorBrush(LT.Border), 1), chip.Deflate(0.5), 6, 6);
        int sel = Index();
        for (int i = 0; i < _options.Length; i++)
        {
            var t = Text(_label(_options[i]), 11.5, i == sel ? LT.Text : LT.Muted, FontWeight.Medium);
            ctx.DrawText(t, new Point(_xs[i] + (_ws[i] - t.Width) / 2, 16 - t.Height / 2));
        }
    }
}

/// The Mac's accent swatches: eight 16 pt dots; the chosen one gets a ring; hovered ones swell a little.
public sealed class Swatches : SpringControl
{
    private readonly SpringValue[] _hover;
    private int _hot = -1;
    private readonly Action<Accent> _set;
    private static readonly Accent[] All = Enum.GetValues<Accent>();
    public Swatches(IClock clock, Action<Accent> set) : base(clock)
    {
        _set = set;
        _hover = All.Select(_ => Spring(0)).ToArray();
        Width = All.Length * 22 + (All.Length - 1) * 8; Height = 22;
        Cursor = new Cursor(StandardCursorType.Hand);
        VerticalAlignment = VerticalAlignment.Center;
    }
    public Point Centre(int i) => new(11 + i * 30, 11);
    private int At(Point p) { for (int i = 0; i < All.Length; i++) if (Math.Abs(p.X - Centre(i).X) <= 11) return i; return -1; }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        int i = At(e.GetPosition(this));
        if (i == _hot) return;
        if (_hot >= 0) Go(_hover[_hot], 0, LT.Hover);
        _hot = i;
        if (i >= 0) { Go(_hover[i], 1, LT.Hover); ToolTip.SetTip(this, All[i].ToString()); }
    }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); if (_hot >= 0) Go(_hover[_hot], 0, LT.Hover); _hot = -1; }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        int i = At(e.GetPosition(this));
        if (i >= 0) { _set(All[i]); Animate(); }
    }
    public override void Render(DrawingContext ctx)
    {
        for (int i = 0; i < All.Length; i++)
        {
            double s = 1 + 0.12 * _hover[i].Value(Now);
            var c = Centre(i);
            var col = LT.AccentOf(All[i]);
            ctx.DrawEllipse(new SolidColorBrush(col), null, c, 8 * s, 8 * s);
            if (Prefs.Shared.Accent == All[i]) ctx.DrawEllipse(null, new Pen(new SolidColorBrush(col), 1.5), c, 10.25 * s, 10.25 * s);
        }
    }
}

/// <summary>
/// The Mac's ScaleSlider: a small glyph, a slider, a large glyph and the percentage. While dragging it only shows the
/// value; it applies on release (rounded to 5 %), so the notch doesn't rebuild on every pixel of the drag.
/// </summary>
public sealed class ScaleSlider : SpringControl
{
    private readonly double _min, _max, _small, _large;
    private readonly bool _textGlyph;
    private readonly Func<double> _get;
    private readonly Action<double> _set;
    private double? _draft;
    private const double TrackX = 28, TrackW = 150;

    public ScaleSlider(IClock clock, double min, double max, double small, double large, bool textGlyph, Func<double> get, Action<double> set) : base(clock)
    {
        _min = min; _max = max; _small = small; _large = large; _textGlyph = textGlyph; _get = get; _set = set;
        Width = TrackX + TrackW + 10 + 18 + 10 + 38; Height = 24;
        VerticalAlignment = VerticalAlignment.Center;
        Cursor = new Cursor(StandardCursorType.Hand);
    }
    private double Value => _draft ?? _get();
    private double XFor(double v) => TrackX + (v - _min) / (_max - _min) * TrackW;
    private double VFor(double x) => Math.Clamp(_min + (x - TrackX) / TrackW * (_max - _min), _min, _max);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        e.Pointer.Capture(this);
        _draft = VFor(e.GetPosition(this).X); InvalidateVisual();
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_draft is null) return;
        _draft = VFor(e.GetPosition(this).X); InvalidateVisual();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_draft is not { } d) return;
        _draft = null;
        e.Pointer.Capture(null);
        _set(Math.Round(d * 20) / 20);
        InvalidateVisual();
    }

    private void Glyph(DrawingContext ctx, double cx, double size)
    {
        if (_textGlyph)
        {
            var t = new FormattedText("A", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(LT.Ui, FontStyle.Normal, FontWeight.Medium), size, new SolidColorBrush(LT.Muted));
            ctx.DrawText(t, new Point(cx - t.Width / 2, 12 - t.Height / 2));
        }
        else Icons.Draw(ctx, IconKind.AppSquare, new Point(cx, 12), size * 1.35, LT.Muted);
    }

    public override void Render(DrawingContext ctx)
    {
        Glyph(ctx, 9, _small);
        double x = XFor(Value);
        ctx.DrawRectangle(new SolidColorBrush(LT.Wash.WithOpacity(0.12)), null, new Rect(TrackX, 10.5, TrackW, 3), 1.5, 1.5);
        ctx.DrawRectangle(new SolidColorBrush(LT.AccentColor), null, new Rect(TrackX, 10.5, x - TrackX, 3), 1.5, 1.5);
        ctx.DrawEllipse(new SolidColorBrush(Colors.Black.WithOpacity(0.18)), new Pen(new SolidColorBrush(LT.Border), 1), new Point(x, 12.6), 7.5, 7.5);
        ctx.DrawEllipse(Brushes.White, new Pen(new SolidColorBrush(LT.BorderHi), 0.5), new Point(x, 12), 7, 7);
        Glyph(ctx, TrackX + TrackW + 10 + 9, _large);
        var pct = new FormattedText($"{Math.Round(Value * 100)}%", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                    new Typeface(LT.Mono), LT.TextSize(10.5), new SolidColorBrush(LT.Faint));
        ctx.DrawText(pct, new Point(Bounds.Width - pct.Width, 12 - pct.Height / 2));
    }

    /// Test helper: drag to a value and let go.
    public Point PointFor(double v) => new(XFor(v), 12);
}

/// A round ± button (the Mac's StepButton).
public sealed class StepButton : SpringControl
{
    private readonly IconKind _icon;
    private readonly SpringValue _hover;
    public Action? Action { get; set; }
    public StepButton(IClock clock, IconKind icon, Action action) : base(clock)
    {
        _icon = icon; Action = action; _hover = Spring(0);
        Width = 26; Height = 26; Cursor = new Cursor(StandardCursorType.Hand);
        VerticalAlignment = VerticalAlignment.Center;
    }
    protected override void OnPointerEntered(PointerEventArgs e) { base.OnPointerEntered(e); Go(_hover, 1, LT.Hover); }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); Go(_hover, 0, LT.Hover); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (new Rect(Bounds.Size).Contains(e.GetPosition(this))) Action?.Invoke();
    }
    public override void Render(DrawingContext ctx)
    {
        double h = V(_hover.Value(Now));
        ctx.DrawEllipse(new SolidColorBrush(LT.Wash.WithOpacity(0.05 + 0.05 * h)), null, new Point(13, 13), 13, 13);
        Icons.Draw(ctx, _icon, new Point(13, 13), 13 * Prefs.Shared.IconScale, Mix(LT.Muted, LT.Text, h));
    }
}

/// The Mac's PillButton: a small rounded button with an icon and a title, brighter on hover; greyed out when disabled.
public sealed class PillButton : SpringControl
{
    private readonly IconKind? _icon;
    private readonly string _title;
    private readonly SpringValue _hover;
    public Action? Action { get; set; }
    public bool Enabled { get; set; } = true;
    public PillButton(IClock clock, string title, IconKind? icon, Action? action) : base(clock)
    {
        _title = title; _icon = icon; Action = action; _hover = Spring(0);
        Height = 26;
        Cursor = new Cursor(StandardCursorType.Hand);
        VerticalAlignment = VerticalAlignment.Center;
    }
    protected override Size MeasureOverride(Size availableSize) =>
        new(9 + (_icon is null ? 0 : 15) + Text(_title, 11, Colors.White, FontWeight.Medium).Width + 9, 26);
    protected override void OnPointerEntered(PointerEventArgs e) { base.OnPointerEntered(e); if (Enabled) Go(_hover, 1, LT.Hover); }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); Go(_hover, 0, LT.Hover); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (Enabled && new Rect(Bounds.Size).Contains(e.GetPosition(this))) Action?.Invoke();
    }
    public override void Render(DrawingContext ctx)
    {
        double h = V(_hover.Value(Now));
        using var _ = ctx.PushOpacity(Enabled ? 1 : 0.4);
        var r = new Rect(0.5, 0.5, Bounds.Width - 1, 25);
        ctx.DrawRectangle(new SolidColorBrush(LT.Wash.WithOpacity(0.04 + 0.04 * h)), new Pen(new SolidColorBrush(LT.Border), 1), r, 6, 6);
        var c = Mix(LT.Muted, LT.Text, h);
        double x = 9;
        if (_icon is { } icon) { Icons.Draw(ctx, icon, new Point(x + 5.5, 13), 13 * Prefs.Shared.IconScale, c); x += 15; }
        var t = Text(_title, 11, c, FontWeight.Medium);
        ctx.DrawText(t, new Point(x, 13 - t.Height / 2));
    }
}
