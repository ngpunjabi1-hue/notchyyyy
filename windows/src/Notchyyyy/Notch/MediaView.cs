using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Notchyyyy.Media;
using Notchyyyy.Motion;
using Notchyyyy.Theme;
using SkiaSharp;

namespace Notchyyyy.Notch;

/// <summary>
/// The Media tab (the Mac app's MusicView), an object rather than a form: artwork with a record that slides out
/// and spins while it plays, a background that drifts in the artwork's colours, a title that scrolls when it
/// doesn't fit, a waveform you scrub, and the transport controls. With nothing playing: a resting record and a
/// button to open a music app. Everything that moves is drawn on the render thread (<see cref="LiveCanvas"/>).
/// </summary>
public sealed class MediaView : Panel
{
    private readonly MediaPlayer _p;
    private readonly IClock _clock;
    private readonly BlurSwap _swap = new();
    private readonly Control _empty;
    private readonly Grid _player;
    private readonly LiveCanvas _aurora, _turntable;
    private readonly Marquee _title;
    private readonly TextBlock _artist;
    private readonly SourceChip _chip;
    private readonly WaveScrubber _scrubber;
    private readonly ControlButton _shuffle, _repeat, _prev, _next;
    private readonly PlayButton _play;
    private readonly TextBlock _emptyNote;
    private bool _ticking, _active = true;

    // turntable and aurora state, shared with the render thread (lock on _sync)
    private readonly object _sync = new();
    private readonly SpringValue _slide = new(0, 0.001), _lift = new(0, 0.001), _auroraIn = new(0, 0.002);
    private readonly TweenValue _artFade = new(1);
    private SKImage? _art, _oldArt;
    private Color[] _palette = Array.Empty<Color>(), _oldPalette = Array.Empty<Color>();
    private Color _tint;
    private bool _playing;
    private double _angle, _angleAt = double.NaN;

