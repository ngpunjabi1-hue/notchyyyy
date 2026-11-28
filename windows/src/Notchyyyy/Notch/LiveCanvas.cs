using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using Notchyyyy.Motion;
using SkiaSharp;

namespace Notchyyyy.Notch;

/// <summary>
/// A control drawn with Skia on Avalonia's render thread, once per display frame while it moves (the way Sprout is
/// drawn, which measured far steadier than redraws asked from the UI thread). The painter runs on the render thread,
/// so it may only read state, and must take the owner's lock for anything the UI thread changes.
/// <see cref="Kick"/> after changing what it shows; it keeps drawing while <see cref="Animating"/> says so.
/// </summary>
public sealed class LiveCanvas : Control
{
    public delegate void Painter(SKCanvas canvas, Size size, double now);

    private readonly IClock _clock;
    private readonly Painter _paint;
    private CompositionCustomVisual? _visual;
    private volatile bool _active = true;
    private static readonly object KickMessage = new();

    public LiveCanvas(IClock clock, Painter paint) { _clock = clock; _paint = paint; IsHitTestVisible = false; ClipToBounds = false; }

    /// Asked on the render thread every frame: keep drawing?
    public Func<double, bool> Animating { get; set; } = _ => false;
    /// Frames per second while animating (0 = every display frame).
    public double Fps { get; set; } = 60;
    /// Room drawn around the bounds (for shadows and glows).
    public double Bleed { get; set; }
    /// Off while whatever holds it is hidden: nothing is drawn then.
    public bool Active
    {
        get => _active;
        set { if (_active == value) return; _active = value; if (value) Kick(); }
    }

    /// Test hook: frames drawn (render thread).
    public int Frames;

    public void Kick() => _visual?.SendHandlerMessage(KickMessage);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_visual is null && ElementComposition.GetElementVisual(this)?.Compositor is { } compositor)
        {
            _visual = compositor.CreateCustomVisual(new Handler(this));
            ElementComposition.SetElementChildVisual(this, _visual);
            Place();
        }
        Kick();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ElementComposition.SetElementChildVisual(this, null);
        _visual = null;
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e) { base.OnSizeChanged(e); Place(); Kick(); }

    private void Place()
    {
        if (_visual is null) return;
        _visual.Size = new Vector(Bounds.Width + 2 * Bleed, Bounds.Height + 2 * Bleed);
        _visual.Offset = new Vector3D(-Bleed, -Bleed, 0);
    }

    private sealed class Handler : CompositionCustomVisualHandler
    {
        private readonly LiveCanvas _c;
        private TimeSpan _last = TimeSpan.MinValue;
        private bool _running;
        public Handler(LiveCanvas c) => _c = c;

        public override void OnMessage(object message)
        {
            if (message != KickMessage) return;
            Invalidate();
            if (!_running) { _running = true; RegisterForNextAnimationFrameUpdate(); }
        }

        public override void OnAnimationFrameUpdate()
        {
            if (!_c._active) { _running = false; return; }
            var now = CompositionNow;
            if (!_c.Animating(_c._clock.Now)) { Invalidate(); _running = false; return; }   // one last frame at rest
            double fps = _c.Fps;
            if (fps <= 0 || _last == TimeSpan.MinValue || (now - _last).TotalSeconds >= (1 - 0.34) / fps) { _last = now; Invalidate(); }
            RegisterForNextAnimationFrameUpdate();
        }

        public override void OnRender(ImmediateDrawingContext context)
        {
            if (!_c._active) return;
            var lease = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (lease is null) return;
            using var l = lease.Lease();
            // a raw Skia draw must apply the opacity Avalonia would have applied (cross-fades, the body arriving)
            double opacity = l.CurrentOpacity;
            if (opacity <= 0.002) return;
            var size = _c.Bounds.Size;
            using var fade = opacity < 0.998 ? new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Round(opacity * 255)) } : null;
            if (fade is not null) l.SkCanvas.SaveLayer(fade);
            l.SkCanvas.Save();
            l.SkCanvas.Translate((float)_c.Bleed, (float)_c.Bleed);
            _c._paint(l.SkCanvas, size, _c._clock.Now);
            l.SkCanvas.Restore();
            if (fade is not null) l.SkCanvas.Restore();
            Interlocked.Increment(ref _c.Frames);
        }
    }
}

