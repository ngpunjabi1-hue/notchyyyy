using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Notchyyyy.Motion;
using Notchyyyy.Notch;
using Notchyyyy.Theme;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(Notchyyyy.Tests.TestApp))]

namespace Notchyyyy.Tests;

public class TestApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Application>()
        .UseSkia().WithInterFont()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>
/// Frame-by-frame, pixel-level checks of the open / close motion. Every frame is rendered at an exact time
/// on a saturated magenta backdrop (a colour neither the shape nor its content can produce), then:
///  • every pixel outside the shape must be exactly the backdrop, so there is no shadow, glow or halo, ever;
///  • the shape's left, right and bottom edges are found to sub-pixel precision from the anti-aliased pixels
///    and compared with where the springs say they should be;
///  • any non-backdrop pixel outside the shape counts as a leak (content escaping the clip);
///  • once closing has run 0.125 s, any open-body pixel still drawn counts as a ghost.
/// Frames and a CSV go to $NOTCHYYYY_FRAMES (or the temp folder) for viewing.
/// </summary>
public class FrameTests
{
    static readonly Color Backdrop = Color.FromRgb(230, 40, 120);
    const double W = 820, H = 430, Cx = W / 2;

    sealed record Measure(double T, double ExpL, double ExpR, double ExpB, double L, double R, double B, int Leaks, int Ghosts, double Pour, int Header, int Bar, int Centre);

    sealed class Rig
    {
        public readonly NotchModel M = new();
        public readonly ManualClock Clock = new();
        public readonly NotchStage Stage;
        public readonly Window Win;
        public Rig(bool music)
        {
            M.MusicPlaying = music;
            Stage = new NotchStage(M, Clock) { Width = W, Height = H };
            Win = new Window { Width = W, Height = H, Content = new Grid { Background = new SolidColorBrush(Backdrop), Children = { Stage } } };
            Win.Show();
            Frame(0);
        }
        public WriteableBitmap Frame(double t)
        {
            Clock.Now = t;
            Stage.Update(t);
            Dispatcher.UIThread.RunJobs();
            return Win.CaptureRenderedFrame()!;
        }
        public void Open() { M.Expanded = true; M.Hovering = false; }
        public void Close() { M.Expanded = false; M.Hovering = false; }
    }

    /// Copies the frame out as RGBA, whatever order the buffer uses.
    static byte[] Pixels(WriteableBitmap bmp, out int stride, out int pw, out int ph)
    {
        using var fb = bmp.Lock();
        stride = fb.RowBytes; pw = fb.Size.Width; ph = fb.Size.Height;
        var data = new byte[stride * ph];
        System.Runtime.InteropServices.Marshal.Copy(fb.Address, data, 0, data.Length);
        if (fb.Format == Avalonia.Platform.PixelFormat.Bgra8888)
            for (int i = 0; i + 3 < data.Length; i += 4) (data[i], data[i + 2]) = (data[i + 2], data[i]);
        else Assert.Equal(Avalonia.Platform.PixelFormat.Rgba8888, fb.Format);
        return data;
    }