    public MediaView(NotchModel m, IClock clock)
    {
        _p = m.Media; _clock = clock;
        _tint = _p.Tint;

        // ---- the empty state ----
        var record = new LiveCanvas(clock, PaintRestingRecord) { Width = 92, Height = 92, Bleed = 16, HorizontalAlignment = HorizontalAlignment.Center };
        var note = new IconView(IconKind.Note, 12, Colors.White) { Width = 92, Height = 92, HorizontalAlignment = HorizontalAlignment.Center };
        _emptyNote = new TextBlock
        {
            FontFamily = LT.Ui, FontSize = LT.TextSize(11.5), Foreground = new SolidColorBrush(LT.Muted),
            TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
        };
        var open = new PillButton(clock, _p.OpenLabel, IconKind.Play, _p.Open) { Prominent = true, HorizontalAlignment = HorizontalAlignment.Center };
        _empty = new StackPanel
        {
            Spacing = 16, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                new Panel { Children = { record, note } },
                new StackPanel
                {
                    Spacing = 5,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Nothing playing", FontFamily = LT.Ui, FontSize = LT.TextSize(14), FontWeight = FontWeight.SemiBold,
                            Foreground = new SolidColorBrush(LT.Text), HorizontalAlignment = HorizontalAlignment.Center,
                        },
                        _emptyNote,
                    },
                },
                open,
            },
        };

        // ---- the player ----
        _aurora = new LiveCanvas(clock, PaintAurora) { Margin = new Thickness(-38, -46, -38, -24), Fps = 30, Animating = AuroraMoving };
        _turntable = new LiveCanvas(clock, PaintTurntable)
        {
            Width = 250, Height = 160, Bleed = 50, VerticalAlignment = VerticalAlignment.Center, Animating = TurntableMoving,
        };
        _chip = new SourceChip(clock, _p);
        _title = new Marquee(clock, 19, FontWeight.SemiBold) { Height = 25 * Prefs.Shared.TextScale };
        _artist = new TextBlock
        {
            FontFamily = LT.Ui, FontSize = LT.TextSize(13.5), Foreground = new SolidColorBrush(LT.Text.WithOpacity(0.62)),
            TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1,
        };
        _scrubber = new WaveScrubber(clock, _p);
        _shuffle = new ControlButton(clock, IconKind.Shuffle, 13, _p.ToggleShuffle) { Tip = "Shuffle" };
        _prev = new ControlButton(clock, IconKind.Previous, 19, _p.Previous) { Tip = "Previous" };
        _play = new PlayButton(clock, _p);
        _next = new ControlButton(clock, IconKind.Next, 19, _p.Next) { Tip = "Next" };
        _repeat = new ControlButton(clock, IconKind.Repeat, 13, _p.CycleRepeat) { Tip = "Repeat" };
        var controls = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,18,Auto,18,Auto,*,Auto") };
        void Col(Control c, int i) { Grid.SetColumn(c, i); controls.Children.Add(c); }
        Col(_shuffle, 0); Col(_prev, 2); Col(_play, 4); Col(_next, 6); Col(_repeat, 8);

        var column = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                _chip,
                new Control { Height = 8 },
                _title,
                _artist,
                new Control { Height = 12 },
                _scrubber,
                new Control { Height = 10 },
                controls,
            },
        };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("250,18,*"), Margin = new Thickness(6, 0) };
        Grid.SetColumn(_turntable, 0); Grid.SetColumn(column, 2);
        row.Children.Add(_turntable); row.Children.Add(column);
        _player = new Grid { Children = { _aurora, row } };

        Children.Add(_swap);
        _swap.Show(_p.HasTrack ? _player : _empty, clock.Now, animate: false);
        SnapAll();
    }

    /// The control the transport buttons and scrubber live in (for tests and the Windows self-test).
    public PlayButton Play => _play;
    public ControlButton NextButton => _next;
    public ControlButton PreviousButton => _prev;
    public WaveScrubber Scrubber => _scrubber;
    public Marquee TitleView => _title;
    public bool ShowingPlayer => ReferenceEquals(_swap.Current, _player);
    public LiveCanvas Turntable => _turntable;
    public LiveCanvas Aurora => _aurora;

    /// Off while the open notch is hidden: nothing is drawn.
    public bool Active
    {
        get => _active;
        set
        {
            if (_active == value) return;
            _active = value;
            foreach (var c in new[] { _aurora, _turntable }) c.Active = value;
            _title.Active = value; _scrubber.Active = value; _chip.Active = value;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _p.Changed += OnPlayer;
        SnapAll();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _p.Changed -= OnPlayer;
    }

    /// Shows the player's state as it is now, without animating (when the view appears).
    private void SnapAll()
    {
        double now = _clock.Now;
        lock (_sync)
        {
            _playing = _p.IsPlaying;
            _slide.Snap(_playing ? 1 : 0);
            _lift.Snap(_playing ? 1 : 0);
            _art = _p.ArtImage; _oldArt = null; _artFade.Snap(1);
            _palette = _p.Palette; _oldPalette = Array.Empty<Color>();
            _auroraIn.Snap(_palette.Length == 9 ? 1 : 0);
            _tint = _p.Tint;
            _angleAt = _playing ? now : double.NaN;
        }
        if (_p.HasTrack != ShowingPlayer) _swap.Show(_p.HasTrack ? _player : _empty, now, animate: false);
        _swap.Update(now);
        Fill();
        _play.Snap(_p.IsPlaying);
        Kick();
    }

    private void Fill()
    {
        _title.SetText(_p.Title, LT.Text);
        _artist.Text = _p.Artist.Length > 0 ? _p.Artist : _p.AppName;
        _emptyNote.Text = _p.Unavailable ?? "Play something in Spotify, Media Player,\nor YouTube in any browser";
        _shuffle.IsVisible = _p.CanShuffle; _shuffle.Active = _p.Shuffle; _shuffle.ActiveColor = _p.Tint;
        _repeat.IsVisible = _p.CanRepeat; _repeat.Active = _p.Repeat != RepeatMode.Off; _repeat.ActiveColor = _p.Tint;
        _repeat.Icon = _p.Repeat == RepeatMode.One ? IconKind.RepeatOne : IconKind.Repeat;
        _shuffle.InvalidateVisual(); _repeat.InvalidateVisual();
        _chip.Refresh();
        _scrubber.Refresh();
    }

    private void OnPlayer(string what)
    {
        double now = _clock.Now;
        switch (what)
        {
            case "Track":
                if (_p.HasTrack != ShowingPlayer) { _swap.Show(_p.HasTrack ? _player : _empty, now, animate: true); Tick(); }
                Fill();
                break;
            case "Playing":
                lock (_sync)
                {
                    if (_p.IsPlaying != _playing)
                    {
                        // the record keeps the angle it reached; it starts turning again from there
                        if (_playing && !double.IsNaN(_angleAt)) _angle += (now - _angleAt) * Spin;
                        _playing = _p.IsPlaying;
                        _angleAt = _playing ? now : double.NaN;
                        _slide.AnimateTo(_playing ? 1 : 0, SpringSpec.Response(0.7, 0.72), now);
                        _lift.AnimateTo(_playing ? 1 : 0, LT.Liquid, now);
                    }
                }
                _play.Set(_p.IsPlaying);
                _chip.Refresh();
                break;
            case "Art":
                lock (_sync)
                {
                    // the old artwork fades out under the new one (the Mac's .transition(.opacity).id(art))
                    _oldArt = _artFade.Value(now) > 0.5 ? _art : _oldArt;
                    _art = _p.ArtImage;
                    _artFade.Snap(0); _artFade.AnimateTo(1, 0.45, Ease.InOut, now);
                    _oldPalette = _palette;
                    _palette = _p.Palette;
                    if (_palette.Length == 9) _auroraIn.AnimateTo(1, LT.Liquid, now);
                    else _auroraIn.AnimateTo(0, LT.Liquid, now);
                    _tint = _p.Tint;
                }
                Fill();
                break;
            case "Modes":
                Fill();
                break;
            case "Position":
                _scrubber.Refresh();
                break;
        }
        Kick();
    }

    private void Kick() { _aurora.Kick(); _turntable.Kick(); }

    /// The empty ⇄ player cross-fade.
    private void Tick()
    {
        if (_ticking || TopLevel.GetTopLevel(this) is not { } top) return;
        _ticking = true;
        top.RequestAnimationFrame(_ =>
        {
            _ticking = false;
            double now = _clock.Now;
            _swap.Update(now);
            if (_swap.Animating(now)) Tick();
        });
    }

    // ---------------------------------------------------------------- render thread

    /// 33⅓ rpm, near enough (the Mac turns it once every 1.8 s).
    private const double Spin = 360 / 1.8;

    private bool TurntableMoving(double now)
    {
        lock (_sync) return _playing || !_slide.IsSettled(now) || !_lift.IsSettled(now) || !_artFade.IsSettled(now);
    }

    private bool AuroraMoving(double now)
    {
        lock (_sync) return (_playing && _palette.Length == 9) || !_auroraIn.IsSettled(now) || !_artFade.IsSettled(now);
    }

    /// Artwork in front, a vinyl record behind it. Playing, the record slides out and turns; paused, it tucks in.
    private void PaintTurntable(SKCanvas c, Size size, double now)
    {
        SKImage? art, old; double slide, lift, fade, angle; Color tint;
        lock (_sync)
        {
            art = _art; old = _oldArt; tint = _tint;
            slide = _slide.Value(now); lift = Math.Clamp(_lift.Value(now), 0, 1.2); fade = Math.Clamp(_artFade.Value(now), 0, 1);
            angle = _angle + (_playing && !double.IsNaN(_angleAt) ? (now - _angleAt) * Spin : 0);
        }
        double side = Math.Min(size.Height, size.Width * 0.75), cy = size.Height / 2;
        double rec = side * 0.9;
        double rx = side * 0.08 + (side * 0.62 - side * 0.08) * slide;
        DrawVinyl(c, (float)(rx + rec / 2), (float)cy, (float)(rec / 2), angle, art, fade < 1 ? old : null, fade);

        // the artwork: scaled to 0.94 from its leading edge while paused
        double s = 0.94 + 0.06 * lift;
        c.Save();
        c.Translate(0, (float)cy);
        c.Scale((float)s);
        c.Translate(0, (float)-cy);
        var r = new SKRect(0, (float)(cy - side / 2), (float)side, (float)(cy + side / 2));
        var rr = new SKRoundRect(r, 14, 14);
        using (var sh = Sk.Shadow(SKColors.Black.WithAlpha((byte)(0.45 * 255)), 14))
        {
            c.Save(); c.Translate(6, 8); c.DrawRoundRect(rr, sh); c.Restore();
        }
        if (lift > 0.01)
            using (var glow = Sk.Shadow(Sk.C(tint, 0.35 * Math.Min(1, lift)), 24))
                c.DrawRoundRect(rr, glow);
        c.Save();
        c.ClipRoundRect(rr, SKClipOperation.Intersect, true);
        using (var bg = new SKPaint { Color = Sk.C(LT.SurfaceHi), IsAntialias = true }) c.DrawRect(r, bg);
        // the old artwork stays fully drawn underneath while the new one fades in over it (both are opaque, so
        // fading one out while the other fades in let the grey behind show through mid-way)
        if (old is not null && fade < 1) Sk.Fill(c, old, r);
        if (art is not null) Sk.Fill(c, art, r, fade);
        else Sk.Note(c, r.MidX, r.MidY, 26 * 1.15, Sk.C(LT.Faint, old is null ? 1 : fade));
        c.Restore();
        using (var edge = new SKPaint { Color = SKColors.White.WithAlpha(26), IsStroke = true, StrokeWidth = 1, IsAntialias = true })
            c.DrawRoundRect(new SKRoundRect(SKRect.Inflate(r, -0.5f, -0.5f), 13.5f, 13.5f), edge);
        c.Restore();
    }

    private static readonly SKColor[] SheenColors =
        { SKColors.Transparent, SKColors.White.WithAlpha(23), SKColors.Transparent, SKColors.Transparent, SKColors.White.WithAlpha(15), SKColors.Transparent };

    private static void DrawVinyl(SKCanvas c, float cx, float cy, float rad, double angle, SKImage? label, SKImage? oldLabel = null, double fade = 1)
    {
        float k = rad / 72;   // the Mac's record is 144 pt across at this size
        using (var sh = Sk.Shadow(SKColors.Black.WithAlpha(128), 8)) c.DrawCircle(cx, cy + 4, rad, sh);
        using (var body = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateRadialGradient(new SKPoint(cx, cy), 90 * k,
                new[] { Sk.Grey(0.16), Sk.Grey(0.05), Sk.Grey(0.10) }, new[] { 2 / 90f, 0.511f, 1f }, SKShaderTileMode.Clamp),
        }) c.DrawCircle(cx, cy, rad, body);
        using (var groove = new SKPaint { IsAntialias = true, IsStroke = true, StrokeWidth = 0.6f * k })
            for (int i = 0; i < 7; i++)
            {
                groove.Color = SKColors.White.WithAlpha((byte)Math.Round((i % 2 == 0 ? 0.06 : 0.035) * 255));
                c.DrawCircle(cx, cy, rad - (6 + i * 7) * k - 0.3f * k, groove);
            }
        // the sheen stays put while the record turns under it
        using (var sheen = new SKPaint { IsAntialias = true, Shader = SKShader.CreateSweepGradient(new SKPoint(cx, cy), SheenColors, null) })
            c.DrawCircle(cx, cy, rad, sheen);
        // the label is the artwork, turning
        float lr = 22 * k;
        c.Save();
        c.RotateDegrees((float)(angle % 360), cx, cy);
        using (var path = new SKPath())
        {
            path.AddCircle(cx, cy, lr);
            c.ClipPath(path, SKClipOperation.Intersect, true);
        }
        var lrect = new SKRect(cx - lr, cy - lr, cx + lr, cy + lr);
        if (oldLabel is not null) Sk.Fill(c, oldLabel, lrect);
        else if (fade < 1 || label is null) using (var p = new SKPaint { Color = Sk.Grey(0.25), IsAntialias = true }) c.DrawCircle(cx, cy, lr, p);
        if (label is not null) Sk.Fill(c, label, lrect, fade);
        c.Restore();
        using (var dot = new SKPaint { Color = SKColors.Black, IsAntialias = true }) c.DrawCircle(cx, cy, 2.5f * k, dot);
    }

    /// The empty state's record: one record, centred and still, its label the accent with a note on it.
    private static void PaintRestingRecord(SKCanvas c, Size size, double now)
    {
        float cx = (float)size.Width / 2, cy = (float)size.Height / 2, rad = (float)size.Width / 2;
        using (var sh = Sk.Shadow(SKColors.Black.WithAlpha((byte)(0.45 * 255)), 10)) c.DrawCircle(cx, cy + 5, rad, sh);
        using (var body = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateRadialGradient(new SKPoint(cx, cy), 50,
                new[] { Sk.Grey(0.17), Sk.Grey(0.06), Sk.Grey(0.11) }, new[] { 2 / 50f, 0.52f, 1f }, SKShaderTileMode.Clamp),
        }) c.DrawCircle(cx, cy, rad, body);
        using (var groove = new SKPaint { IsAntialias = true, IsStroke = true, StrokeWidth = 0.6f })
            for (int i = 0; i < 5; i++)
            {
                groove.Color = SKColors.White.WithAlpha((byte)Math.Round((i % 2 == 0 ? 0.07 : 0.04) * 255));
                c.DrawCircle(cx, cy, rad - (7 + i * 6) - 0.3f, groove);
            }
        var sheenColors = new[] { SKColors.Transparent, SKColors.White.WithAlpha(20), SKColors.Transparent, SKColors.Transparent, SKColors.White.WithAlpha(13), SKColors.Transparent };
        using (var sheen = new SKPaint { IsAntialias = true, Shader = SKShader.CreateSweepGradient(new SKPoint(cx, cy), sheenColors, null) })
            c.DrawCircle(cx, cy, rad, sheen);
        using (var label = new SKPaint { Color = Sk.C(LT.AccentColor, 0.9), IsAntialias = true }) c.DrawCircle(cx, cy, 15, label);
    }

    /// The artwork's colours drifting slowly behind the player (the Mac's MeshGradient, 3 × 3 points), drawn as a
    /// fine mesh with smoothly blended colours, then darkened toward the controls and faded out toward the top.
    private void PaintAurora(SKCanvas c, Size size, double now)
    {
        Color[] pal, old; double fade, appear; bool playing; double t;
        lock (_sync)
        {
            pal = _palette; old = _oldPalette; fade = Math.Clamp(_artFade.Value(now), 0, 1);
            appear = Math.Clamp(_auroraIn.Value(now), 0, 1); playing = _playing;
            t = now;
        }
        if (appear < 0.002 || (pal.Length != 9 && old.Length != 9)) return;
        float w = (float)size.Width, h = (float)size.Height;
        // the drift freezes while paused (the Mac's timeline pauses); it never jumps
        double dt = playing ? t : _frozenAt ??= t;
        if (playing) _frozenAt = null;
        var colours = new SKColor[9];
        for (int i = 0; i < 9; i++)
        {
            Color a = old.Length == 9 ? old[i] : (pal.Length == 9 ? pal[i] : Colors.Black);
            Color b = pal.Length == 9 ? pal[i] : a;
            double f = old.Length == 9 ? fade : 1;
            colours[i] = new SKColor((byte)(a.R + (b.R - a.R) * f), (byte)(a.G + (b.G - a.G) * f), (byte)(a.B + (b.B - a.B) * f));
        }
        float W(double sa, double sb) => (float)(0.5 + 0.18 * Math.Sin(dt * sa + sb));
        var pts = new SKPoint[9]
        {
            new(0, 0), new(W(0.31, 0), 0), new(1, 0),
            new(0, W(0.27, 1)), new(W(0.23, 2), W(0.29, 3)), new(1, W(0.21, 4)),
            new(0, 1), new(W(0.25, 5), 1), new(1, 1),
        };
        const int N = 20;   // vertices per side of the fine mesh
        var pos = new SKPoint[(N + 1) * (N + 1)];
        var col = new SKColor[pos.Length];
        for (int j = 0; j <= N; j++)
        for (int i = 0; i <= N; i++)
        {
            double u = (double)i / N * 2, v = (double)j / N * 2;
            int ci = Math.Min(1, (int)u), cj = Math.Min(1, (int)v);
            double fu = u - ci, fv = v - cj;
            SKPoint P(int a, int b) => pts[(cj + b) * 3 + ci + a];
            SKColor Q(int a, int b) => colours[(cj + b) * 3 + ci + a];
            // positions: bilinear inside the patch; colours: smoothstep-weighted (SwiftUI's smoothsColors)
            double x = Bil(P(0, 0).X, P(1, 0).X, P(0, 1).X, P(1, 1).X, fu, fv);
            double y = Bil(P(0, 0).Y, P(1, 0).Y, P(0, 1).Y, P(1, 1).Y, fu, fv);
            double su = fu * fu * (3 - 2 * fu), sv = fv * fv * (3 - 2 * fv);
            col[j * (N + 1) + i] = new SKColor(
                (byte)Bil(Q(0, 0).Red, Q(1, 0).Red, Q(0, 1).Red, Q(1, 1).Red, su, sv),
                (byte)Bil(Q(0, 0).Green, Q(1, 0).Green, Q(0, 1).Green, Q(1, 1).Green, su, sv),
                (byte)Bil(Q(0, 0).Blue, Q(1, 0).Blue, Q(0, 1).Blue, Q(1, 1).Blue, su, sv));
            pos[j * (N + 1) + i] = new SKPoint((float)(x * w), (float)(y * h));
        }
        var idx = new ushort[N * N * 6];
        int n = 0;
        for (int j = 0; j < N; j++)
        for (int i = 0; i < N; i++)
        {
            ushort a = (ushort)(j * (N + 1) + i), b = (ushort)(a + 1), d = (ushort)(a + N + 1), e = (ushort)(d + 1);
            idx[n++] = a; idx[n++] = b; idx[n++] = d; idx[n++] = b; idx[n++] = e; idx[n++] = d;
        }
        var bounds = new SKRect(0, 0, w, h);
        c.SaveLayer(bounds, null);
        using (var layer = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Round(255 * (LT.Light ? 0.36 : 0.48) * appear)) })
        {
            c.SaveLayer(bounds, layer);
            using var verts = SKVertices.CreateCopy(SKVertexMode.Triangles, pos, null, col, idx);
            using var vp = new SKPaint { IsAntialias = false };
            c.DrawVertices(verts, SKBlendMode.Dst, vp);
            c.Restore();
        }
        // darker toward the controls on the right, so the text keeps its contrast
        var bg = LT.Bg;
        using (var over = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(w, 0),
                new[] { Sk.C(bg, 0.05 * appear), Sk.C(bg, 0.7 * appear) }, SKShaderTileMode.Clamp),
        }) c.DrawRect(bounds, over);
        // fade to the notch's own colour toward the top, so it meets the header cleanly
        using (var mask = new SKPaint
        {
            BlendMode = SKBlendMode.DstIn,
            Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, h),
                new[] { SKColors.Transparent, SKColors.Transparent, SKColors.Black }, new[] { 0f, 0.1f, 0.42f }, SKShaderTileMode.Clamp),
        }) c.DrawRect(bounds, mask);
        c.Restore();
    }

    private double? _frozenAt;

    private static double Bil(double a, double b, double c, double d, double u, double v) =>
        a * (1 - u) * (1 - v) + b * u * (1 - v) + c * (1 - u) * v + d * u * v;
}

