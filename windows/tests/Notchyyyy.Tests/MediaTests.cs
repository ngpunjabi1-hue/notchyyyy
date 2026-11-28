using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Notchyyyy.Media;
using Notchyyyy.Motion;
using Notchyyyy.Notch;
using Notchyyyy.Theme;
using SkiaSharp;
using Xunit;

namespace Notchyyyy.Tests;

/// Step 6: the Media tab, the closed pill's music activity and Home's media row, with the demo source.
public class MediaTests
{
    static string Out(string name)
    {
        var d = Path.Combine(Environment.GetEnvironmentVariable("NOTCHYYYY_FRAMES") ?? Path.GetTempPath(), name);
        Directory.CreateDirectory(d);
        return d;
    }

    static readonly Color Backdrop = Color.FromRgb(140, 160, 190);

    internal sealed class Rig : IDisposable
    {
        public readonly NotchModel M = new();
        public readonly ManualClock Clock = new() { Now = 1 };
        public readonly NotchStage Stage;
        public readonly Window Win;
        public readonly IMediaSource Source;
        public Rig(bool light = false, bool playing = true, IMediaSource? source = null, Tab tab = Tab.Media, bool open = true, double scale = 2)
        {
            Prefs.Shared.Appearance = light ? Appearance.Light : Appearance.Dark;
            M.Buddy.Hour = () => 14;
            Stage = new NotchStage(M, Clock) { Width = 820, Height = 430 };
            Win = new Window { Width = 820, Height = 430, Content = new Grid { Background = new SolidColorBrush(Backdrop), Children = { Stage } } };
            Win.Show();
            if (scale != 1) Win.SetRenderScaling(scale);
            Source = source ?? new DemoMediaSource(playing);
            M.Media.Start(Source);
            WaitForArt();
            M.Tab = tab;
            if (open) { Clock.Now = 1.1; M.Expanded = true; }
        }

        /// The demo artwork is decoded off the UI thread: wait for it to arrive.
        public void WaitForArt()
        {
            for (int i = 0; i < 400; i++)
            {
                Dispatcher.UIThread.RunJobs();
                if (!M.Media.HasTrack || M.Media.ArtImage is not null) { Dispatcher.UIThread.RunJobs(); return; }
                Thread.Sleep(5);
            }
            throw new TimeoutException("artwork never arrived");
        }

        public WriteableBitmap Run(double until)
        {
            WriteableBitmap? b = null;
            for (double t = Clock.Now; t <= until + 1e-9; t += 1 / 30.0)
            {
                Clock.Now = t; Stage.Update(t);
                b = Frame();
            }
            return b!;
        }