    static Measure MeasureFrame(Rig rig, WriteableBitmap bmp, double t, bool closingGhostCheck)
    {
        var px = Pixels(bmp, out int stride, out int pw, out int ph);
        Assert.Equal((int)W, pw);
        var s = rig.Stage;
        double w = s.CurrentWidth, h = s.CurrentHeight, top = s.CurrentTop, bot = Math.Min(s.CurrentBottom, (h - s.CurrentTop) * 0.9);
        var f = s.CurrentFill;
        double fr = f.R - Backdrop.R, fg = f.G - Backdrop.G, fb = f.B - Backdrop.B, ff = fr * fr + fg * fg + fb * fb;
        (int r, int g, int b) P(int x, int y) { int i = y * stride + x * 4; return (px[i], px[i + 1], px[i + 2]); }
        // how much of the pixel the shape covers (projection onto the backdrop → fill line)
        double C(int x, int y) { var (r, g, b) = P(x, y); return Math.Clamp(((r - Backdrop.R) * fr + (g - Backdrop.G) * fg + (b - Backdrop.B) * fb) / ff, 0, 1); }
        bool IsBackdrop(int x, int y) { var (r, g, b) = P(x, y); return Math.Abs(r - Backdrop.R) <= 2 && Math.Abs(g - Backdrop.G) <= 2 && Math.Abs(b - Backdrop.B) <= 2; }

        double expL = Cx - w / 2 + top, expR = Cx + w / 2 - top, expB = h;
        int row = (int)Math.Floor(top + (h - bot - top) / 2);

        double L = double.NaN, R = double.NaN, B = double.NaN;
        for (int x = 0; x < Cx; x++) if (C(x, row) > 0.01) { L = x + 1 - C(x, row); break; }
        for (int x = pw - 1; x > Cx; x--) if (C(x, row) > 0.01) { R = x + C(x, row); break; }
        // the bottom edge, scanning up from below; open, at a column clear of the tab bar (±182 pt)
        // on the straight part of the bottom edge; open, at a column clear of the tab bar (±182 pt)
        int col = (int)Cx + (w / 2 - top - bot > 262 ? 250 : 40);
        var pill = s.PillOpacity > 0.001 ? s.PillRect.Inflate(2) : default;
        int startY = ph - 1;
        if (pill.Width > 0 && col >= pill.Left && col <= pill.Right) startY = (int)Math.Floor(pill.Top) - 1;   // look above the bar
        if (expB >= startY - 1) B = expB;            // the bar covers this column's edge right now: nothing to measure
        else for (int y = startY; y >= 0; y--) if (C(col, y) > 0.01) { B = y + C(col, y); break; }

        // leaks: anything not backdrop strictly outside the shape
        int leaks = 0;
        for (int y = 0; y < ph; y++)
        for (int x = 0; x < pw; x++)
        {
            bool below = y >= Math.Ceiling(expB) + 1;
            bool beside = y > top + 1 && y < h - bot - 1 && (x < Math.Floor(expL) - 1 || x > Math.Ceiling(expR) + 1);
            bool outsideFlare = y >= 1 && y < h - 1 && (x < Cx - w / 2 - 1 || x > Cx + w / 2 + 1);
            if ((below || beside || outsideFlare) && !IsBackdrop(x, y) && !pill.Contains(new Point(x + 0.5, y + 0.5))) leaks++;
        }

        // ghosts: the header's "Notchyyyy / Home" still drawn after closing has had 0.125 s
        int ghosts = 0;
        if (closingGhostCheck)
        {
            for (int y = 6; y < 26; y++)
            for (int x = (int)Cx - 318; x < (int)Cx - 200; x++)
            {
                if (x <= expL + 1 || x >= expR - 1 || y >= h - bot - 1) continue;    // only inside the shape
                var (r, g, b) = P(x, y);
                if (Math.Abs(r - f.R) + Math.Abs(g - f.G) + Math.Abs(b - f.B) > 12) ghosts++;
            }
        }
        // overlap: how strongly the header text and the pill's art are each showing (max channel distance from the fill)
        int Dist(int x, int y) { var (r, g, b) = P(x, y); return Math.Max(Math.Abs(r - f.R), Math.Max(Math.Abs(g - f.G), Math.Abs(b - f.B))); }
        int header = 0, bar = 0;
        for (int y = 8; y < 24; y++)
        for (int x = (int)Cx - 318; x < (int)Cx - 200; x++)
            if (x > expL + 1 && x < expR - 1 && y < h - bot - 1) header = Math.Max(header, Dist(x, y));
        int ax = (int)Cx - (int)(rig.M.NotchSize.Width + 12 + rig.M.Extra) / 2 + 18 + 12;   // the art's centre
        if (ax > expL + 1 && 16 < h - bot - 1) bar = Dist(ax, 16);
        var (cr, cg, cb) = P((int)Cx, 4);   // the shape's own colour, at its top centre
        return new Measure(t, expL, expR, expB, L, R, B, leaks, ghosts, s.CurrentPour, header, bar, (cr + cg + cb) / 3);
    }

