using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Notchyyyy.Motion;
using Notchyyyy.Theme;

namespace Notchyyyy.Notch;

/// <summary>
/// The notch itself: a shape whose width and height ride separate springs (that offset is what makes it
/// feel poured, not scaled), and the content, which is laid out at its final size and revealed by the
/// growing shape, so nothing inside squeezes or slides. No shadow: the notch sits flat against the screen edge.
/// A port of NotchStage in the Mac app's NotchView.swift.
/// </summary>
public sealed class NotchStage : Panel
{
    private readonly NotchModel _m;
    private readonly IClock _clock;

    // the shape
    private readonly SpringValue _w, _h, _top, _bottom, _stroke, _activity;
    // fades
    private readonly TweenValue _bar = new(1), _pour = new(1);
    // closed is always black, open takes the theme's background: a quick ease of its own, so the light theme
    // never shows a muddy grey body or a grey pill lingering after it closes
    private readonly TweenValue _fill = new(0);

    private readonly ShapeLayer _shape;
    private readonly Panel _barHost = new() { ClipToBounds = false };
    private readonly Panel _bodyHost = new() { ClipToBounds = false };
    private readonly CollapsedBar _barView;
    private readonly Sprout.SproutView _buddy;
    public Sprout.SproutView Buddy => _buddy;
    private readonly ExpandedBody _bodyView;
    // the tab bar under the open notch: arrives a beat after the shape (dropping 18 pt and pouring in on its
    // own springy curve), leaves with a quick fade
    private readonly TabPill _pill;
    private readonly SpringValue _pillIn = new(1, 0.002);
    private readonly TweenValue _pillFade = new(0);

    private bool _frameRequested;

    /// Called after every animation frame with (time, width, height, ms the update took): the frame log uses it.
    public Action<double, double, double, double>? FrameRendered { get; set; }

    public NotchStage(NotchModel model, IClock clock)
    {
        _m = model; _clock = clock;
        var c = model.ClosedSize;
        _w = new SpringValue(c.Width); _h = new SpringValue(c.Height);
        _top = new SpringValue(6); _bottom = new SpringValue(10);
        _stroke = new SpringValue(0, 0.001);
        _activity = new SpringValue(model.Activity ? 1 : 0, 0.001);

        _barView = new CollapsedBar(model, clock);
        _bodyView = new ExpandedBody(model, clock);
        _pill = new TabPill(model, clock, () => Kick()) { Opacity = 0, IsHitTestVisible = false };
        _barHost.Children.Add(_barView);
        // the little Sprout in the leading slot (30 pt, 18 pt in from the pill's end), drawn at 30 fps
        _buddy = new Sprout.SproutView(clock, 30)
        {
            Fps = 30, GazeFollowsMood = true, IsHitTestVisible = false,
            FpsOverride = double.TryParse(Debug.Value("--mini-fps"), System.Globalization.CultureInfo.InvariantCulture, out var mf) ? mf : null,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(18, 0, 0, 0), IsVisible = Prefs.Shared.Buddy,
            Source = t => (model.BuddyMood(t), model.Buddy.Gaze, model.MusicPlaying, model.Buddy.Pace),
        };
        _barHost.Children.Add(_buddy);
        _bodyHost.Children.Add(_bodyView);
        // Hosts are never hidden with IsVisible: switching that back on re-lays-out the whole body at the
        // moment it opens (a dropped frame, measured). Opacity 0 isn't drawn, and keeps the layout ready.
        ShowBody(false);
        _shape = new ShapeLayer(this);
        Children.Add(_shape);
        Children.Add(_barHost);
        Children.Add(_bodyHost);
        Children.Add(_pill);

        _m.Changed += OnChanged;
        Update(clock.Now);
    }

    public double CurrentWidth { get; private set; }
    public double CurrentHeight { get; private set; }
    // read-only probes for the frame-by-frame tests
    public double CurrentTop => CurTop;
    public double CurrentBottom => CurBottom;
    public Color CurrentFill => Lerp(Colors.Black, LT.Bg, Math.Clamp(_fill.Value(_now), 0, 1));
    public double CurrentPour => Math.Clamp(_pour.Value(_now), 0, 1);
    public TabPill Pill => _pill;
    public ExpandedBody Body => _bodyView;
    /// Where the tab bar is drawn this frame (stage coordinates, after its arrival offset), and how opaque.
    public Rect PillRect { get; private set; }
    public double PillOpacity => _pill.Opacity;