        /// One frame at the test clock's time. Every live canvas is asked to draw it: their own pacing goes by real time
        /// (at most once per display frame), and test frames can come faster than that.
        public WriteableBitmap Frame()
        {
            foreach (var c in Win.GetVisualDescendants().OfType<LiveCanvas>()) c.Kick();
            Dispatcher.UIThread.RunJobs();
            return Win.CaptureRenderedFrame()!;
        }
        public MediaView View => Stage.Body.CurrentMedia!;
        public Point Centre(Control c) => c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), Win)!.Value;
        public void Dispose() { Win.Close(); Prefs.Shared.Appearance = Appearance.Dark; Prefs.Shared.Buddy = true; }
    }

    internal sealed class Px
    {
        readonly byte[] _p; readonly int _stride; public readonly int W, H; readonly double _s;
        public Px(WriteableBitmap b, double scale)
        {
            using var fb = b.Lock();
            _stride = fb.RowBytes; W = fb.Size.Width; H = fb.Size.Height; _s = scale;
            _p = new byte[_stride * H];
            System.Runtime.InteropServices.Marshal.Copy(fb.Address, _p, 0, _p.Length);
            if (fb.Format == Avalonia.Platform.PixelFormat.Bgra8888)
                for (int i = 0; i + 3 < _p.Length; i += 4) (_p[i], _p[i + 2]) = (_p[i + 2], _p[i]);
        }
        /// The pixel at (x, y) in points.
        public Color At(double x, double y)
        {
            int px = Math.Clamp((int)(x * _s), 0, W - 1), py = Math.Clamp((int)(y * _s), 0, H - 1);
            int i = py * _stride + px * 4;
            return Color.FromRgb(_p[i], _p[i + 1], _p[i + 2]);
        }
        public static int Dist(Color a, Color b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
    }

    // the body is 700 × 300, centred at the top of the 820-pt stage; its content sits 38 in, 46 down
    const double BodyX = 60, ContentX = 60 + 38, ContentY = 46;

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public void PlayerLooksRight(bool light)
    {
        using var rig = new Rig(light);
        var bmp = rig.Run(2.4);
        bmp.Save(Path.Combine(Out("media"), $"player-{(light ? "light" : "dark")}.png"), new PngBitmapEncoderOptions());
        Assert.True(rig.View.ShowingPlayer, "the player shows while a track is loaded");
        var px = new Px(bmp, 2);
        // the artwork's own gradient fills the art square (left of the turntable: x 6…166 in the content)
        var art = rig.View.Turntable.TranslatePoint(new Point(20, 80), rig.Win)!.Value;
        var c = px.At(art.X, art.Y);
        Assert.True(c.R > 150 && c.B > 60 && c.G < 140, $"artwork not drawn at the left of the turntable: {c}");
        // playing: the record has slid out to the right of the artwork (its dark body shows beyond the art's edge)
        var rec = rig.View.Turntable.TranslatePoint(new Point(160 + 50, 80 - 50), rig.Win)!.Value;
        var r = px.At(rec.X, rec.Y);
        Assert.True(r.R < 90 && Math.Abs(r.R - r.G) < 25, $"record not out beside the artwork: {r}");
    }

    [AvaloniaFact]
    public void EmptyStateShowsARestingRecordAndAButton()
    {
        using var rig = new Rig(source: new NoMediaSource());
        var bmp = rig.Run(2.4);
        bmp.Save(Path.Combine(Out("media"), "empty-dark.png"), new PngBitmapEncoderOptions());
        Assert.False(rig.View.ShowingPlayer);
        var texts = rig.View.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
        Assert.Contains("Nothing playing", texts);
        // the record sits centred, its accent label in the middle
        var label = new Px(bmp, 2).At(BodyX + 350, ContentY + 115 - 16 - 30);
        Assert.True(Px.Dist(label, LT.AccentColor) < 90, $"accent label not at the record's centre: {label}");
    }

    /// Pausing: the button swaps to "play", the record tucks back behind the artwork, the art shrinks a little.
    [AvaloniaFact]
    public void PausingTucksTheRecordIn()
    {
        using var rig = new Rig();
        rig.Run(2.4);
        Assert.True(rig.View.Play.ShowsPause);
        var probe = rig.View.Turntable.TranslatePoint(new Point(160 + 50, 80 - 50), rig.Win)!.Value;
        var before = new Px(rig.Frame(), 2).At(probe.X, probe.Y);
        rig.View.Play.Press();
        Assert.False(rig.M.Media.IsPlaying);
        Assert.False(rig.View.Play.ShowsPause);
        var frames = new List<Color>();
        for (int i = 1; i <= 45; i++)
        {
            rig.Clock.Now += 1 / 30.0;
            frames.Add(new Px(rig.Frame(), 2).At(probe.X, probe.Y));
        }
        var after = frames[^1];
        Assert.True(Px.Dist(before, after) > 30, $"nothing moved at the record's edge ({before} → {after})");
        // it settles: the last few frames are the same
        Assert.True(frames.Skip(frames.Count - 4).All(f => Px.Dist(f, after) <= 3), "the record keeps moving after it should have settled");
        // and the demo source really was told to pause
        Assert.False(rig.M.MusicPlaying);
    }

    /// The record turns while playing: its label (the artwork) rotates, the rest of the record looks still.
    [AvaloniaFact]
    public void RecordTurnsWhilePlaying()
    {
        using var rig = new Rig();
        rig.Run(2.4);
        int f0 = rig.View.Turntable.Frames;
        // the label is 22 pt in radius at the record's centre (99.2 + 72 from the turntable's left, mid-height)
        var centre = rig.View.Turntable.TranslatePoint(new Point(0.62 * 160 + 72, 80), rig.Win)!.Value;
        var a = new Px(rig.Frame(), 2);
        rig.Clock.Now += 0.45;   // a quarter turn
        var b = new Px(rig.Frame(), 2);
        int changed = 0;
        for (int dx = -16; dx <= 16; dx += 4)
        for (int dy = -16; dy <= 16; dy += 4)
            if (Px.Dist(a.At(centre.X + dx, centre.Y + dy), b.At(centre.X + dx, centre.Y + dy)) > 12) changed++;
        Assert.True(changed > 6, $"the label didn't turn ({changed} probes changed)");
        Assert.True(rig.View.Turntable.Frames > f0, "the turntable wasn't redrawn while playing");
    }

    [AvaloniaFact]
    public void LongTitleScrollsAndShortTitleDoesNot()
    {
        using var rig = new Rig();
        rig.Run(2.4);
        var m = rig.View.TitleView;
        Assert.True(m.Overflowing, $"the demo's long title should scroll ({m.TextWidth:0} pt in {m.Bounds.Width:0} pt)");
        // still for the first 2.2 s of each lap, then it moves left at 32 pt/s
        var at = m.TranslatePoint(new Point(4, m.Bounds.Height / 2), rig.Win)!.Value;
        var row = Enumerable.Range(0, 120).Select(i => new Point(at.X + i, at.Y)).ToList();
        string Sig(Px p) => string.Join(",", row.Select(q => p.At(q.X, q.Y).R / 32));
        var s0 = Sig(new Px(rig.Frame(), 2));
        rig.Clock.Now += 0.5;
        var s1 = Sig(new Px(rig.Frame(), 2));
        Assert.Equal(s0, s1);   // within the pause
        rig.Clock.Now += 3.0;
        var s2 = Sig(new Px(rig.Frame(), 2));
        Assert.NotEqual(s0, s2);   // scrolling now

        // the next demo track's title fits: no scrolling, an ordinary text line
        rig.View.NextButton.Press();
        rig.WaitForArt();
        rig.Run(rig.Clock.Now + 0.5);
        Assert.Equal("Weightless", rig.M.Media.Title);
        Assert.False(m.Overflowing);
        // a new track starts from its own position (the demo's next track starts at 0:00)
        Assert.InRange(rig.M.Media.LivePosition(rig.Clock.Now), 0, 1);
    }

    /// Dragging across the waveform seeks to where you let go; the times follow the drag.
    [AvaloniaFact]
    public void DraggingTheWaveformSeeks()
    {
        using var rig = new Rig();
        rig.Run(2.4);
        var s = rig.View.Scrubber;
        var bars = s.Bars;
        var left = bars.TranslatePoint(new Point(0, 13), rig.Win)!.Value;
        double w = bars.Bounds.Width;
        rig.Win.MouseMove(new Point(left.X + w * 0.2, left.Y), RawInputModifiers.None);
        rig.Win.MouseDown(new Point(left.X + w * 0.2, left.Y), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(s.Dragging);
        rig.Win.MouseMove(new Point(left.X + w * 0.5, left.Y), RawInputModifiers.LeftMouseButton);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(WaveScrubber.Time(2017 * 0.5), s.ElapsedText);
        rig.Win.MouseUp(new Point(left.X + w * 0.5, left.Y), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(s.Dragging);
        Assert.InRange(rig.M.Media.LivePosition(rig.Clock.Now), 2017 * 0.5 - 2, 2017 * 0.5 + 2);
        Assert.Equal("-" + WaveScrubber.Time(2017 - 2017 * 0.5), s.RemainingText);
    }

    /// Each song has its own waveform shape, the same every time (not .NET's per-run string hash).
    [Fact]
    public void WaveShapeIsStablePerSong()
    {
        var a = WaveScrubber.ShapeFor("Weightless");
        Assert.Equal(a, WaveScrubber.ShapeFor("Weightless"));
        Assert.NotEqual(a, WaveScrubber.ShapeFor("Sunflower"));
        Assert.All(a, v => Assert.InRange(v, 0.32, 1));
        Assert.Equal(WaveScrubber.Count, a.Length);
    }

    /// The artwork's colours: an even 3 × 3 sample of it, lifted to glow on black.
    [Fact]
    public void PaletteKeepsTheArtworksLayout()
    {
        using var b = new SKBitmap(90, 90);
        using (var c = new SKCanvas(b))
        {
            c.Clear(new SKColor(20, 40, 200));
            using var red = new SKPaint { Color = new SKColor(220, 30, 30) };
            c.DrawRect(0, 0, 30, 30, red);   // top-left cell red, the rest blue
        }
        var (tint, pal) = MediaPlayer.Colours(b);
        Assert.Equal(9, pal.Length);
        Assert.True(pal[0].R > 200 && pal[0].B < 80, $"top-left should be red: {pal[0]}");
        Assert.True(pal[8].B > 200 && pal[8].R < 80, $"bottom-right should be blue: {pal[8]}");
        Assert.True(Math.Max(tint.R, Math.Max(tint.G, tint.B)) >= 190, $"tint is lifted to read on black: {tint}");
    }

    [AvaloniaFact]
    public void ControlsDriveTheSource()
    {
        using var rig = new Rig();
        rig.Run(2.4);
        Assert.Equal("BLOX", rig.M.Media.Artist);
        rig.View.NextButton.Press();
        rig.WaitForArt();
        Assert.Equal("Weightless", rig.M.Media.Title);
        rig.View.PreviousButton.Press();   // within 3 s of the start: back to the previous track
        rig.WaitForArt();
        Assert.Equal("BLOX", rig.M.Media.Artist);
        // real clicks reach the buttons (hit-testing works on the custom-drawn controls)
        var next = rig.Centre(rig.View.NextButton);
        rig.Win.MouseMove(next, RawInputModifiers.None);
        rig.Win.MouseDown(next, MouseButton.Left);
        rig.Win.MouseUp(next, MouseButton.Left);
        rig.WaitForArt();
        Assert.Equal("Weightless", rig.M.Media.Title);
        var play = rig.Centre(rig.View.Play);
        rig.Win.MouseDown(play, MouseButton.Left);
        rig.Win.MouseUp(play, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.False(rig.M.Media.IsPlaying);
    }

    /// The closed pill: the artwork on the left (with Sprout off) and bars in the artwork's colour on the right.
    [AvaloniaFact]
    public void ClosedPillShowsArtworkAndBars()
    {
        Prefs.Shared.Buddy = false;
        using var rig = new Rig(open: false);
        rig.Run(2.0);
        var bmp = rig.Frame();
        bmp.Save(Path.Combine(Out("media"), "pill-art.png"), new PngBitmapEncoderOptions());
        var px = new Px(bmp, 2);
        var size = rig.M.ClosedSize;
        double left = 410 - size.Width / 2;
        // the art square: 22 pt, 19 pt in from the pill's left end
        var art = px.At(left + 19 + 11, size.Height / 2);
        Assert.True(art.R > 150 && art.G < 150, $"pill artwork missing: {art}");
        // bars on the right, in the artwork's tint
        double right = 410 + size.Width / 2 - 18 - 12;
        int tinted = 0;
        for (double dx = -6; dx <= 6; dx += 0.5)
        for (double dy = -4; dy <= 4; dy += 1)
            if (Px.Dist(px.At(right + dx, size.Height / 2 + dy), rig.M.Media.Tint) < 60) tinted++;
        Assert.True(tinted > 4, $"no bars in the artwork's colour ({tinted})");
    }

    /// Home's media row names the track and shows the bars; with nothing playing it says so.
    [AvaloniaFact]
    public void HomeRowFollowsTheTrack()
    {
        using var rig = new Rig(tab: Tab.Home);
        rig.Run(2.4);
        var home = rig.Stage.Body.CurrentHome!;
        Assert.Equal(rig.M.Media.Title, home.MediaRow.Title);
        Assert.Equal("BLOX · Google Chrome", home.MediaRow.Detail);
        Assert.True(home.MediaBars.IsVisible);
        rig.Frame().Save(Path.Combine(Out("media"), "home-playing.png"), new PngBitmapEncoderOptions());
        rig.M.Media.PlayPause();
        Dispatcher.UIThread.RunJobs();
        Assert.False(home.MediaBars.IsVisible);
    }

    /// Nothing the player draws leaks outside the open notch (the colour field is clipped by the notch's outline).
    [AvaloniaFact]
    public void NothingLeaksOutsideTheNotch()
    {
        using var rig = new Rig();
        var bmp = rig.Run(2.6);
        var px = new Px(bmp, 2);
        // just outside the body's left and right edges, and just below its bottom (between the body and the tab bar)
        foreach (var (x, y) in new[] { (BodyX - 3, 150.0), (BodyX + 700 + 3, 150.0), (BodyX + 120, 300 + 4.0), (BodyX + 600, 300 + 4.0) })
            Assert.True(Px.Dist(px.At(x, y), Backdrop) <= 6, $"something drawn outside the notch at ({x}, {y}): {px.At(x, y)}");
    }

    /// Frame by frame at 120 Hz: arriving on Media from Home, pausing, and changing track. Each moves without a pop:
    /// no frame changes much more of the picture than the frames either side of it (a cross-fade spreads its change
    /// over many frames; a ghost vanishing or an effect switching off lands in one).
    [AvaloniaTheory]
    [InlineData("switch")] [InlineData("pause")] [InlineData("next")]
    public void MotionHasNoPops(string what) => Assert.Empty(Spikes(what));

    /// The check above can see a pop: with the Mac's blur put back, the view snapping sharp as the blur comes off is
    /// found (at 2x, Avalonia's blur never fades to sharp; see BlurSwap).
    [AvaloniaFact]
    public void PopCheckCatchesTheBlurSnap()
    {
        BlurSwap.NoBlur = false;
        try { Assert.NotEmpty(Spikes("switch", "blur")); }
        finally { BlurSwap.NoBlur = true; }
    }

    /// Changing track: the artwork cross-fades without going grey half-way (the old one stays drawn under the new).
    [AvaloniaFact]
    public void ArtworkCrossFadeNeverGoesGrey()
    {
        using var rig = new Rig();
        rig.Run(2.4);
        var probe = rig.View.Turntable.TranslatePoint(new Point(80, 80), rig.Win)!.Value;
        var from = new Px(rig.Frame(), 2).At(probe.X, probe.Y);
        rig.View.NextButton.Press();
        rig.WaitForArt();
        double minSat = 1;
        Color last = from;
        for (int i = 0; i < 40; i++)
        {
            rig.Clock.Now += 1 / 60.0;
            last = new Px(rig.Frame(), 2).At(probe.X, probe.Y);
            double max = Math.Max(last.R, Math.Max(last.G, last.B)), min = Math.Min(last.R, Math.Min(last.G, last.B));
            minSat = Math.Min(minSat, max == 0 ? 0 : (max - min) / max);
        }
        Assert.True(last.B > last.R, $"didn't end on the blue artwork: {last}");
        Assert.True(minSat > 0.3, $"the artwork washed out mid-fade (saturation down to {minSat:0.00})");
    }

    static List<(int Frame, double Share)> Spikes(string what, string tag = "")
    {
        using var rig = new Rig(tab: what == "switch" ? Tab.Home : Tab.Media);
        rig.Run(2.4);
        var dir = Out("media-motion-" + what + tag);
        switch (what)
        {
            case "switch": rig.M.Tab = Tab.Media; break;
            case "pause": rig.View.Play.Press(); break;
            case "next": rig.View.NextButton.Press(); rig.WaitForArt(); break;
        }
        Px? prev = null;
        var jumps = new List<double>();
        for (int i = 1; i <= 96; i++)
        {
            rig.Clock.Now += 1 / 120.0;
            rig.Stage.Update(rig.Clock.Now);
            var bmp = rig.Frame();
            if (i % 6 == 0 || Environment.GetEnvironmentVariable("NOTCHYYYY_ALLFRAMES") is not null)
                bmp.Save(Path.Combine(dir, $"f{i:D3}.png"), new PngBitmapEncoderOptions());
            var px = new Px(bmp, 2);
            if (prev is not null)
            {
                // share of the open notch (sampled every 4 pt) that changed visibly since the last frame
                int changed = 0, n = 0;
                for (double y = 4; y < 296; y += 4)
                for (double x = 64; x < 756; x += 4)
                {
                    n++;
                    if (Px.Dist(px.At(x, y), prev.At(x, y)) > 40) changed++;
                }
                jumps.Add((double)changed / n);
            }
            prev = px;
        }
        File.WriteAllText(Path.Combine(dir, "jumps.txt"), string.Join("\n", jumps.Select(j => j.ToString("0.0000"))));
        var spikes = new List<(int, double)>();
        for (int i = 0; i < jumps.Count; i++)
        {
            double around = Math.Max(i > 0 ? jumps[i - 1] : 0, i + 1 < jumps.Count ? jumps[i + 1] : 0);
            if (jumps[i] > 0.02 && jumps[i] > 2.5 * around) spikes.Add((i + 2, jumps[i]));
        }
        return spikes;
    }
}