/// <summary>
/// One line of text that scrolls sideways when it doesn't fit, pausing at the start of each lap, its edges fading
/// (the Mac's Marquee). When it fits it's an ordinary TextBlock; when it doesn't, the text is drawn once into an
/// image and that image scrolls on the render thread, so the motion is as even as the display.
/// </summary>
public sealed class Marquee : Panel
{
    private readonly IClock _clock;
    private readonly double _size;
    private readonly FontWeight _weight;
    private readonly TextBlock _plain;
    private readonly LiveCanvas _scroll;
    private readonly object _sync = new();
    private SKImage? _image;
    private double _textWidth, _start;
    private string _text = "";
    private Color _color;
    private bool _overflow;

    public const double Gap = 48, Speed = 32, Pause = 2.2;

    public Marquee(IClock clock, double size, FontWeight weight)
    {
        _clock = clock; _size = size; _weight = weight;
        ClipToBounds = true;
        _plain = new TextBlock { FontFamily = LT.Ui, FontSize = LT.TextSize(size), FontWeight = weight, VerticalAlignment = VerticalAlignment.Center };
        _scroll = new LiveCanvas(clock, Paint) { Animating = _ => _overflow, IsVisible = false };
        Children.Add(_plain);
        Children.Add(_scroll);
    }

