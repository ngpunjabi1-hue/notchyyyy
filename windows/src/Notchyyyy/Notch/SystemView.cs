using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Notchyyyy.Motion;
using Notchyyyy.SystemInfo;
using Notchyyyy.Theme;
using SkiaSharp;

namespace Notchyyyy.Notch;

/// <summary>
/// The System tab (the Mac app's SystemView): CPU (with a bar per core), GPU and Memory, each with its share and a
/// sparkline of the last minute, and Storage as a ring.
/// </summary>
public sealed class SystemView : Grid
{
    private readonly SystemMonitor _sys;
    private readonly MetricCard _cpu, _gpu, _mem;
    private readonly StorageCard _disk;
    private readonly CoreBars _cores;

    public SystemView(NotchModel m, IClock clock)
    {
        _sys = m.System;
        ColumnDefinitions = new ColumnDefinitions("*,14,*,14,*,14,*");
        _cores = new CoreBars(clock) { Height = 12 };
        _cpu = new MetricCard(clock, IconKind.Cpu, "CPU", _cores);
        _gpu = new MetricCard(clock, IconKind.Gpu, "GPU", null);
        _mem = new MetricCard(clock, IconKind.Memory, "Memory", null);
        _disk = new StorageCard(clock);
        var cards = new Control[] { _cpu, _gpu, _mem, _disk };
        for (int i = 0; i < 4; i++) { SetColumn(cards[i], i * 2); Children.Add(cards[i]); }
        Refresh(animate: false);
    }

    public MetricCard CpuCard => _cpu;
    public MetricCard GpuCard => _gpu;
    public MetricCard MemCard => _mem;
    public StorageCard DiskCard => _disk;
    public CoreBars Cores => _cores;

    /// Off while the open notch is hidden: the sparklines stop drawing.
    public bool Active { set { _cpu.Active = value; _gpu.Active = value; _mem.Active = value; } }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); _sys.Changed += OnChanged; Refresh(false); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { base.OnDetachedFromVisualTree(e); _sys.Changed -= OnChanged; }
    private void OnChanged() => Refresh(true);

    private void Refresh(bool animate)
    {
        var s = _sys;
        _cpu.Set(s.Cpu, s.CpuHistory, $"{s.Chip} · {s.Cores.Length} cores", animate);
        _gpu.Set(s.Gpu ?? 0, s.GpuHistory, s.Gpu is null ? "No GPU readings here" : (s.GpuName.Length > 0 ? s.GpuName : "Graphics"), animate);
        _mem.Set(s.Mem, s.MemHistory, $"{SystemMonitor.Bytes(s.MemUsed)} of {SystemMonitor.Bytes(s.MemTotal)}", animate);
        _disk.Set(s.DiskUsed, s.DiskTotal, animate);
        _cores.Set(s.Cores, animate);
    }

    /// The accent when calm, amber when busy, red when pegged (the Mac's load()).
    public static Color Load(double v, double warn = 0.6, double critical = 0.85) =>
        v > critical ? LT.Red : v > warn ? Color.FromRgb(237, 166, 51) : LT.AccentColor;
}

/// <summary>A card's surface: Surface → SurfaceHi and a brighter hairline on hover (the Mac's .card).</summary>
public sealed class CardBack : SpringControl
{
    private readonly SpringValue _hover;
    public CardBack(IClock clock) : base(clock) { _hover = Spring(0); IsHitTestVisible = false; }
    public void SetHover(bool on) => Go(_hover, on ? 1 : 0, LT.Hover);
    public double Hover => _hover.Target;
    public override void Render(DrawingContext ctx)
    {
        double h = V(_hover.Value(Now));
        var r = new Rect(Bounds.Size);
        ctx.DrawRectangle(new SolidColorBrush(Mix(LT.Surface, LT.SurfaceHi, h)), new Pen(new SolidColorBrush(Mix(LT.Border, LT.BorderHi, h)), 1), r.Deflate(0.5), 9.5, 9.5);
    }
}