    /// True while anything is still moving.
    public bool Animating(double now) =>
        !(_w.IsSettled(now) && _h.IsSettled(now) && _top.IsSettled(now) && _bottom.IsSettled(now) && _fill.IsSettled(now)
          && _stroke.IsSettled(now) && _activity.IsSettled(now) && _bar.IsSettled(now) && _pour.IsSettled(now)
          && _pillIn.IsSettled(now) && _pillFade.IsSettled(now) && !_pill.Animating(now) && !_bodyView.Animating(now));

    private void Go(SpringValue s, double target, SpringSpec spec, double now)
    {
        if (Math.Abs(s.Target - target) > 1e-9) s.AnimateTo(target, spec, now);
    }

    private void OnChanged(string what)
    {
        double now = _clock.Now;
        bool e = _m.Expanded;
        var closed = _m.ClosedSize;
        double w = e ? _m.BodySize.Width : closed.Width;
        double h = e ? _m.BodySize.Height : closed.Height;
        double t = e ? 14 : 6;
        double b = e ? 34 : (_m.Hovering ? 14 : 10);

        switch (what)
        {
            case nameof(NotchModel.Expanded):
                var wide = e ? LT.OpenWide : LT.CloseWide;
                var tall = e ? LT.OpenTall : LT.CloseTall;
                Go(_w, w, wide, now);
                Go(_h, h, tall, now);
                Go(_top, t, tall, now);
                Go(_bottom, b, tall, now);
                _fill.AnimateTo(e ? 1 : 0, e ? 0.24 : 0.20, Ease.Out, now);
                Go(_stroke, e ? 1 : 0, wide, now);
                if (e)
                {
                    // the content arrives a beat after the shape starts to open
                    if (!_bodyShown) _pour.Snap(1);
                    _pour.AnimateTo(0, 0.32, Ease.Out, now, 0.1);
                    _bar.AnimateTo(0, 0.10, Ease.Out, now);   // gone by the time the body starts to arrive
                    if (_m.Onboarded)
                    {
                        if (_pill.Opacity < 0.001) _pillIn.Snap(1);
                        _pillIn.AnimateTo(0, new SpringSpec(0.45, 0.2, 0.14), now);
                        _pillFade.Snap(1);
                    }
                }
                else
                {
                    // leaving: everything goes at once and quickly
                    _pour.AnimateTo(1, 0.12, Ease.In, now);
                    // the pill's own content waits until the body's header has all but gone, so the two
                    // never sit on screen together (a clean hand-off rather than a cross-fade)
                    _bar.AnimateTo(1, 0.18, Ease.Out, now, 0.12);   // starts as the body finishes leaving (0.12 s)
                    _pillFade.AnimateTo(0, 0.10, Ease.In, now);
                }
                _pill.IsHitTestVisible = e;
                break;
            case nameof(NotchModel.Hovering):
                Go(_w, w, LT.Hover, now);
                Go(_h, h, LT.Hover, now);
                Go(_bottom, b, LT.Hover, now);
                break;
            case nameof(NotchModel.Tab):
                _bodyView.SwitchTo(_m.Tab, now, animate: _bodyShown);
                _pill.Select(_m.Tab, now);
                break;
            default:   // live activity came or went
                _barView.Refresh();
                Go(_w, w, LT.OpenWide, now);
                Go(_activity, _m.Activity ? 1 : 0, LT.OpenWide, now);
                break;
        }
        Kick();
    }

    /// Ask for animation frames until everything settles.
    public void Kick()
    {
        if (_frameRequested) return;
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        _frameRequested = true;
        top.RequestAnimationFrame(_ =>
        {
            _frameRequested = false;
            double now = _clock.Now;
            Update(now);
            if (Animating(now)) Kick();
        });
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Update(_clock.Now);
        Kick();
        WarmUp();
    }