    public bool Overflowing => _overflow;
    public double TextWidth => _textWidth;
    public bool Active { get => _scroll.Active; set => _scroll.Active = value; }
    public LiveCanvas Canvas => _scroll;

    public void SetText(string text, Color color)
    {
        if (text == _text && color == _color) return;
        _text = text; _color = color;
        _plain.Text = text; _plain.Foreground = new SolidColorBrush(color);
        lock (_sync) { _image = null; _start = _clock.Now; }
        InvalidateArrange();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        var ft = new FormattedText(_text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(LT.Ui, FontStyle.Normal, _weight), LT.TextSize(_size), Brushes.White);
        _textWidth = Math.Ceiling(ft.WidthIncludingTrailingWhitespace);
        bool overflow = _textWidth > finalSize.Width + 0.5 && finalSize.Width > 0;
        if (overflow && _image is null) Snapshot(finalSize.Height);
        _overflow = overflow;
        _plain.IsVisible = !overflow;
        _scroll.IsVisible = overflow;
        if (overflow) _scroll.Kick();
        return size;
    }

    /// Draws the text once, at the display's scale, for the render thread to scroll.
    private void Snapshot(double height)
    {
        double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var tb = new TextBlock
        {
            Text = _text, FontFamily = LT.Ui, FontSize = LT.TextSize(_size), FontWeight = _weight,
            Foreground = new SolidColorBrush(_color), VerticalAlignment = VerticalAlignment.Center, Height = height,
        };
        TextOptions.SetTextRenderingMode(tb, TextRenderingMode.Antialias);
        tb.Measure(new Size(_textWidth + 4, height));
        tb.Arrange(new Rect(0, 0, _textWidth + 4, height));
        var px = new PixelSize(Math.Max(1, (int)Math.Ceiling((_textWidth + 4) * scale)), Math.Max(1, (int)Math.Ceiling(height * scale)));
        using var rtb = new RenderTargetBitmap(px, new Vector(96 * scale, 96 * scale));
        rtb.Render(tb);
        using var ms = new MemoryStream();
        rtb.Save(ms, new PngBitmapEncoderOptions());
        var img = SKImage.FromEncodedData(ms.ToArray());
        lock (_sync) { _image = img; _start = _clock.Now; }
    }