/// <summary>CPU / GPU / Memory: the share, large, a sparkline of the last 40 readings, and a line of detail.</summary>
public sealed class MetricCard : Panel
{
    private readonly IClock _clock;
    private readonly CardBack _back;
    private readonly Percent _value;
    private readonly Sparkline _spark;
    private readonly TextBlock _detail;

    public MetricCard(IClock clock, IconKind icon, string title, Control? extra)
    {
        _clock = clock;
        _back = new CardBack(clock);
        _value = new Percent(clock, 22) { HorizontalAlignment = HorizontalAlignment.Left };
        _spark = new Sparkline(clock);
        _detail = new TextBlock { FontFamily = LT.Ui, FontSize = LT.TextSize(10), Foreground = new SolidColorBrush(LT.Faint), TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 };
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6,
            Children =
            {
                new IconView(icon, 11, LT.Muted) { Width = 12, Height = 14, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = title, FontFamily = LT.Ui, FontSize = LT.TextSize(11), FontWeight = FontWeight.Medium, Foreground = new SolidColorBrush(LT.Muted), VerticalAlignment = VerticalAlignment.Center },
            },
        };
        var grid = new Grid { Margin = new Thickness(14), RowDefinitions = new RowDefinitions("Auto,8,Auto,8,*,8,Auto,8,Auto") };
        grid.Children.Add(header);
        Grid.SetRow(_value, 2); grid.Children.Add(_value);
        Grid.SetRow(_spark, 4); grid.Children.Add(_spark);
        if (extra is not null) { Grid.SetRow(extra, 6); grid.Children.Add(extra); }
        else grid.RowDefinitions[5].Height = grid.RowDefinitions[6].Height = new GridLength(0);
        Grid.SetRow(_detail, 8); grid.Children.Add(_detail);
        Children.Add(_back);
        Children.Add(grid);
        Background = Brushes.Transparent;
    }

    public double Shown => _value.Target;
    public string Detail => _detail.Text ?? "";
    public Sparkline Spark => _spark;
    public bool Hovered => _back.Hover > 0.5;
    public bool Active { set => _spark.Active = value; }

    public void Set(double v, double[] history, string detail, bool animate)
    {
        _value.Set(v, animate);
        _spark.Set(history, SystemView.Load(v), animate);
        _detail.Text = detail;
    }

    protected override void OnPointerEntered(PointerEventArgs e) { base.OnPointerEntered(e); _back.SetHover(true); }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); _back.SetHover(false); }
}

/// The share as "37%", counting to a new value on the snappy spring (the Mac's numericText transition).
public sealed class Percent : SpringControl
{
    private readonly SpringValue _v;
    private readonly double _size;
    public Percent(IClock clock, double size) : base(clock) { _v = Spring(0, 0.001); _size = size; }
    public double Target => _v.Target;
    public string Label => $"{(int)Math.Round(Math.Clamp(_v.Value(Now), 0, 1) * 100)}%";
    public void Set(double v, bool animate) { if (animate) Go(_v, v, LT.Snappy); else { _v.Snap(v); InvalidateVisual(); } }
    private FormattedText F(string s) => new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(LT.Mono, FontStyle.Normal, FontWeight.SemiBold), LT.TextSize(_size), new SolidColorBrush(LT.Text));
    protected override Size MeasureOverride(Size available) => new(F("100%").Width, F("0%").Height);
    public override void Render(DrawingContext ctx) => ctx.DrawText(F(Label), new Point(0, 0));
}

/// <summary>
/// A filled line of the last readings, newest on the right (the Mac's Sparkline). Between readings each point glides
/// to its new height over 1.4 s, as the Mac's linear animation does; drawn on the render thread.
/// </summary>
public sealed class Sparkline : Panel
{
    private readonly IClock _clock;
    private readonly LiveCanvas _canvas;
    private readonly object _sync = new();
    private double[] _from = Array.Empty<double>(), _to = Array.Empty<double>();
    private double _since = double.NegativeInfinity;
    private Color _color = LT.AccentColor;
    public const double Glide = 1.4;