    private bool _warm;
    private int _warmFrames;

    /// <summary>
    /// The first open used to stall (up to 46 ms measured): the body's code ran for the first time and the
    /// renderer drew its text for the first time. So right after launch the springs run once on throwaway
    /// values and the body is drawn for a few real frames at 0.2 % opacity, inside the black closed pill
    /// (under half a colour level, so nothing shows); the first real open then costs no more than later ones.
    /// </summary>
    private void WarmUp()
    {
        if (_warm || TopLevel.GetTopLevel(this) is null) return;
        _warm = true;
        var s = new SpringValue(0);
        foreach (var spec in new[] { LT.OpenWide, LT.OpenTall, LT.CloseTall, LT.CloseWide, LT.Hover })
        { s.AnimateTo(1, spec, 0); s.Value(0.1); s.Velocity(0.1); s.IsSettled(0.1); }
        _ = new TweenValue(0).Value(0);
        _ = Ease.Out.At(0.5) + Ease.In.At(0.5);
        _warmFrames = 3;
        ForceFrames(_warmFrames + 1);
        double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        Sprout.SproutPainter.Warm(30, scale);
        Sprout.SproutPainter.Warm(Notch.HomeView.SproutSize, scale);
    }

    private void WarmFrame()
    {
        ShowBody(true);
        Pour(0.5);
        _bodyView.Opacity = 0.002;
        var r = ShapeRect;
        _bodyHost.Clip = NotchShape.Build(r.Translate(new Vector(-_bodyHost.Bounds.X, 0)), CurTop, CurBottom);
    }

    private void ForceFrames(int n)
    {
        if (n <= 0 || TopLevel.GetTopLevel(this) is not { } top) return;
        top.RequestAnimationFrame(_ => { Update(_clock.Now); ForceFrames(n - 1); });
    }

    /// Moves everything to time <paramref name="now"/>. Frame-by-frame renders call it directly.
    public void Update(double now)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        _now = now;
        CurrentWidth = _w.Value(now);
        CurrentHeight = Math.Max(1, _h.Value(now));

        double pour = Math.Clamp(_pour.Value(now), 0, 1);
        bool bodyAlive = _m.Expanded || pour < 0.999;
        if (_warmFrames > 0 && !bodyAlive) { _warmFrames--; WarmFrame(); }
        else
        {
            ShowBody(bodyAlive);
            if (bodyAlive) Pour(pour);
        }
        double barOpacity = Math.Clamp(_bar.Value(now), 0, 1) * Math.Clamp(_activity.Value(now), 0, 1);
        _barHost.Opacity = barOpacity;
        _barShown = barOpacity > 0.001;
        _barHost.IsHitTestVisible = _barShown;
        _barView.Live = _barShown;
        _buddy.Active = _barShown;
        // the big Sprout on Home draws only while the open body can be seen
        if (_bodyView.CurrentHome is { } home) home.Sprout.Active = _bodyShown;