    private void Paint(SKCanvas c, Size size, double now)
    {
        SKImage? img; double start, tw;
        lock (_sync) { img = _image; start = _start; tw = _textWidth; }
        if (img is null) return;
        float w = (float)size.Width, h = (float)size.Height;
        double lap = (tw + Gap) / Speed;
        double t = (now - start) % (lap + Pause);
        double x = -Math.Max(0, t - Pause) * Speed;
        var bounds = new SKRect(0, 0, w, h);
        c.SaveLayer(bounds, null);
        float iw = (float)(img.Width * (h / img.Height)), ih = h;
        using (var p = new SKPaint { IsAntialias = true })
        {
            var s = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);
            c.DrawImage(img, new SKRect((float)x, 0, (float)x + iw, ih), s, p);
            c.DrawImage(img, new SKRect((float)(x + tw + Gap), 0, (float)(x + tw + Gap) + iw, ih), s, p);
        }
        using (var mask = new SKPaint
        {
            BlendMode = SKBlendMode.DstIn,
            Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(w, 0),
                new[] { SKColors.Transparent, SKColors.Black, SKColors.Black, SKColors.Transparent }, new[] { 0f, 0.03f, 0.88f, 1f }, SKShaderTileMode.Clamp),
        }) c.DrawRect(bounds, mask);
        c.Restore();
    }
}