    public Sparkline(IClock clock)
    {
        _clock = clock;
        _canvas = new LiveCanvas(clock, Paint) { Fps = 30, Animating = now => { lock (_sync) return now - _since < Glide; } };
        Children.Add(_canvas);
    }

    public bool Active { set => _canvas.Active = value; }
    public LiveCanvas Canvas => _canvas;

    public void Set(double[] values, Color color, bool animate)
    {
        double now = _clock.Now;
        lock (_sync)
        {
            _from = animate && _to.Length == values.Length ? Current(now) : values;
            _to = values;
            _since = animate ? now : double.NegativeInfinity;
            _color = color;
        }
        _canvas.Kick();
    }

    private double[] Current(double now)
    {
        double p = Math.Clamp((now - _since) / Glide, 0, 1);
        var r = new double[_to.Length];
        for (int i = 0; i < r.Length; i++) r[i] = _from.Length == r.Length ? _from[i] + (_to[i] - _from[i]) * p : _to[i];
        return r;
    }

    private void Paint(SKCanvas c, Size size, double now)
    {
        double[] v; Color col;
        lock (_sync) { v = Current(now); col = _color; }
        if (v.Length < 2) return;
        float w = (float)size.Width, h = (float)size.Height;
        using var line = new SKPath();
        using var fill = new SKPath();
        for (int i = 0; i < v.Length; i++)
        {
            var p = new SKPoint(w * i / (v.Length - 1), (float)(h * (1 - Math.Clamp(v[i], 0, 1))));
            if (i == 0) { line.MoveTo(p); fill.MoveTo(p.X, h); fill.LineTo(p); }
            else { line.LineTo(p); fill.LineTo(p); }
        }
        fill.LineTo(w, h); fill.Close();
        using (var f = new SKPaint { IsAntialias = true, Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, h), new[] { Sk.C(col, 0.28), Sk.C(col, 0) }, SKShaderTileMode.Clamp) })
            c.DrawPath(fill, f);
        using (var s = new SKPaint { IsAntialias = true, IsStroke = true, StrokeWidth = 1.5f, StrokeJoin = SKStrokeJoin.Round, Color = Sk.C(col) })
            c.DrawPath(line, s);
    }
}

/// A bar per core (the Mac's CoreBars): each fills from the bottom with its load, on the snappy spring.
public sealed class CoreBars : SpringControl
{
    private readonly List<SpringValue> _bars = new();
    public CoreBars(IClock clock) : base(clock) { }
    public int Count => _bars.Count;
    public void Set(double[] cores, bool animate)
    {
        while (_bars.Count < cores.Length) _bars.Add(Spring(cores[_bars.Count], 0.002));
        while (_bars.Count > cores.Length) _bars.RemoveAt(_bars.Count - 1);
        for (int i = 0; i < cores.Length; i++) { if (animate) Go(_bars[i], cores[i], LT.Snappy); else _bars[i].Snap(cores[i]); }
        InvalidateVisual();
    }
    public override void Render(DrawingContext ctx)
    {
        int n = _bars.Count;
        if (n == 0) return;
        double gap = 2, w = (Bounds.Width - gap * (n - 1)) / n, h = Bounds.Height, r = Math.Min(w, h) / 2;
        for (int i = 0; i < n; i++)
        {
            double v = Math.Clamp(_bars[i].Value(Now), 0, 1), x = i * (w + gap);
            ctx.DrawRectangle(new SolidColorBrush(LT.Wash.WithOpacity(0.08)), null, new Rect(x, 0, w, h), r, r);
            double bh = Math.Max(2, h * v);
            ctx.DrawRectangle(new SolidColorBrush(SystemView.Load(v)), null, new Rect(x, h - bh, w, bh), r, r);
        }
    }
}

