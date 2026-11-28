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
/// The peek bubble (the Mac's PeekBubble): the closed notch swells a little to say something. Sprout (thirsty, or
/// cheering) and the water ring on top; under them "Time for a sip, Alex · 3 of 8 glasses so far" with Later and
/// Drank, or after a glass "Nice, that's 4 today", or after an update "Updated to 0.2.0". Always dark: the closed
/// notch is black in either theme.
/// </summary>
public sealed class PeekBubble : Panel
{
    public static readonly Color WaterBlue = Color.FromRgb(107, 189, 255);   // the Mac's SproutPainter.water
    private readonly NotchModel _m;
    private readonly IClock _clock;
    private readonly Sprout.SproutView _sprout;
    private readonly GlassRing _ring;
    private readonly IconView _done;
    private readonly TextBlock _title, _detail;
    private readonly PeekButton _later, _drank;

    public PeekBubble(NotchModel m, IClock clock)
    {
        _m = m; _clock = clock;
        Width = m.PeekSize.Width; Height = m.PeekSize.Height;
        _sprout = new Sprout.SproutView(clock, 30)
        {
            Fps = 60, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            Source = t => (_m.Peeking is Peek.Cheer or Peek.Updated ? global::Notchyyyy.Sprout.SproutMood.Cheer : global::Notchyyyy.Sprout.SproutMood.Thirsty, default, false, 1),
        };
        _ring = new GlassRing(clock) { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        _done = new IconView(IconKind.CheckCircle, 15, LT.Green) { Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var top = new Grid { Height = m.NotchSize.Height, Margin = new Thickness(16, 0), Children = { _sprout, _ring, _done } };
        _title = new TextBlock { FontFamily = LT.Ui, FontSize = LT.TextSize(13), FontWeight = FontWeight.SemiBold, Foreground = Brushes.White };
        _detail = new TextBlock { FontFamily = LT.Ui, FontSize = LT.TextSize(11), Foreground = new SolidColorBrush(Color.FromRgb(148, 148, 148)) };
        _later = new PeekButton(clock, "Later", null, false, () => { _m.Water.Later(); _m.EndPeek(); });
        _drank = new PeekButton(clock, "Drank", IconKind.Drop, true, Drank);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Children = { _later, _drank } };
        var words = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { _title, _detail } };
        var row = new Grid { Margin = new Thickness(20, 0), Children = { words, buttons } };
        var grid = new Grid { RowDefinitions = new RowDefinitions($"{m.NotchSize.Height},*"), Children = { top } };
        Grid.SetRow(row, 1);
        grid.Children.Add(row);
        Children.Add(grid);
        Refresh();
    }

    public PeekButton LaterButton => _later;
    public PeekButton DrankButton => _drank;
    public string Title => _title.Text ?? "";
    public string Detail => _detail.Text ?? "";
    public Sprout.SproutView Sprout => _sprout;

    private void Drank()
    {
        _m.Water.Drank();
        double now = _clock.Now;
        _m.Buddy.CheerUntil = now + 4;
        _m.ShowPeek(Peek.Cheer, now, 1.8);
    }

    /// Fills in the words for what the bubble says now.
    public void Refresh()
    {
        var w = _m.Water;
        bool cheer = _m.Peeking == Peek.Cheer, updated = _m.Peeking == Peek.Updated;
        string first = _m.FirstName;
        if (updated)
        {
            _title.Text = $"Updated to {AppVersion}";
            _detail.Text = "Notchyyyy keeps itself up to date";
        }
        else if (cheer)
        {
            _title.Text = $"Nice, that's {w.Count} today";
            _detail.Text = w.Count >= w.Goal ? "Goal reached. Sprout is thriving" : $"{Math.Max(0, w.Goal - w.Count)} more to reach your goal";
        }
        else
        {
            _title.Text = "Time for a sip" + (first.Length > 0 ? $", {first}" : "");
            _detail.Text = $"{w.Count} of {w.Goal} glasses so far";
        }
        _later.IsVisible = _drank.IsVisible = !cheer && !updated;
        _ring.IsVisible = !updated;
        _done.IsVisible = updated;
        _ring.Set(w.Count, w.Goal);
    }

