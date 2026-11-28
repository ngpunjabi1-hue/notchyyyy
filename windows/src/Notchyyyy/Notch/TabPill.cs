using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Notchyyyy.Motion;
using Notchyyyy.Theme;

namespace Notchyyyy.Notch;

/// <summary>
/// The floating tab bar under the open notch: a glass capsule of round tab buttons with a soft circle that
/// slides to the selected tab, and a round glass close button beside it. Sizes follow the Mac app's TabPill:
/// 34 pt buttons 4 pt apart, 5 pt inside the capsule, the close button 42 pt and 12 pt away.
/// Without a real backdrop blur (an overlay window can't blur the desktop behind part of itself), the glass is
/// opaque with a hairline edge and a soft top highlight: any see-through tint let text behind it show through,
/// readable, on the Windows runner (a 90 % tint was tried and rejected).
/// </summary>
public sealed class TabPill : Control, Avalonia.Rendering.ICustomHitTest
{
    public const double Button = 34, Spacing = 4, Inset = 5, CloseSize = 42, CloseGap = 12, RowHeight = 42;

    private readonly NotchModel _m;
    private readonly IClock _clock;
    private readonly Action _kick;
    private IReadOnlyList<Tab> _tabs;
    private readonly SpringValue _indicator;
    private readonly Dictionary<int, SpringValue> _hover = new();     // per button: 0…1
    private readonly Dictionary<Tab, SpringValue> _selected = new();  // per tab: 0…1 (colour)
    private int _hot = -1, _pressed = -1;
    private double _now;

    /// Called when the close button is clicked.
    public Action? Close { get; set; }

    public TabPill(NotchModel m, IClock clock, Action kick)
    {
        _m = m; _clock = clock; _kick = kick;
        _tabs = m.VisibleTabs;
        _indicator = new SpringValue(ButtonCentre(Math.Max(0, IndexOf(m.Tab))), 0.01);
        foreach (var t in Enum.GetValues<Tab>()) _selected[t] = new SpringValue(t == m.Tab ? 1 : 0, 0.002);
        Cursor = new Cursor(StandardCursorType.Arrow);
    }

    public double GlassWidth => _tabs.Count * Button + (_tabs.Count - 1) * Spacing + 2 * Inset;
    public double TotalWidth => GlassWidth + CloseGap + CloseSize;
    public double ButtonCentre(int i) => Inset + Button / 2 + i * (Button + Spacing);
    public double CloseCentre => GlassWidth + CloseGap + CloseSize / 2;
    public double CurrentIndicator => _indicator.Value(_now);
    public IReadOnlyList<Tab> Tabs => _tabs;
    private int IndexOf(Tab t) { for (int i = 0; i < _tabs.Count; i++) if (_tabs[i] == t) return i; return -1; }

    protected override Size MeasureOverride(Size availableSize) => new(TotalWidth, RowHeight);

    /// The visible tabs changed (Settings › Tabs) or the theme did: re-read them and redraw.
    public void Refresh(double now)
    {
        _tabs = _m.VisibleTabs;
        _hover.Clear(); _hot = -1;
        int i = Math.Max(0, IndexOf(_m.Tab));
        _indicator.Snap(ButtonCentre(i));
        InvalidateMeasure();
        InvalidateVisual();
    }

    public void Select(Tab tab, double now)
    {
        int i = IndexOf(tab);
        if (i >= 0) _indicator.AnimateTo(ButtonCentre(i), LT.Snappy, now);
        foreach (var (t, s) in _selected) s.AnimateTo(t == tab ? 1 : 0, LT.Snappy, now);
    }

    public bool Animating(double now) =>
        !_indicator.IsSettled(now) || _hover.Values.Any(h => !h.IsSettled(now)) || _selected.Values.Any(s => !s.IsSettled(now));

    public void Update(double now) { _now = now; InvalidateVisual(); }

    // ---- pointer ----

    /// Which button is under <paramref name="p"/>: a tab index, <c>_tabs.Count</c> for close, or -1.
    public int ButtonAt(Point p)
    {
        double cy = RowHeight / 2;
        for (int i = 0; i < _tabs.Count; i++)
            if (Dist(p, new Point(ButtonCentre(i), cy)) <= Button / 2) return i;
        if (Dist(p, new Point(CloseCentre, cy)) <= CloseSize / 2) return _tabs.Count;
        return -1;
    }
    /// Avalonia asks this to decide whether the pointer is over the bar: anywhere on the capsule or the close button.
    public bool HitTest(Point p)
    {
        double cy = RowHeight / 2, gh = Button + 2 * Inset;
        bool inCapsule = p.X >= 0 && p.X <= GlassWidth && Math.Abs(p.Y - cy) <= gh / 2;
        return inCapsule || Dist(p, new Point(CloseCentre, cy)) <= CloseSize / 2;
    }

