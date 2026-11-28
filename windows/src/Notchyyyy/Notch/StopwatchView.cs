using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Notchyyyy.Motion;
using Notchyyyy.Theme;
using Notchyyyy.Today;

namespace Notchyyyy.Notch;

/// <summary>
/// The Stopwatch tab (the Mac app's StopwatchView): the time, large; Lap / Reset and Start / Stop; the keys
/// (Space, L, R); and the laps, newest first, the best in green and the worst in red.
/// </summary>
public sealed class StopwatchView : Grid
{
    private readonly StopwatchModel _sw;
    private readonly IClock _clock;
    private readonly TimeText _time;
    private readonly RoundAction _left, _right;
    private readonly StackPanel _laps;
    private readonly TextBlock _noLaps;
    private readonly ScrollViewer _lapScroll;

    public StopwatchView(NotchModel m, IClock clock)
    {
        _sw = m.Stopwatch; _clock = clock;
        ColumnDefinitions = new ColumnDefinitions("*,24,180");
        _time = new TimeText(_sw) { HorizontalAlignment = HorizontalAlignment.Center };
        _left = new RoundAction(clock, "Reset", null, () => { if (_sw.Running) _sw.Lap(); else _sw.Reset(); });
        _right = new RoundAction(clock, "Start", LT.Green, _sw.Toggle);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center, Children = { _left, _right } };
        var hint = new TextBlock
        {
            Text = "Space start / stop · L lap · R reset", FontFamily = LT.Ui, FontSize = LT.TextSize(10.5),
            Foreground = new SolidColorBrush(LT.Faint), HorizontalAlignment = HorizontalAlignment.Center,
        };
        Children.Add(new StackPanel { Spacing = 22, VerticalAlignment = VerticalAlignment.Center, Children = { _time, buttons, hint } });

        _laps = new StackPanel();
        _lapScroll = new ScrollViewer { Content = _laps, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden };
        _noLaps = new TextBlock
        {
            Text = "Laps you take show here", FontFamily = LT.Ui, FontSize = LT.TextSize(11.5), Foreground = new SolidColorBrush(LT.Faint),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        var lapsTitle = new TextBlock { Text = "Laps", FontFamily = LT.Ui, FontSize = LT.TextSize(11), FontWeight = FontWeight.Medium, Foreground = new SolidColorBrush(LT.Faint), Margin = new Thickness(0, 0, 0, 6) };
        var right = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Children = { lapsTitle } };
        Grid.SetRow(_lapScroll, 1); right.Children.Add(_lapScroll);
        Grid.SetRow(_noLaps, 1); right.Children.Add(_noLaps);
        SetColumn(right, 2);
        Children.Add(right);
        Refresh();
    }

    public RoundAction LeftButton => _left;
    public RoundAction RightButton => _right;
    public TimeText Time => _time;
    public IReadOnlyList<(string Name, string Time, Color Colour)> LapRows =>
        _laps.Children.OfType<Grid>().Select(g => (((TextBlock)g.Children[0]).Text!, ((TextBlock)g.Children[1]).Text!, ((SolidColorBrush)((TextBlock)g.Children[1]).Foreground!).Color)).ToList();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); _sw.Changed += Refresh; Refresh(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { base.OnDetachedFromVisualTree(e); _sw.Changed -= Refresh; }

    private void Refresh()
    {
        _left.Set(_sw.Running ? "Lap" : "Reset", null, _sw.HasTime);
        _right.Set(_sw.Running ? "Stop" : "Start", _sw.Running ? LT.Red : LT.Green, true);
        _time.Tick();
        _noLaps.IsVisible = _sw.Laps.Count == 0;
        _laps.Children.Clear();
        double? best = _sw.Laps.Count > 1 ? _sw.Laps.Min() : null, worst = _sw.Laps.Count > 1 ? _sw.Laps.Max() : null;
        for (int i = 0; i < _sw.Laps.Count; i++)
        {
            double lap = _sw.Laps[i];
            var colour = lap == best ? LT.Green : lap == worst ? LT.Red : LT.Text;
            var name = new TextBlock { Text = $"Lap {_sw.Laps.Count - i}", FontFamily = LT.Ui, FontSize = LT.TextSize(12), Foreground = new SolidColorBrush(LT.Muted), VerticalAlignment = VerticalAlignment.Center };
            var time = new TextBlock { Text = StopwatchModel.Format(lap), FontFamily = LT.Mono, FontSize = LT.TextSize(12), Foreground = new SolidColorBrush(colour), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            var line = new Border { Height = 1, Background = new SolidColorBrush(LT.Border), VerticalAlignment = VerticalAlignment.Bottom };
            _laps.Children.Add(new Grid { Height = 30, Children = { name, time, line } });
        }
    }

    /// Space, L and R while the Stopwatch tab shows (from the shell's key handler).
    public static bool HandleKey(StopwatchModel sw, Key key)
    {
        switch (key)
        {
            case Key.Space: sw.Toggle(); return true;
            case Key.L: sw.Lap(); return true;
            case Key.R: if (!sw.Running) sw.Reset(); return true;
            default: return false;
        }
    }
}

/// <summary>
/// The big time, in light rounded figures that keep their width (so it doesn't shimmy as digits change), redrawn
/// every display frame while running (the hundredths need it).
/// </summary>
public sealed class TimeText : Control
{
    private readonly StopwatchModel _sw;
    private bool _ticking;
    public TimeText(StopwatchModel sw) { _sw = sw; Height = 64 * Prefs.Shared.TextScale; }
    public string Shown { get; private set; } = "";

    private static FormattedText F(string s) => new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
        new Typeface(LT.Ui, FontStyle.Normal, FontWeight.Light), LT.TextSize(50), new SolidColorBrush(LT.Text));

    /// Every figure is drawn in its own slot as wide as the widest figure ("tabular" figures).
    private static double DigitW => _digitW ??= Enumerable.Range(0, 10).Max(d => F(d.ToString()).WidthIncludingTrailingWhitespace);
    private static double? _digitW;

    protected override Size MeasureOverride(Size available)
    {
        var s = StopwatchModel.Format(0);
        return new Size(s.Count(char.IsDigit) * DigitW + F(":").Width + F(".").Width + 2, Height);
    }

    public void Tick()
    {
        InvalidateVisual();
        if (_ticking || !_sw.Running || TopLevel.GetTopLevel(this) is not { } top) return;
        _ticking = true;
        top.RequestAnimationFrame(_ => { _ticking = false; Tick(); });
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); _digitW = null; Tick(); }

    public override void Render(DrawingContext ctx)
    {
        Shown = StopwatchModel.Format(_sw.Elapsed());
        double total = Shown.Sum(c => char.IsDigit(c) ? DigitW : F(c.ToString()).WidthIncludingTrailingWhitespace);
        double x = Bounds.Width / 2 - total / 2;
        foreach (var ch in Shown)
        {
            var t = F(ch.ToString());
            double w = char.IsDigit(ch) ? DigitW : t.WidthIncludingTrailingWhitespace;
            ctx.DrawText(t, new Point(x + (w - t.WidthIncludingTrailingWhitespace) / 2, Bounds.Height / 2 - t.Height / 2));
            x += w;
        }
    }
}