/// <summary>Storage: a ring that fills with the share used (amber past 85 %, red past 95 %) and "N GB free of M".</summary>
public sealed class StorageCard : Panel
{
    private readonly CardBack _back;
    private readonly Ring _ring;
    private readonly TextBlock _detail;

    public StorageCard(IClock clock)
    {
        _back = new CardBack(clock);
        _ring = new Ring(clock);
        _detail = new TextBlock { FontFamily = LT.Ui, FontSize = LT.TextSize(10), Foreground = new SolidColorBrush(LT.Faint), TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 };
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6,
            Children =
            {
                new IconView(IconKind.Drive, 11, LT.Muted) { Width = 12, Height = 14, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = "Storage", FontFamily = LT.Ui, FontSize = LT.TextSize(11), FontWeight = FontWeight.Medium, Foreground = new SolidColorBrush(LT.Muted), VerticalAlignment = VerticalAlignment.Center },
            },
        };
        var grid = new Grid { Margin = new Thickness(14), RowDefinitions = new RowDefinitions("Auto,8,*,8,Auto") };
        grid.Children.Add(header);
        Grid.SetRow(_ring, 2); grid.Children.Add(_ring);
        Grid.SetRow(_detail, 4); grid.Children.Add(_detail);
        Children.Add(_back);
        Children.Add(grid);
        Background = Brushes.Transparent;
    }

    public double Fraction => _ring.Target;
    public string Detail => _detail.Text ?? "";

    public void Set(double used, double total, bool animate)
    {
        _ring.Set(total > 0 ? used / total : 0, animate);
        _detail.Text = $"{SystemMonitor.Bytes(Math.Max(0, total - used))} free of {SystemMonitor.Bytes(total)}";
    }

    protected override void OnPointerEntered(PointerEventArgs e) { base.OnPointerEntered(e); _back.SetHover(true); }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); _back.SetHover(false); }

    private sealed class Ring : SpringControl
    {
        private readonly SpringValue _f;
        public Ring(IClock clock) : base(clock) { _f = Spring(0, 0.001); }
        public double Target => _f.Target;
        public void Set(double f, bool animate) { if (animate) Go(_f, f, LT.Liquid); else { _f.Snap(f); InvalidateVisual(); } }
        public override void Render(DrawingContext ctx)
        {
            double f = Math.Clamp(_f.Value(Now), 0, 1);
            var c = new Point(Bounds.Width / 2, Bounds.Height / 2);
            double r = Math.Max(4, Math.Min(Bounds.Width, Bounds.Height) / 2 - 4 - 3.5);
            ctx.DrawEllipse(null, new Pen(new SolidColorBrush(LT.Wash.WithOpacity(0.08)), 7), c, r, r);
            if (f > 0.001)
            {
                var arc = new StreamGeometry();
                using (var g = arc.Open())
                {
                    double a = f * 2 * Math.PI - Math.PI / 2;
                    g.BeginFigure(new Point(c.X, c.Y - r), false);
                    g.ArcTo(new Point(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a)), new Size(r, r), 0, f > 0.5, SweepDirection.Clockwise);
                    g.EndFigure(false);
                }
                ctx.DrawGeometry(null, new Pen(new SolidColorBrush(SystemView.Load(_f.Target, 0.85, 0.95)), 7, lineCap: PenLineCap.Round), arc);
            }
            var pct = new FormattedText($"{(int)Math.Round(f * 100)}%", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(LT.Mono, FontStyle.Normal, FontWeight.SemiBold), LT.TextSize(15), new SolidColorBrush(LT.Text));
            var used = new FormattedText("used", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(LT.Ui), LT.TextSize(9), new SolidColorBrush(LT.Faint));
            double top = c.Y - (pct.Height + used.Height) / 2;
            ctx.DrawText(pct, new Point(c.X - pct.Width / 2, top));
            ctx.DrawText(used, new Point(c.X - used.Width / 2, top + pct.Height));
        }
    }
}