/// Small Skia helpers shared by the live canvases.
public static class Sk
{
    public static SKColor C(Color c, double opacity = 1) => new(c.R, c.G, c.B, (byte)Math.Round(c.A * Math.Clamp(opacity, 0, 1)));
    public static SKColor Grey(double white, double opacity = 1) { byte v = (byte)Math.Round(white * 255); return new SKColor(v, v, v, (byte)Math.Round(opacity * 255)); }

    /// SwiftUI's shadow(radius:) is a Gaussian with sigma ≈ radius / 2.
    public static SKPaint Shadow(SKColor color, double radius) => new()
    {
        Color = color, IsAntialias = true, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, (float)(radius / 2)),
    };

    /// Draws <paramref name="img"/> to fill <paramref name="r"/> (aspect fill, centred).
    public static void Fill(SKCanvas c, SKImage img, SKRect r, double opacity = 1)
    {
        float k = Math.Max(r.Width / img.Width, r.Height / img.Height);
        float w = img.Width * k, h = img.Height * k;
        var dst = new SKRect(r.MidX - w / 2, r.MidY - h / 2, r.MidX + w / 2, r.MidY + h / 2);
        using var p = new SKPaint { IsAntialias = true, Color = SKColors.White.WithAlpha((byte)Math.Round(255 * Math.Clamp(opacity, 0, 1))) };
        c.DrawImage(img, dst, new SKSamplingOptions(SKCubicResampler.Mitchell), p);
    }

    /// The Mac's MusicBars: four capsules, each on its own beat, centred on (cx, cy), scaled by k.
    public static void MusicBars(SKCanvas c, double cx, double cy, double k, SKColor color, double t)
    {
        using var p = new SKPaint { Color = color, IsAntialias = true };
        double total = 4 * 2.5 + 3 * 2;
        for (int i = 0; i < 4; i++)
        {
            double v = Math.Abs(Math.Sin(t * (2.6 + i * 0.85) + i * 1.3));
            double bh = (4 + 9 * v) * k, bw = 2.5 * k;
            double x = cx - total * k / 2 + i * (2.5 + 2) * k;
            c.DrawRoundRect(new SKRect((float)x, (float)(cy - bh / 2), (float)(x + bw), (float)(cy + bh / 2)), (float)(bw / 2), (float)(bw / 2), p);
        }
    }

    /// A quaver (the stand-in when there's no artwork), filling a size-pt square centred on (cx, cy).
    public static void Note(SKCanvas c, double cx, double cy, double size, SKColor color)
    {
        double k = size / 24;
        c.Save();
        c.Translate((float)(cx - 12 * k), (float)(cy - 12 * k));
        c.Scale((float)k);
        using var p = new SKPaint { Color = color, IsAntialias = true };
        c.Save();
        c.RotateDegrees(-20, 8, 17.5f);
        c.DrawOval(new SKRect(4, 14.4f, 12, 20.6f), p);
        c.Restore();
        c.DrawRoundRect(new SKRect(10.4f, 3.6f, 12.4f, 17.6f), 1, 1, p);
        using var flag = new SKPath();
        flag.MoveTo(11.4f, 3.6f);
        flag.CubicTo(14, 4.6f, 18.6f, 6.4f, 18.6f, 10.8f);
        flag.CubicTo(17.4f, 9, 15, 8.4f, 12.4f, 8.6f);
        flag.Close();
        c.DrawPath(flag, p);
        c.Restore();
    }
}