/// <summary>
/// "Playing from Spotify" with little bars beside it: opens the app. A capsule that brightens on hover.
/// </summary>
public sealed class SourceChip : Panel, Avalonia.Rendering.ICustomHitTest
{
    private readonly IClock _clock;
    private readonly MediaPlayer _p;
    private readonly Back _back;
    private readonly TextBlock _label;
    private readonly LiveCanvas _bars;
    private readonly SpringValue _hover = new(0, 0.002);
    private bool _ticking;

    public SourceChip(IClock clock, MediaPlayer p)
    {
        _clock = clock; _p = p;
        Height = 22; HorizontalAlignment = HorizontalAlignment.Left;
        Cursor = new Cursor(StandardCursorType.Hand);
        _back = new Back(this);
        _label = new TextBlock
        {
            FontFamily = LT.Ui, FontSize = LT.TextSize(10.5), FontWeight = FontWeight.Medium,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0),
        };
        _bars = new LiveCanvas(clock, (c, size, now) =>
        {
            Sk.MusicBars(c, size.Width / 2, size.Height / 2, 0.6, Sk.C(_tintForBars), now);
        }) { Width = 14, Height = 10, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), Fps = 30, Animating = _ => _barsOn };
        Children.Add(_back); Children.Add(_label); Children.Add(_bars);
        Refresh();
    }

    private volatile bool _barsOn;
    private Color _tintForBars;

    public bool HitTest(Point p) => new Rect(Bounds.Size).Contains(p);
    public bool Active { get => _bars.Active; set => _bars.Active = value; }
    public string Text => _label.Text ?? "";

    public void Refresh()
    {
        _label.Text = _p.IsPlaying ? $"Playing from {_p.AppName}" : $"Paused · {_p.AppName}";
        _tintForBars = _p.Tint;
        _barsOn = _p.IsPlaying;
        _bars.IsVisible = _p.IsPlaying;
        _label.Margin = new Thickness(8, 0, _p.IsPlaying ? 8 + 14 + 6 : 8, 0);
        ToolTip.SetTip(this, _p.AppName.Length > 0 ? $"Open {_p.AppName}" : null);
        Paint();
        _bars.Kick();
    }

    private void Paint()
    {
        double h = Math.Clamp(_hover.Value(_clock.Now), 0, 1);
        _label.Foreground = new SolidColorBrush(LT.Text.WithOpacity(0.55 + 0.35 * h));
        _back.InvalidateVisual();
    }

    protected override void OnPointerEntered(PointerEventArgs e) { base.OnPointerEntered(e); _hover.AnimateTo(1, LT.Hover, _clock.Now); Tick(); }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); _hover.AnimateTo(0, LT.Hover, _clock.Now); Tick(); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (new Rect(Bounds.Size).Contains(e.GetPosition(this))) _p.Open();
    }

    private void Tick()
    {
        Paint();
        if (_ticking || TopLevel.GetTopLevel(this) is not { } top) return;
        _ticking = true;
        top.RequestAnimationFrame(_ => { _ticking = false; Paint(); if (!_hover.IsSettled(_clock.Now)) Tick(); });
    }

    private sealed class Back : Control
    {
        private readonly SourceChip _o;
        public Back(SourceChip o) { _o = o; IsHitTestVisible = false; }
        public override void Render(DrawingContext ctx)
        {
            double h = Math.Clamp(_o._hover.Value(_o._clock.Now), 0, 1);
            ctx.DrawRectangle(new SolidColorBrush(LT.Wash.WithOpacity(0.06 + 0.04 * h)), null, new Rect(Bounds.Size), 11, 11);
        }
    }
}

/// <summary>
/// Progress as a waveform (the Mac's WaveScrubber): 54 bars in a shape of the song's own, the played ones bright,
/// the ones by the playhead breathing while it plays. Drag anywhere on it to seek; it grows a little on hover.
/// Times below: elapsed on the left, remaining on the right (LIVE for streams without a length).
/// </summary>
public sealed class WaveScrubber : StackPanel
{
    public const int Count = 54;
    private readonly IClock _clock;
    private readonly MediaPlayer _p;
    private readonly LiveCanvas _bars;
    private readonly TextBlock _elapsed, _left;
    private readonly Border _hit;
    private readonly object _sync = new();
    private readonly SpringValue _hover = new(0, 0.002);
    private double[] _shape = new double[Count];
    private double? _drag;
    private string _shapeFor = "\0";
    private DispatcherTimer? _timer;

