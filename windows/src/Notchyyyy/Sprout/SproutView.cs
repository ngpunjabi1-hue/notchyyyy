using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Notchyyyy.Motion;
using Notchyyyy.Theme;

namespace Notchyyyy.Sprout;

/// <summary>
/// Sprout on screen. The control is a <c>Size</c>-pt box; Sprout is drawn on a canvas 1.3× bigger, centred on it
/// (room for the glow and the signs above its head), resting on the floor 11.5 % up from the canvas's bottom,
/// exactly like the Mac app's MiniSprout / SproutView. The pose is worked out here each frame and handed to Skia.
/// </summary>
public class SproutView : Control, Avalonia.Rendering.ICustomHitTest
{
    private readonly IClock _clock;
    private readonly SproutTween _tween = new();
    private bool _ticking, _attached;
    private double _lastDraw = double.NegativeInfinity;
    private TimeSpan _lastTick = TimeSpan.MinValue;

    public SproutView(IClock clock, double size)
    {
        _clock = clock;
        BoxSize = size;
        Width = size; Height = size;
        ClipToBounds = false;
    }

    public double BoxSize { get; }
    /// What Sprout feels when nothing else (a tap, a hover) overrides it.
    public SproutMood Mood { get; set; } = SproutMood.Idle;
    /// Where Sprout looks (each axis −1…1, y down).
    public Point Gaze { get; set; }
    public bool Headphones { get; set; }
    public double Pace { get; set; } = 1;
    /// Frames per second while visible: the little one in the pill needs fewer than the big one on Home.
    public double Fps { get; set; } = 60;
    /// When set, asked every frame for the mood, gaze, headphones and typing pace (so time-based moods, like a cheer
    /// running out or night falling, change on their own).
    public Func<double, (SproutMood Mood, Point Gaze, bool Headphones, double Pace)>? Source { get; set; }

    /// Test flags --sprout-fps / --mini-fps override the rates (0 = every display frame).
    public double? FpsOverride { get; set; }

    /// Off while whatever holds Sprout is hidden (opacity 0): no frames are drawn then.
    public bool Active { get; set; } = true;
    /// Pokes and hover (the big Sprout on Home); off for the little one.
    public bool Interactive { get; set; }
    /// How far down (0…1 of its box) Sprout answers the pointer: less when part of it is hidden below an edge.
    public double HitDepth { get; set; } = 1;

    /// The little Sprout's rule (NotchBuddy): asleep or listening it looks straight ahead; running, it looks where it's going.
    public bool GazeFollowsMood { get; set; }

    /// The mood actually drawn this frame (after taps and hover).
    public SproutMood Showing { get; private set; }

    // ---- poking (the Mac app's SproutView) ----
    private bool _hover;
    private SproutMood? _reaction;
    private double _reactionUntil = double.NegativeInfinity, _dizzyUntil = double.NegativeInfinity, _lastTap = double.NegativeInfinity;
    private double _tapAt = double.NegativeInfinity;
    private int _streak;

    /// Each quick tap escalates: giggle, love, startled, annoyed … and the fifth makes Sprout dizzy.
    public void Poke()
    {
        double now = _clock.Now;
        _tapAt = now;
        if (now < _dizzyUntil) { _dizzyUntil = Math.Max(_dizzyUntil, now + 2.2); return; }   // still spinning: a little longer
        _streak = now - _lastTap < 1.3 ? _streak + 1 : 1;      // a pause starts the streak over
        _lastTap = now;
        if (_streak >= 5) { _streak = 0; _reaction = null; _dizzyUntil = now + 3.4; return; }
        _reaction = new[] { SproutMood.Cheer, SproutMood.Love, SproutMood.Surprised, SproutMood.Hurt }[_streak - 1];
        _reactionUntil = now + 1.5;
    }

    public SproutMood CurrentMood(double now)
    {
        if (now < _dizzyUntil) return SproutMood.Dizzy;
        if (_reaction is { } r && now < _reactionUntil) return r;
        if (_hover) return SproutMood.Happy;
        return Mood;
    }

    /// The hit area: the middle of the box from 22 % down to <see cref="HitDepth"/> (the Mac's TopPart).
    public bool HitTest(Point p) => Interactive
        && p.X >= BoxSize * 0.15 && p.X <= BoxSize * 0.85 && p.Y >= BoxSize * 0.22 && p.Y <= BoxSize * HitDepth;