    public static string AppVersion => typeof(PeekBubble).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "the latest";

    public bool Active { set => _sprout.Active = value; }
}

/// The water ring (the Mac's GlassRing): glasses today against the goal, with a drop in the middle.
public sealed class GlassRing : SpringControl
{
    private readonly SpringValue _f;
    private readonly double _size;
    public GlassRing(IClock clock, double size = 16) : base(clock) { _size = size; _f = Spring(0, 0.001); Width = size; Height = size; }
    public double Fraction => _f.Target;
    public void Set(int count, int goal) => Go(_f, Math.Min(1, count / (double)Math.Max(goal, 1)), new SpringSpec(0.5, 0.3));
    public override void Render(DrawingContext ctx)
    {
        double f = Math.Clamp(_f.Value(Now), 0, 1), r = _size / 2 - 1;
        var c = new Point(_size / 2, _size / 2);
        ctx.DrawEllipse(null, new Pen(new SolidColorBrush(Colors.White.WithOpacity(0.14)), 2), c, r, r);
        if (f > 0.001)
        {
            var arc = new StreamGeometry();
            using (var g = arc.Open())
            {
                double a = Math.Min(f, 0.9999) * 2 * Math.PI - Math.PI / 2;
                g.BeginFigure(new Point(c.X, c.Y - r), false);
                g.ArcTo(new Point(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a)), new Size(r, r), 0, f > 0.5, SweepDirection.Clockwise);
                g.EndFigure(false);
            }
            ctx.DrawGeometry(null, new Pen(new SolidColorBrush(PeekBubble.WaterBlue), 2, lineCap: PenLineCap.Round), arc);
        }
        Icons.Draw(ctx, IconKind.Drop, c, _size * 0.5, PeekBubble.WaterBlue);
    }
}

/// The bubble's buttons (the Mac's PeekButton): white on a soft wash, or water-blue with black text; a little
/// larger on hover.
public sealed class PeekButton : SpringControl
{
    private readonly string _title;
    private readonly IconKind? _icon;
    private readonly bool _prominent;
    private readonly Action _action;
    private readonly SpringValue _hover;
    public PeekButton(IClock clock, string title, IconKind? icon, bool prominent, Action action) : base(clock)
    {
        _title = title; _icon = icon; _prominent = prominent; _action = action; _hover = Spring(0);
        Height = 28; Cursor = new Cursor(StandardCursorType.Hand);
    }
    public int Presses { get; private set; }
    private FormattedText T(Color c) => new(_title, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(LT.Ui, FontStyle.Normal, FontWeight.SemiBold), LT.TextSize(11.5), new SolidColorBrush(c));
    protected override Size MeasureOverride(Size available) => new(12 + (_icon is null ? 0 : 15) + T(Colors.White).Width + 12, 28);
    protected override void OnPointerEntered(PointerEventArgs e) { base.OnPointerEntered(e); Go(_hover, 1, LT.Hover); }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); Go(_hover, 0, LT.Hover); }
    protected override void OnPointerPressed(PointerPressedEventArgs e) { base.OnPointerPressed(e); e.Handled = true; }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (new Rect(Bounds.Size).Contains(e.GetPosition(this))) Press();
    }
    public void Press() { Presses++; _action(); }
    public override void Render(DrawingContext ctx)
    {
        double h = V(_hover.Value(Now)), s = 1 + 0.04 * h, w = Bounds.Width;
        var r = new Rect(w / 2 - w * s / 2, 14 - 14 * s, w * s, 28 * s);
        var fill = _prominent ? PeekBubble.WaterBlue.WithOpacity(0.9 + 0.1 * h) : Colors.White.WithOpacity(0.09 + 0.07 * h);
        ctx.DrawRectangle(new SolidColorBrush(fill), null, r, 14 * s, 14 * s);
        var ink = _prominent ? Colors.Black : Colors.White.WithOpacity(0.8 + 0.2 * h);
        double x = 12;
        if (_icon is { } icon) { Icons.Draw(ctx, icon, new Point(x + 5, 14), 11, ink); x += 15; }
        var t = T(ink);
        ctx.DrawText(t, new Point(x, 14 - t.Height / 2));
    }
}