    public WaveScrubber(IClock clock, MediaPlayer p)
    {
        _clock = clock; _p = p;
        Spacing = 6;
        _bars = new LiveCanvas(clock, Paint) { Height = 26, Fps = 30, Animating = Moving };
        _hit = new Border { Background = Brushes.Transparent, Height = 26, Cursor = new Cursor(StandardCursorType.Hand), Child = _bars };
        _hit.PointerEntered += (_, _) => { lock (_sync) _hover.AnimateTo(1, LT.Hover, _clock.Now); _bars.Kick(); };
        _hit.PointerExited += (_, _) => { lock (_sync) _hover.AnimateTo(0, LT.Hover, _clock.Now); _bars.Kick(); };
        _hit.PointerPressed += (_, e) =>
        {
            if (!_p.CanSeek) return;
            e.Pointer.Capture(_hit);
            DragTo(e.GetPosition(_hit).X);
            e.Handled = true;
        };
        _hit.PointerMoved += (_, e) => { if (_drag is not null) DragTo(e.GetPosition(_hit).X); };
        _hit.PointerReleased += (_, e) =>
        {
            double? d; lock (_sync) { d = _drag; _drag = null; }
            if (d is { } seconds) _p.Seek(seconds);
            e.Pointer.Capture(null);
            _bars.Kick();
            UpdateTimes();
        };
        _hit.PointerCaptureLost += (_, _) => { lock (_sync) _drag = null; _bars.Kick(); };
        _elapsed = TimeLabel(HorizontalAlignment.Left);
        _left = TimeLabel(HorizontalAlignment.Right);
        Children.Add(_hit);
        Children.Add(new Grid { Children = { _elapsed, _left } });
    }

    public bool Active { get => _bars.Active; set => _bars.Active = value; }
    public LiveCanvas Bars => _bars;
    public double? Dragging { get { lock (_sync) return _drag; } }
    public string ElapsedText => _elapsed.Text ?? "";
    public string RemainingText => _left.Text ?? "";
    /// The bars' heights (0…1 of the row) for the current song.
    public IReadOnlyList<double> Shape { get { lock (_sync) return _shape; } }

    private static TextBlock TimeLabel(HorizontalAlignment a) => new()
    {
        FontFamily = LT.Mono, FontSize = LT.TextSize(10), FontWeight = FontWeight.Medium,
        Foreground = new SolidColorBrush(LT.Text.WithOpacity(0.45)), HorizontalAlignment = a,
    };

    private void DragTo(double x)
    {
        double w = Math.Max(1, _hit.Bounds.Width);
        lock (_sync) _drag = Math.Clamp(x / w, 0, 1) * _p.Duration;
        _bars.Kick();
        UpdateTimes();
    }

    public void Refresh()
    {
        if (_shapeFor != _p.Title)
        {
            _shapeFor = _p.Title;
            var s = ShapeFor(_p.Title);
            lock (_sync) _shape = s;
        }
        UpdateTimes();
        _bars.Kick();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(0.25), DispatcherPriority.Background, (_, _) => UpdateTimes());
        _timer.Start();
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer?.Stop(); _timer = null;
    }

    private void UpdateTimes()
    {
        double pos = Dragging ?? _p.LivePosition(_clock.Now);
        if (_p.Duration > 0)
        {
            _elapsed.Text = Time(pos);
            _left.Text = "-" + Time(Math.Max(0, _p.Duration - pos));
        }
        else { _elapsed.Text = ""; _left.Text = _p.HasTrack ? "LIVE" : ""; }
    }

    public static string Time(double s)
    {
        int t = (int)Math.Round(s);
        return t >= 3600 ? $"{t / 3600}:{t / 60 % 60:00}:{t % 60:00}" : $"{t / 60}:{t % 60:00}";
    }

    /// A fixed, song-specific silhouette (the Mac's shape, seeded from a stable hash of the title).
    public static double[] ShapeFor(string title)
    {
        ulong seed = 14695981039346656037UL;
        foreach (char ch in title) { seed ^= ch; seed *= 1099511628211UL; }
        seed |= 1;
        double prev = 0.5;
        var r = new double[Count];
        for (int i = 0; i < Count; i++)
        {
            seed = seed * 6364136223846793005UL + 1442695040888963407UL;
            double u = (seed >> 33) / (double)(1UL << 31);
            double envelope = 0.55 + 0.45 * Math.Sin((double)i / Count * Math.PI);
            prev = prev * 0.6 + u * 0.4;
            r[i] = Math.Max(0.32, Math.Min(1, (0.25 + prev) * envelope * 1.1));
        }
        return r;
    }

    private bool Moving(double now)
    {
        lock (_sync) return _p.IsPlaying || _drag is not null || !_hover.IsSettled(now);
    }

    private void Paint(SKCanvas c, Size size, double now)
    {
        double[] shape; double? drag; double hover;
        lock (_sync) { shape = _shape; drag = _drag; hover = Math.Clamp(_hover.Value(now), 0, 1); }
        bool playing = _p.IsPlaying;
        double duration = _p.Duration;
        double pos = drag ?? _p.LivePosition(now);
        double frac = duration > 0 ? Math.Clamp(pos / duration, 0, 1) : 1;
        double gap = 2.2, w = (size.Width - gap * (Count - 1)) / Count, head = frac * Count;
        double grow = drag is not null ? 1 : 0.8 + 0.2 * hover;
        using var played = new SKPaint { IsAntialias = true, Color = drag is not null ? Sk.C(_p.Tint) : Sk.C(LT.Text, 0.92) };
        using var rest = new SKPaint { IsAntialias = true, Color = Sk.C(LT.Text, 0.18) };
        for (int i = 0; i < Count; i++)
        {
            double near = Math.Max(0, 1 - Math.Abs(i - head) / 4);
            double wobble = playing ? 0.18 * near * Math.Sin(now * 9 + i) : 0;
            double bh = size.Height * Math.Min(1, shape[i] + wobble) * grow;
            float x = (float)(i * (w + gap)), y = (float)((size.Height - bh) / 2);
            c.DrawRoundRect(new SKRect(x, y, x + (float)w, y + (float)bh), (float)(w / 2), (float)(w / 2), i < head ? played : rest);
        }
    }
}