        UpdatePill(now);
        _bodyView.Update(now);
        UpdateClips();
        _shape.InvalidateVisual();
        FrameRendered?.Invoke(now, CurrentWidth, CurrentHeight, System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    private bool _bodyShown, _barShown;

    private void ShowBody(bool on)
    {
        _bodyShown = on;
        _bodyHost.Opacity = on ? 1 : 0;
        _bodyHost.IsHitTestVisible = on;
    }

    /// The body arriving or leaving: fade, settle and drop a few points (GPU-cheap: no blur).
    private void Pour(double pour)
    {
        double s = 1 - pour * 0.04, half = _m.BodySize.Width / 2;
        _bodyView.Opacity = 1 - pour;
        _bodyView.RenderTransform = new MatrixTransform(
            Matrix.CreateTranslation(-half, 0) * Matrix.CreateScale(s, s) * Matrix.CreateTranslation(half, -pour * 10));
    }

    /// The tab bar's resting place: centred, 10 pt below the open body (it doesn't ride the body's spring:
    /// like the Mac's VStack, it's laid out at the open size at once and animates on its own).
    private Rect PillHomeIn(double stageWidth) =>
        new((stageWidth - _pill.TotalWidth) / 2, _m.BodySize.Height + _m.PillGap, _pill.TotalWidth, TabPill.RowHeight);

    private void UpdatePill(double now)
    {
        // Pour (fade, settle from 96 %, drop 10 pt) combined with an 18 pt drop, on one springy amount
        double a = _pillIn.Value(now);
        double opacity = Math.Clamp(1 - a, 0, 1) * Math.Clamp(_pillFade.Value(now), 0, 1);
        _pill.Opacity = opacity;
        double s = 1 - 0.04 * a, half = _pill.TotalWidth / 2, dy = -28 * a;
        _pill.RenderTransformOrigin = RelativePoint.TopLeft;
        _pill.RenderTransform = new MatrixTransform(Matrix.CreateTranslation(-half, 0) * Matrix.CreateScale(s, s) * Matrix.CreateTranslation(half, dy));
        var home = PillHomeIn(Bounds.Width);
        PillRect = new Rect(home.X + half * (1 - s), home.Y + dy, home.Width * s, home.Height * s);
        _pill.Update(now);
    }

    private Rect ShapeRect => new((Bounds.Width - CurrentWidth) / 2, 0, CurrentWidth, CurrentHeight);
    // everything drawn in one frame reads the same instant, so the clip and the fill never disagree
    private double _now;
    private double CurTop => _top.Value(_now);
    private double CurBottom => _bottom.Value(_now);

    private void UpdateClips()
    {
        var r = ShapeRect;
        double t = CurTop, b = CurBottom;
        if (_barShown) _barHost.Clip = NotchShape.Build(r.Translate(new Vector(-_barHost.Bounds.X, 0)), t, b);
        if (_bodyShown) _bodyHost.Clip = NotchShape.Build(r.Translate(new Vector(-_bodyHost.Bounds.X, 0)), t, b);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _shape.Measure(availableSize);
        _barHost.Measure(new Size(BarWidth, _m.NotchSize.Height));
        _bodyHost.Measure(_m.BodySize);
        _pill.Measure(availableSize);
        return availableSize;
    }

    private double BarWidth => _m.NotchSize.Width + 12 + _m.Extra;

    protected override Size ArrangeOverride(Size finalSize)
    {
        double cx = finalSize.Width / 2;
        _shape.Arrange(new Rect(finalSize));
        _barHost.Arrange(new Rect(cx - BarWidth / 2, 0, BarWidth, _m.NotchSize.Height));
        _bodyHost.Arrange(new Rect(cx - _m.BodySize.Width / 2, 0, _m.BodySize.Width, _m.BodySize.Height));
        _pill.Arrange(PillHomeIn(finalSize.Width));   // finalSize: Bounds isn't updated yet during arrange
        UpdateClips();
        return finalSize;
    }

    /// Draws the shape behind the content (Panel.Render is sealed in Avalonia 12).
    private sealed class ShapeLayer : Control
    {
        private readonly NotchStage _s;
        public ShapeLayer(NotchStage s) { _s = s; IsHitTestVisible = false; }
        public override void Render(DrawingContext ctx) => _s.Draw(ctx);
    }

    private void Draw(DrawingContext ctx)
    {
        double now = _now;
        var r = ShapeRect;
        double t = CurTop, b = CurBottom;
        var geom = NotchShape.Build(r, t, b);
        // closed, the pill is always black; open, it takes the theme's background
        ctx.DrawGeometry(new SolidColorBrush(CurrentFill), null, geom);
        double so = Math.Clamp(_stroke.Value(now), 0, 1);
        if (so > 0.001)
        {
            using (ctx.PushGeometryClip(geom))
                ctx.DrawGeometry(null, new Pen(new SolidColorBrush(LT.Wash.WithOpacity(0.08 * so)), 1), NotchShape.Build(r, t, b, closed: false));
        }
    }

    private static Color Lerp(Color a, Color b, double t) => Color.FromArgb(
        (byte)Math.Round(a.A + (b.A - a.A) * t), (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));
}
