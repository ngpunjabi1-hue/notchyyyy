using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Notchyyyy.Motion;
using Notchyyyy.Sprout;
using Notchyyyy.Theme;

namespace Notchyyyy.Notch;

/// <summary>
/// Home (the Mac app's Dashboard): a big Sprout rising out of the notch's bottom edge on the left; on the right a
/// one-line greeting with the date under it, plain icon quick actions beside it, and three at-a-glance rows.
/// Laid out at the Mac's sizes: the content box is 624 × 230 pt; Sprout's column is 206 pt, then 18 pt, then 400 pt.
/// </summary>
public sealed class HomeView : Canvas
{
    public const double SproutColumn = 206, Gap = 18, RightWidth = 400, SproutSize = 300;
    /// Content-box coordinates of the open body's top-left (the content area starts 38 pt in, 46 pt down).
    public static readonly Point ContentOrigin = new(14 + 24, 32 + 14);

    private readonly NotchModel _m;
    private readonly IClock _clock;
    private readonly TextBlock _greeting = new(), _subline = new();
    private readonly QuickAction _water;
    public SproutView Sprout { get; }

    /// What the buttons ask the app to do (the shell wires these up).
    public static Action<bool>? Capture { get; set; }

    public HomeView(NotchModel m, IClock clock)
    {
        _m = m; _clock = clock;
        ClipToBounds = false;

        // Sprout, large: its design line y = 76 (just under its smile) lands on the open body's bottom edge (y = 300),
        // so only the upper body shows; the notch's own outline clips the rest.
        Sprout = new SproutView(clock, SproutSize)
        {
            Interactive = true, HitDepth = 0.74,
            FpsOverride = double.TryParse(global::Notchyyyy.Theme.Debug.Value("--sprout-fps"), System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : null,
            Source = t => (m.MusicPlaying ? SproutMood.Music : IsNight(m) ? SproutMood.Sleepy : SproutMood.Idle,
                           HomeGaze, m.MusicPlaying, 1),
        };
        // SproutView draws Sprout into its box inset by 11.5 % of a 1.3× canvas: the floor-line maths below
        double canvas = SproutSize * 1.3, rectTop = (SproutSize - canvas) / 2 + canvas * 0.115, k = (canvas * 0.77) / 100;
        double boxTop = m.BodySize.Height - (rectTop + 76 * k) - ContentOrigin.Y;
        SetLeft(Sprout, SproutColumn / 2 - SproutSize / 2);
        SetTop(Sprout, boxTop);
        Children.Add(Sprout);

        // the greeting row: the greeting gets the room first; the actions are compact icons beside it
        _greeting.FontFamily = LT.Ui; _greeting.FontWeight = FontWeight.SemiBold; _greeting.Foreground = new SolidColorBrush(LT.Text);
        _greeting.TextWrapping = TextWrapping.NoWrap;
        _subline.FontFamily = LT.Ui; _subline.FontSize = LT.TextSize(11.5); _subline.Foreground = new SolidColorBrush(LT.Muted);
        _water = new QuickAction(IconKind.Drop, "0", "Log a glass of water", clock, () =>
        {
            m.Glasses++;
            m.Buddy.CheerUntil = clock.Now + 3;       // the little Sprout cheers
            Refresh();
        });
        var actions = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 2,
            Children =
            {
                new QuickAction(IconKind.Screenshot, null, "Screenshot", clock, () => Capture?.Invoke(false)),
                new QuickAction(IconKind.Record, null, "Record the screen", clock, () => Capture?.Invoke(true)),
                _water,
            },
        };
        var words = new StackPanel { Spacing = 3, Children = { _greeting, _subline } };
        var header = new DockPanel { Width = RightWidth, LastChildFill = true };
        DockPanel.SetDock(actions, Dock.Right);
        actions.Margin = new Thickness(12, 0, 0, 0);
        actions.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        header.Children.Add(actions);
        header.Children.Add(words);

        var rows = new StackPanel
        {
            Spacing = 8, Width = RightWidth,
            Children =
            {
                new GlanceRow(IconKind.Note, "Nothing playing", "Music, Spotify or YouTube", clock, () => m.Tab = Tab.Media),
                new GlanceRow(IconKind.Today, "Nothing planned today", "Add a task in Today", clock, () => m.Tab = Tab.Today),
                new GlanceRow(IconKind.Clipboard, "Nothing copied yet", "What you copy is kept here", clock, () => m.Tab = Tab.Clipboard),
            },
        };
        var column = new StackPanel { Spacing = 14, Width = RightWidth, Children = { header, rows } };
        SetLeft(column, SproutColumn + Gap);
        SetTop(column, 0);
        Children.Add(column);

        _actions = actions;
        Refresh();
    }