    static string OutDir(string name)
    {
        var root = Environment.GetEnvironmentVariable("NOTCHYYYY_FRAMES") ?? Path.Combine(Path.GetTempPath(), "notchyyyy-frames");
        var d = Path.Combine(root, name);
        if (Directory.Exists(d)) Directory.Delete(d, true);
        Directory.CreateDirectory(d);
        return d;
    }

    static List<Measure> Film(string name, bool light, bool music, double fps, double duration, Action<Rig, double> events, double ghostAfter = double.MaxValue)
    {
        Prefs.Shared.Appearance = light ? Appearance.Light : Appearance.Dark;
        var rig = new Rig(music);
        var dir = OutDir(name);
        var list = new List<Measure>();
        var csv = new StringBuilder("t,expL,L,expR,R,expB,B,leaks,ghosts,pour,header,bar\n");
        int n = (int)Math.Round(duration * fps);
        for (int i = 0; i <= n; i++)
        {
            double t = i / fps;
            rig.Clock.Now = t;     // events happen at this frame's time
            events(rig, t);
            var bmp = rig.Frame(t);
            var m = MeasureFrame(rig, bmp, t, t >= ghostAfter);
            list.Add(m);
            bmp.Save(Path.Combine(dir, $"f{i:D4}.png"), new PngBitmapEncoderOptions());
            csv.AppendLine(string.Join(",", new[] { m.T, m.ExpL, m.L, m.ExpR, m.R, m.ExpB, m.B, m.Leaks, m.Ghosts, m.Pour, m.Header, m.Bar }
                .Select(v => v.ToString("0.###", CultureInfo.InvariantCulture))));
        }
        File.WriteAllText(Path.Combine(dir, "measure.csv"), csv.ToString());
        rig.Win.Close();
        Prefs.Shared.Appearance = Appearance.Dark;
        return list;
    }

    static void AssertTracks(List<Measure> ms, double tol = 0.6)
    {
        foreach (var m in ms)
        {
            Assert.True(Math.Abs(m.L - m.ExpL) <= tol, $"left edge at t={m.T:0.000}: {m.L:0.00} vs {m.ExpL:0.00}");
            Assert.True(Math.Abs(m.R - m.ExpR) <= tol, $"right edge at t={m.T:0.000}: {m.R:0.00} vs {m.ExpR:0.00}");
            Assert.True(Math.Abs(m.B - m.ExpB) <= tol, $"bottom edge at t={m.T:0.000}: {m.B:0.00} vs {m.ExpB:0.00}");
            Assert.True(m.Leaks == 0, $"{m.Leaks} pixels outside the shape at t={m.T:0.000} (content escaping, or a shadow)");
            Assert.True(m.Ghosts == 0, $"{m.Ghosts} open-body pixels still drawn at t={m.T:0.000}");
            // never both layouts at once: header and pill content can't both be above ~5% visibility
            Assert.True(m.Header <= 12 || m.Bar <= 12, $"header ({m.Header}) and pill content ({m.Bar}) overlap at t={m.T:0.000}");
            // symmetric about the centre: the shape never drifts sideways
            Assert.True(Math.Abs((m.L + m.R) / 2 - Cx) <= 0.5, $"off-centre at t={m.T:0.000}");
        }
    }