/// <summary>A transport button: an icon in a circle that washes in on hover, and bounces when pressed.</summary>
public sealed class ControlButton : SpringControl
{
    private readonly double _size;
    private readonly Action _action;
    private readonly SpringValue _hover, _bounce;
    public IconKind Icon { get; set; }
    public bool Active { get; set; }
    public Color ActiveColor { get; set; } = LT.AccentColor;
    public string Tip { set => ToolTip.SetTip(this, value); }
    public int Presses { get; private set; }

    public ControlButton(IClock clock, IconKind icon, double size, Action action) : base(clock)
    {
        Icon = icon; _size = size; _action = action;
        _hover = Spring(0); _bounce = Spring(1);
        double f = (size + 18) * Prefs.Shared.IconScale;
        Width = f; Height = f;
        VerticalAlignment = VerticalAlignment.Center;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public override bool HitTest(Point p) => Vector.Distance(p, new Point(Bounds.Width / 2, Bounds.Height / 2)) <= Bounds.Width / 2;
    protected override void OnPointerEntered(PointerEventArgs e) { base.OnPointerEntered(e); Go(_hover, 1, LT.Hover); }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); Go(_hover, 0, LT.Hover); }
    protected override void OnPointerPressed(PointerPressedEventArgs e) { base.OnPointerPressed(e); e.Handled = true; }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (HitTest(e.GetPosition(this))) Press();
    }

    public void Press()
    {
        Presses++;
        _bounce.Snap(0.8);
        Go(_bounce, 1, new SpringSpec(0.42, 0.45));
        _action();
    }

    public override void Render(DrawingContext ctx)
    {
        double h = V(_hover.Value(Now)), s = _bounce.Value(Now);
        var c = new Point(Bounds.Width / 2, Bounds.Height / 2);
        if (h > 0.001) ctx.DrawEllipse(new SolidColorBrush(LT.Wash.WithOpacity(0.08 * h)), null, c, Bounds.Width / 2, Bounds.Height / 2);
        var color = Active ? ActiveColor : Mix(LT.Text.WithOpacity(0.7), LT.Text, h);
        Icons.Draw(ctx, Icon, c, _size * 1.05 * Prefs.Shared.IconScale * s, color);
    }
}

/// <summary>
/// The big round play / pause button: glass, a little larger on hover, and the icon swaps down-and-up
/// (the Mac's .contentTransition(.symbolEffect(.replace.downUp))) with a bounce on each press.
/// </summary>
public sealed class PlayButton : SpringControl
{
    private readonly MediaPlayer _p;
    private readonly SpringValue _hover, _swap, _bounce;
    private bool _shows;   // true = shows "pause" (it's playing)
    private bool _from;

    public PlayButton(IClock clock, MediaPlayer p) : base(clock)
    {
        _p = p;
        _hover = Spring(0); _swap = Spring(1); _bounce = Spring(1);
        double f = 50 * Prefs.Shared.IconScale;
        Width = f; Height = f;
        VerticalAlignment = VerticalAlignment.Center;
        Cursor = new Cursor(StandardCursorType.Hand);
        ToolTip.SetTip(this, "Play or pause");
    }

    public bool ShowsPause => _shows;
    public int Presses { get; private set; }

    public void Snap(bool playing) { _shows = playing; _from = playing; _swap.Snap(1); InvalidateVisual(); }
    public void Set(bool playing)
    {
        if (playing == _shows) return;
        _from = _shows; _shows = playing;
        _swap.Snap(0); Go(_swap, 1, LT.Snappy);
    }

    public override bool HitTest(Point p) => Vector.Distance(p, new Point(Bounds.Width / 2, Bounds.Height / 2)) <= Bounds.Width / 2;
    protected override void OnPointerEntered(PointerEventArgs e) { base.OnPointerEntered(e); Go(_hover, 1, LT.Hover); }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); Go(_hover, 0, LT.Hover); }
    protected override void OnPointerPressed(PointerPressedEventArgs e) { base.OnPointerPressed(e); e.Handled = true; }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (HitTest(e.GetPosition(this))) Press();
    }

    public void Press()
    {
        Presses++;
        _bounce.Snap(0.86);
        Go(_bounce, 1, new SpringSpec(0.42, 0.4));
        _p.PlayPause();
        Set(_p.IsPlaying);
    }

    public override void Render(DrawingContext ctx)
    {
        double h = V(_hover.Value(Now)), sw = V(_swap.Value(Now));
        double s = (1 + 0.06 * h) * _bounce.Value(Now);
        double r = Bounds.Width / 2 * (1 + 0.06 * h);
        var c = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var circle = new EllipseGeometry(new Rect(c.X - r, c.Y - r, 2 * r, 2 * r));
        TabPill.Glass(ctx, circle, circle.Bounds);
        double icon = 21 * 1.05 * Prefs.Shared.IconScale * s;
        // the icon leaving drops and fades; the new one comes down from above
        if (sw < 1)
        {
            using (ctx.PushOpacity(1 - sw))
                Icons.Draw(ctx, _from ? IconKind.Pause : IconKind.Play, new Point(c.X + (_from ? 0 : 1.5), c.Y + 7 * sw), icon * (1 - 0.3 * sw), LT.Text);
        }
        using (ctx.PushOpacity(sw))
            Icons.Draw(ctx, _shows ? IconKind.Pause : IconKind.Play, new Point(c.X + (_shows ? 0 : 1.5), c.Y - 7 * (1 - sw)), icon * (0.7 + 0.3 * sw), LT.Text);
    }
}