    protected override void OnPointerEntered(PointerEventArgs e) { base.OnPointerEntered(e); if (Interactive) _hover = true; }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); _hover = false; }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!Interactive) return;
        Poke();
        e.Handled = true;
    }

    // ---- frames ----

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        Tick();
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { base.OnDetachedFromVisualTree(e); _attached = false; }

    /// Keeps drawing while on screen, at <see cref="Fps"/>, stepping on the display's own frame clock: a frame is
    /// asked for when a whole Sprout frame has passed, give or take a third of one, so on a 120 Hz screen 60 fps is
    /// exactly every other display frame (timing it from when Sprout last drew made it skip unevenly: measured).
    private void Tick()
    {
        if (_ticking || !_attached || TopLevel.GetTopLevel(this) is not { } top) return;
        _ticking = true;
        top.RequestAnimationFrame(frame =>
        {
            _ticking = false;
            if (!_attached) return;
            double since = _lastTick == TimeSpan.MinValue ? double.MaxValue : (frame - _lastTick).TotalSeconds;
            double fps = FpsOverride ?? Fps;
            if (Active && IsEffectivelyVisible && (fps <= 0 || since >= (1 - 0.34) / fps))
            {
                _lastTick = frame;
                InvalidateVisual();
            }
            Tick();
        });
    }

    /// Test hook: called after each frame Sprout draws (the perf probe times them).
    public static Action<SproutView>? Rendered { get; set; }
    /// Test hook: how long each Skia draw of Sprout took (ms), with Sprout's size.
    public static Action<double, double>? Painted { get; set; }

    public override void Render(DrawingContext ctx)
    {
        double t = _clock.Now;
        _lastDraw = t;
        Rendered?.Invoke(this);
        if (Source is { } src) { var s = src(t); Mood = s.Mood; Gaze = s.Gaze; Headphones = s.Headphones; Pace = s.Pace; }
        var mood = CurrentMood(t);
        Showing = mood;
        var look = !GazeFollowsMood ? Gaze : mood is SproutMood.Sleepy or SproutMood.Music ? default : mood == SproutMood.Running ? new Point(0.6, 0) : Gaze;
        var (pose, gaze) = _tween.Pose(mood, look, t);
        var (hp, phase) = _tween.Accessories(Headphones, Pace, t);
        // a jelly squish after each tap
        double since = t - _tapAt;
        double squish = since < 0.6 ? 0.16 * Math.Exp(-since * 8) * Math.Cos(since * 24) : 0;
        double canvas = BoxSize * 1.3, off = (BoxSize - canvas) / 2, inset = canvas * 0.115;
        var rect = new Rect(off + inset, off + inset, canvas - 2 * inset, canvas - 2 * inset);
        double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        ctx.Custom(new Op(new Rect(off, off, canvas, canvas), rect, pose, t, gaze, squish, hp, phase, Prefs.Shared.Accent, LT.Light, scale));
    }

    private sealed class Op : ICustomDrawOperation
    {
        private readonly Rect _sprout;
        private readonly SproutPose _pose;
        private readonly double _t, _squash, _hp, _phase;
        private readonly Point _gaze;
        private readonly Accent _accent;
        private readonly bool _light;
        private readonly double _scale;

        public Op(Rect bounds, Rect sprout, SproutPose pose, double t, Point gaze, double squash, double hp, double phase, Accent accent, bool light, double scale)
        {
            _scale = scale;
            Bounds = bounds; _sprout = sprout; _pose = pose; _t = t; _gaze = gaze; _squash = squash; _hp = hp; _phase = phase;
            _accent = accent; _light = light;
        }

        public Rect Bounds { get; }
        public bool HitTest(Point p) => false;
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Dispose() { }

        public void Render(ImmediateDrawingContext context)
        {
            var lease = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (lease is null) return;
            using var l = lease.Lease();
            var canvas = l.SkCanvas;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            canvas.Save();
            SproutPainter.Draw(canvas, _sprout, _pose, _t, _gaze, _squash, true, _hp, _phase, _accent, _light, _scale);
            canvas.Restore();
            Painted?.Invoke(System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds, _sprout.Width);
        }
    }
}