    [AvaloniaTheory]
    [InlineData(false, 60)] [InlineData(true, 60)] [InlineData(false, 120)]
    public void OpenThenClose(bool light, int fps)
    {
        var ms = Film($"open-close-{(light ? "light" : "dark")}-{fps}", light, music: true, fps, 1.8, (r, t) =>
        {
            if (Near(t, 0.10, fps)) r.Open();
            if (Near(t, 1.00, fps)) r.Close();
        }, ghostAfter: 1.125);
        AssertTracks(ms);
        // opened fully, overshot a little, settled; then closed back to the pill
        Assert.InRange(ms.Max(m => m.B), 302, 330);
        Assert.InRange(ms.First(m => m.T >= 0.95).B, 299.4, 300.6);
        Assert.InRange(ms.Last().B, 31.4, 32.6);
        // the fill: open body fully in the theme's background colour once open, closed pill pure black soon after closing
        int bg = light ? 249 : 0;
        Assert.All(ms.Where(m => m.T >= 0.50 && m.T < 1.0), m => Assert.InRange(m.Centre, bg - 2, bg + 2));
        Assert.All(ms.Where(m => m.T >= 1.22), m => Assert.InRange(m.Centre, 0, 2));
    }

    [AvaloniaFact]
    public void InterruptedMidwayStaysContinuous()
    {
        const int fps = 120;
        var ms = Film("interrupted-dark-120", false, music: true, fps, 1.4, (r, t) =>
        {
            if (Near(t, 0.10, fps)) r.Open();
            if (Near(t, 0.25, fps)) r.Close();
            if (Near(t, 0.45, fps)) r.Open();
        });
        AssertTracks(ms);
        // no jumps: frame to frame, the edges move about as much as the springs say (never a snap)
        for (int i = 1; i < ms.Count; i++)
        {
            double expStep = Math.Abs(ms[i].ExpB - ms[i - 1].ExpB), step = Math.Abs(ms[i].B - ms[i - 1].B);
            Assert.True(Math.Abs(step - expStep) <= 1.0, $"bottom jumped at t={ms[i].T:0.000}");
        }
    }

    [AvaloniaFact]
    public void HoverSwellsAndRelaxes()
    {
        const int fps = 60;
        var ms = Film("hover-dark-60", false, music: false, fps, 1.0, (r, t) =>
        {
            if (Near(t, 0.10, fps)) r.M.Hovering = true;
            if (Near(t, 0.55, fps)) r.M.Hovering = false;
        });
        AssertTracks(ms);
        Assert.InRange(ms.First(m => m.T >= 0.5).B, 36.5, 37.5);      // 32 + 5
        Assert.InRange(ms.First(m => m.T >= 0.5).R - ms.First(m => m.T >= 0.5).L, 180 + 12 + 16 - 12 - 0.6, 180 + 12 + 16 - 12 + 0.6);
        Assert.InRange(ms.Last().B, 31.5, 32.5);
    }

    static bool Near(double t, double at, double fps) => Math.Abs(t - at) < 0.5 / fps;
}


/// The launch warm-up draws the open body for a few frames at 0.2 % opacity inside the closed pill:
/// it must not change any pixel by more than one colour level.
public class WarmUpTests
{
    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public void WarmUpIsInvisible(bool light)
    {
        Prefs.Shared.Appearance = light ? Appearance.Light : Appearance.Dark;
        var m = new NotchModel { MusicPlaying = false };
        var clock = new ManualClock();
        var stage = new NotchStage(m, clock) { Width = 820, Height = 430 };
        var win = new Window { Width = 820, Height = 430, Content = new Grid { Background = Brushes.Gray, Children = { stage } } };
        win.Show();
        static byte[] Copy(WriteableBitmap b) { using var fb = b.Lock(); var d = new byte[fb.RowBytes * fb.Size.Height]; System.Runtime.InteropServices.Marshal.Copy(fb.Address, d, 0, d.Length); return d; }
        var frames = new List<byte[]>();
        for (int i = 0; i < 8; i++) { Dispatcher.UIThread.RunJobs(); frames.Add(Copy(win.CaptureRenderedFrame()!)); }
        var settled = frames[^1];
        int worst = 0;
        foreach (var f in frames)
            for (int i = 0; i < f.Length; i++) worst = Math.Max(worst, Math.Abs(f[i] - settled[i]));
        Assert.True(worst <= 1, $"warm-up changed a pixel by {worst} levels");
        win.Close();
        Prefs.Shared.Appearance = Appearance.Dark;
    }
}