/// A capsule button (the Mac's RoundAction): tinted (green Start, red Stop) or plain, a little larger on hover.
public sealed class RoundAction : SpringControl
{
    private readonly SpringValue _hover;
    private readonly Action _action;
    private string _title;
    private Color? _tint;
    private bool _enabled = true;

    public RoundAction(IClock clock, string title, Color? tint, Action action) : base(clock)
    {
        _title = title; _tint = tint; _action = action; _hover = Spring(0);
        Width = 76; Height = 36; Cursor = new Cursor(StandardCursorType.Hand);
    }

    public string Title => _title;
    public bool Enabled => _enabled;
    public void Set(string title, Color? tint, bool enabled) { _title = title; _tint = tint; _enabled = enabled; InvalidateVisual(); }

    protected override void OnPointerEntered(PointerEventArgs e) { base.OnPointerEntered(e); Go(_hover, 1, LT.Hover); }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); Go(_hover, 0, LT.Hover); }
    protected override void OnPointerPressed(PointerPressedEventArgs e) { base.OnPointerPressed(e); e.Handled = true; }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_enabled && new Rect(Bounds.Size).Contains(e.GetPosition(this))) _action();
    }

    public override void Render(DrawingContext ctx)
    {
        double h = _enabled ? V(_hover.Value(Now)) : 0, s = 1 + 0.04 * h;
        var r = new Rect(38 - 38 * s, 18 - 18 * s, 76 * s, 36 * s);
        var tint = _tint ?? LT.Wash;
        ctx.DrawRectangle(new SolidColorBrush(tint.WithOpacity(0.12 + 0.08 * h)), null, r, 18 * s, 18 * s);
        var t = Text(_title, 12.5, _tint ?? (_enabled ? LT.Text : LT.Faint), FontWeight.SemiBold);
        ctx.DrawText(t, new Point(38 - t.Width / 2, 18 - t.Height / 2));
    }
}