    private static double Dist(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private void SetHot(int i)
    {
        if (i == _hot) return;
        double now = _clock.Now;
        if (_hot >= 0) Hover(_hot).AnimateTo(0, LT.Hover, now);
        _hot = i;
        if (_hot >= 0) Hover(_hot).AnimateTo(1, LT.Hover, now);
        // the tab's name as a tooltip, like the Mac's .help()
        ToolTip.SetTip(this, i < 0 ? null : i == _tabs.Count ? "Close" : _tabs[i].Title());
        Cursor = new Cursor(i >= 0 ? StandardCursorType.Hand : StandardCursorType.Arrow);
        _kick();
    }
    private SpringValue Hover(int i) => _hover.TryGetValue(i, out var s) ? s : _hover[i] = new SpringValue(0, 0.002);

    protected override void OnPointerMoved(PointerEventArgs e) { base.OnPointerMoved(e); SetHot(ButtonAt(e.GetPosition(this))); }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); SetHot(-1); _pressed = -1; }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _pressed = ButtonAt(e.GetPosition(this));
        if (_pressed >= 0) e.Handled = true;
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        int i = ButtonAt(e.GetPosition(this));
        if (i >= 0 && i == _pressed) Activate(i);
        _pressed = -1;
    }

    /// What a click on button <paramref name="i"/> does (tests call it too).
    public void Activate(int i)
    {
        if (i == _tabs.Count) Close?.Invoke();
        else if (i >= 0 && i < _tabs.Count) _m.Tab = _tabs[i];
    }

    // ---- drawing ----

    private static void Glass(DrawingContext ctx, Geometry shape, Rect bounds)
    {
        ctx.DrawGeometry(new SolidColorBrush(LT.Bg), null, shape);
        // a soft highlight along the top edge, fading out by the middle
        var top = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 0.55, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Colors.White.WithOpacity(LT.Light ? 0.9 : 0.16), 0),
                new GradientStop(Colors.White.WithOpacity(0), 1),
            },
        };
        using (ctx.PushGeometryClip(shape))
            ctx.DrawGeometry(null, new Pen(top, 2), shape);
        ctx.DrawGeometry(null, new Pen(new SolidColorBrush(LT.Border), 1), shape);
    }

    public override void Render(DrawingContext ctx)
    {
        double now = _now, cy = RowHeight / 2;

        // the capsule (5 pt around 34 pt buttons: 44 pt tall, centred on the 42 pt row like SwiftUI lays it out)
        double gh = Button + 2 * Inset;
        var capsule = new Rect(0, cy - gh / 2, GlassWidth, gh);
        Glass(ctx, new RectangleGeometry(capsule, gh / 2, gh / 2), capsule);

        // the selected tab's circle, sliding between tabs
        double ix = _indicator.Value(now);
        ctx.DrawEllipse(new SolidColorBrush(LT.Wash.WithOpacity(0.14)), null, new Point(ix, cy), Button / 2, Button / 2);

        for (int i = 0; i < _tabs.Count; i++)
        {
            var t = _tabs[i];
            double h = _hover.TryGetValue(i, out var hs) ? Math.Clamp(hs.Value(now), 0, 1.3) : 0;
            double sel = Math.Clamp(_selected[t].Value(now), 0, 1);
            // muted at rest, brighter on hover, full text colour when selected; unselected tabs swell a little on hover
            var rest = Mix(LT.Muted, LT.Text.WithOpacity(0.85), Math.Clamp(h, 0, 1));
            var color = Mix(rest, LT.Text, sel);
            double scale = 1 + 0.08 * h * (1 - sel);
            var c = new Point(ButtonCentre(i), cy);
            Icons.Draw(ctx, t.Icon(), c, 19 * scale * Prefs.Shared.IconScale, color);   // centred, so a bigger size is the swell
        }

        // the close button: a glass circle with an ×, swelling a little on hover
        double ch = _hover.TryGetValue(_tabs.Count, out var cs) ? Math.Clamp(cs.Value(now), 0, 1.3) : 0;
        double k = 1 + 0.06 * ch, r = CloseSize / 2 * k;
        var cc = new Point(CloseCentre, cy);
        var circle = new Rect(cc.X - r, cc.Y - r, 2 * r, 2 * r);
        Glass(ctx, new EllipseGeometry(circle), circle);
        Icons.Draw(ctx, IconKind.Close, cc, 24 * k * Prefs.Shared.IconScale, LT.Text);
    }

    private static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        (byte)Math.Round(a.A + (b.A - a.A) * t), (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));
}
