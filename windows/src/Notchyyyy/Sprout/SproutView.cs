using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using SkiaSharp;
using Notchyyyy.Motion;
using Notchyyyy.Theme;

namespace Notchyyyy.Sprout;

/// <summary>
/// Sprout on screen. The control is a <c>BoxSize</c>-pt box; Sprout is drawn on a canvas 1.3× bigger, centred on it
/// (room for the glow and the signs above its head), resting on the floor 11.5 % up from the canvas's bottom,
/// exactly like the Mac app's MiniSprout / SproutView.
///
/// Smoothness: Sprout is animated by Avalonia's render thread (a composition visual whose handler is called on every
/// display frame), not by the UI thread asking for redraws. Redraws asked from the UI thread kept an uneven rhythm
/// (16–25 ms on a 120 Hz Mac, 16–48 ms on Windows; measured) even though drawing Sprout takes ~0.15 ms. Pointer input
/// (hover, pokes) stays on the UI thread; the render thread only reads that state.
/// </summary>
public class SproutView : Control, Avalonia.Rendering.ICustomHitTest
{
    private readonly IClock _clock;
    private CompositionCustomVisual? _visual;
    private double _scale = 1;

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
    /// Frames per second while visible (0 = every display frame).
    public double Fps { get; set; } = 60;
    /// Test flags --sprout-fps / --mini-fps override the rate.
    public double? FpsOverride { get; set; }
    /// Pokes and hover (the big Sprout on Home); off for the little one.
    public bool Interactive { get; set; }
    /// How far down (0…1 of its box) Sprout answers the pointer: less when part of it is hidden below an edge.
    public double HitDepth { get; set; } = 1;
    /// The little Sprout's rule (NotchBuddy): asleep or listening it looks straight ahead; running, it looks where it's going.
    public bool GazeFollowsMood { get; set; }
    /// When set, asked every frame (on the render thread: it must only read) for the mood, gaze, headphones and pace,
    /// so time-based moods (a cheer running out, night falling) change on their own.
    public Func<double, (SproutMood Mood, Point Gaze, bool Headphones, double Pace)>? Source { get; set; }

    private volatile bool _active = true;
    /// Off while whatever holds Sprout is hidden (opacity 0): no frames are drawn then.
    public bool Active
    {
        get => _active;
        set { if (_active == value) return; _active = value; if (value) _visual?.SendHandlerMessage(Kick); }
    }

    /// The mood actually drawn in the last frame (after taps and hover).
    public SproutMood Showing { get; private set; }

    /// Test hooks (called on the render thread): after each frame, and how long the Skia draw took (ms) with Sprout's size.
    public static Action<SproutView>? Rendered { get; set; }
    public static Action<double, double>? Painted { get; set; }

    // ---- poking (the Mac app's SproutView), on the UI thread ----
    private volatile bool _hover;
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

    // ---- the render-thread visual ----

    private static readonly object Kick = new();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Cursor = new Cursor(StandardCursorType.Hand);
        _scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        if (_visual is null && ElementComposition.GetElementVisual(this)?.Compositor is { } compositor)
        {
            double canvas = BoxSize * 1.3, off = (BoxSize - canvas) / 2;
            _visual = compositor.CreateCustomVisual(new Handler(this));
            _visual.Size = new Vector(canvas, canvas);
            _visual.Offset = new Vector3D(off, off, 0);
            ElementComposition.SetElementChildVisual(this, _visual);
        }
        _visual?.SendHandlerMessage(Kick);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ElementComposition.SetElementChildVisual(this, null);
        _visual = null;
    }

    private sealed class Handler : CompositionCustomVisualHandler
    {
        private readonly SproutView _v;
        private readonly SproutTween _tween = new();
        private TimeSpan _last = TimeSpan.MinValue;
        private bool _running;

        public Handler(SproutView v) => _v = v;

        public override void OnMessage(object message)
        {
            if (message == Kick && !_running) { _running = true; Invalidate(); RegisterForNextAnimationFrameUpdate(); }
        }

        /// Every display frame: draw a new Sprout frame if a whole one has passed (give or take a third), and keep going
        /// while Sprout can be seen.
        public override void OnAnimationFrameUpdate()
        {
            if (!_v._active) { _running = false; return; }
            double fps = _v.FpsOverride ?? _v.Fps;
            var now = CompositionNow;
            if (fps <= 0 || _last == TimeSpan.MinValue || (now - _last).TotalSeconds >= (1 - 0.34) / fps)
            {
                _last = now;
                Invalidate();
            }
            RegisterForNextAnimationFrameUpdate();
        }

        public override void OnRender(ImmediateDrawingContext context)
        {
            var v = _v;
            double t = v._clock.Now;
            if (v.Source is { } src) { var s = src(t); v.Mood = s.Mood; v.Gaze = s.Gaze; v.Headphones = s.Headphones; v.Pace = s.Pace; }
            var mood = v.CurrentMood(t);
            v.Showing = mood;
            var look = !v.GazeFollowsMood ? v.Gaze : mood is SproutMood.Sleepy or SproutMood.Music ? default : mood == SproutMood.Running ? new Point(0.6, 0) : v.Gaze;
            var (pose, gaze) = _tween.Pose(mood, look, t);
            var (hp, phase) = _tween.Accessories(v.Headphones, v.Pace, t);
            // a jelly squish after each tap
            double since = t - v._tapAt;
            double squish = since < 0.6 ? 0.16 * Math.Exp(-since * 8) * Math.Cos(since * 24) : 0;
            double canvas = v.BoxSize * 1.3, inset = canvas * 0.115;
            var rect = new Rect(inset, inset, canvas - 2 * inset, canvas - 2 * inset);

            var lease = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (lease is null) return;
            using var l = lease.Lease();
            // Avalonia applies opacity per drawing call, so a raw Skia draw must apply it itself: without this,
            // Sprout stayed fully opaque while Home cross-faded out around it (seen frame by frame)
            double opacity = l.CurrentOpacity;
            if (opacity <= 0.002) return;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            using var fade = opacity < 0.998 ? new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Round(opacity * 255)) } : null;
            if (fade is not null) l.SkCanvas.SaveLayer(fade);
            l.SkCanvas.Save();
            SproutPainter.Draw(l.SkCanvas, rect, pose, t, gaze, squish, true, hp, phase, Prefs.Shared.Accent, LT.Light, v._scale);
            l.SkCanvas.Restore();
            if (fade is not null) l.SkCanvas.Restore();
            Painted?.Invoke(System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds, rect.Width);
            Rendered?.Invoke(v);
        }
    }
}