    private readonly StackPanel _actions;
    private IDisposable? _timer;

    // the greeting and date refresh only while Home is on screen (a tab switch makes a new Home)
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer ??= DispatcherTimer.Run(() => { Refresh(); return true; }, TimeSpan.FromSeconds(20));
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer?.Dispose(); _timer = null;
    }

    public bool Refreshing => _timer is not null;

    /// Where the pointer is, seen from between the big Sprout's eyes (set by the shell from the real pointer).
    public static Point HomeGaze { get; set; }

    private static bool IsNight(NotchModel m) { int h = m.Buddy.Hour(); return h >= 23 || h < 5; }

    public static string Greeting(int hour) => hour switch
    {
        >= 5 and < 12 => "Good morning",
        >= 12 and < 17 => "Good afternoon",
        >= 17 and < 22 => "Good evening",
        _ => "Hello, night owl",
    };

    /// Updates the greeting (fitting it on one line: it shrinks a little, down to 70 %, rather than cutting the name),
    /// the date, and the water count.
    public void Refresh()
    {
        var now = DateTime.Now;
        string name = _m.FirstName;
        _greeting.Text = name.Length > 0 ? $"{Greeting(_m.Buddy.Hour())}, {name}" : Greeting(_m.Buddy.Hour());
        _subline.Text = now.ToString("ddd, d MMM", CultureInfo.CurrentCulture);     // "Sat, 28 Nov", as on the Mac
        _water.Title = _m.Glasses.ToString(CultureInfo.InvariantCulture);
        _actions.Measure(Size.Infinity);
        double room = RightWidth - _actions.DesiredSize.Width - 12;
        double size = LT.TextSize(19);
        var ft = new FormattedText(_greeting.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                                   new Typeface(LT.Ui, FontStyle.Normal, FontWeight.SemiBold), size, null);
        if (ft.Width > room) size = Math.Max(size * 0.7, size * room / ft.Width);
        _greeting.FontSize = size;
        _greeting.TextTrimming = TextTrimming.CharacterEllipsis;     // only if even 70 % can't fit
    }

    /// Where the big Sprout's eyes are, in open-body coordinates (for pointer gaze).
    public Point EyesInBody()
    {
        double x = ContentOrigin.X + GetLeft(Sprout) + SproutSize / 2, y = ContentOrigin.Y + GetTop(Sprout) + SproutSize * 0.58;
        return new Point(x, y);
    }
}

/// <summary>A small control that eases a 0…1 hover amount on the Mac's hover spring and redraws while it moves.</summary>
public abstract class HoverControl : Control
{
    protected readonly IClock Clock;
    private readonly SpringValue _hover = new(0, 0.002);
    private bool _ticking;
    protected HoverControl(IClock clock) { Clock = clock; Cursor = new Cursor(StandardCursorType.Hand); }
    protected double HoverAmount => Math.Clamp(_hover.Value(Clock.Now), 0, 1.2);
    public Action? Action { get; set; }

    public void SetHover(bool on) { _hover.AnimateTo(on ? 1 : 0, LT.Hover, Clock.Now); Tick(); }
    protected override void OnPointerEntered(PointerEventArgs e) { base.OnPointerEntered(e); SetHover(true); }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); SetHover(false); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (new Rect(Bounds.Size).Contains(e.GetPosition(this))) Action?.Invoke();
    }
    private void Tick()
    {
        if (_ticking || TopLevel.GetTopLevel(this) is not { } top) { InvalidateVisual(); return; }
        _ticking = true;
        top.RequestAnimationFrame(_ => { _ticking = false; InvalidateVisual(); if (!_hover.IsSettled(Clock.Now)) Tick(); });
    }
    protected static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        (byte)Math.Round(a.A + (b.A - a.A) * t), (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));
}

/// A plain icon button (with a number beside it for water). A soft round highlight shows only on hover.
public sealed class QuickAction : HoverControl
{
    private readonly IconKind _icon;
    private string? _title;
    public QuickAction(IconKind icon, string? title, string help, IClock clock, Action action) : base(clock)
    {
        _icon = icon; _title = title; Action = action;
        ToolTip.SetTip(this, help);
        Height = 28;
    }
    public string? Title { get => _title; set { _title = value; InvalidateMeasure(); InvalidateVisual(); } }

    private FormattedText? Text(Color c) => _title is null ? null : new FormattedText(_title, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
        new Typeface(LT.Ui, FontStyle.Normal, FontWeight.SemiBold), LT.TextSize(11.5), new SolidColorBrush(c));

    protected override Size MeasureOverride(Size availableSize)
    {
        var t = Text(Colors.White);
        return t is null ? new Size(28, 28) : new Size(Math.Max(28, 7 + 16 + 4 + t.WidthIncludingTrailingWhitespace + 7), 28);
    }

    public override void Render(DrawingContext ctx)
    {
        double h = HoverAmount, w = Bounds.Width;
        ctx.DrawRectangle(new SolidColorBrush(LT.Wash.WithOpacity(0.08 * Math.Clamp(h, 0, 1))), null, new Rect(0, 0, w, 28), 14, 14);
        var c = Mix(LT.Muted, LT.Text, Math.Clamp(h, 0, 1));
        var t = Text(c);
        if (t is null) { Icons.Draw(ctx, _icon, new Point(w / 2, 14), 17 * Prefs.Shared.IconScale, c); return; }
        Icons.Draw(ctx, _icon, new Point(7 + 8, 14), 15 * Prefs.Shared.IconScale, c);
        ctx.DrawText(t, new Point(7 + 16 + 4, 14 - t.Height / 2));
    }
}

/// One at-a-glance row: an icon tile, a title and a detail line, and a chevron that nudges right on hover.
public sealed class GlanceRow : HoverControl
{
    private readonly IconKind _icon;
    public string Title { get; set; }
    public string Detail { get; set; }
    public GlanceRow(IconKind icon, string title, string detail, IClock clock, Action action) : base(clock)
    {
        _icon = icon; Title = title; Detail = detail; Action = action;
        Height = 46;
    }

    public override void Render(DrawingContext ctx)
    {
        double h = Math.Clamp(HoverAmount, 0, 1), w = Bounds.Width;
        var card = new Rect(0.5, 0.5, w - 1, 45);
        ctx.DrawRectangle(new SolidColorBrush(Mix(LT.Surface, LT.SurfaceHi, h)), new Pen(new SolidColorBrush(Mix(LT.Border, LT.BorderHi, h)), 1), card, 10, 10);
        var tile = new Rect(12, 10, 26, 26);
        ctx.DrawRectangle(new SolidColorBrush(LT.Wash.WithOpacity(0.05)), null, tile, 7, 7);
        Icons.Draw(ctx, _icon, tile.Center, 14 * Prefs.Shared.IconScale, Mix(LT.Muted, LT.AccentColor, h));
        var tf = new Typeface(LT.Ui, FontStyle.Normal, FontWeight.Medium);
        double textW = w - 12 - 26 - 12 - 4 - 9 - 12 - 2;
        var title = new FormattedText(Title, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, tf, LT.TextSize(12), new SolidColorBrush(LT.Text))
            { MaxTextWidth = textW, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
        var detail = new FormattedText(Detail, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(LT.Ui), LT.TextSize(11), new SolidColorBrush(LT.Muted))
            { MaxTextWidth = textW, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
        double y0 = 23 - (title.Height + 1 + detail.Height) / 2;
        ctx.DrawText(title, new Point(12 + 26 + 12, y0));
        ctx.DrawText(detail, new Point(12 + 26 + 12, y0 + title.Height + 1));
        Icons.Draw(ctx, IconKind.Chevron, new Point(w - 12 - 4.5 + 2 * h, 23), 11 * Prefs.Shared.IconScale, LT.Faint);
    }
}
