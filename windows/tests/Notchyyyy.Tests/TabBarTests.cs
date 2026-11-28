using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Notchyyyy.Motion;
using Notchyyyy.Notch;
using Notchyyyy.Theme;
using Xunit;

namespace Notchyyyy.Tests;

/// <summary>
/// Step 3: the glass tab bar, the close button, tab switching and the keyboard, checked from the pixels and
/// through Avalonia's real input routing (headless mouse and key events, hit-tested like real ones).
/// </summary>
public class TabBarTests
{
    static readonly Color Backdrop = Color.FromRgb(230, 40, 120);
    const double W = 820, H = 430, Cx = W / 2;

    sealed class Rig : IDisposable
    {
        public readonly NotchModel M = new();
        public readonly ManualClock Clock = new();
        public readonly NotchStage Stage;
        public readonly Window Win;
        public Rig(double scale = 1, Tab tab = Tab.Home, bool light = false)
        {
            Prefs.Shared.Appearance = light ? Appearance.Light : Appearance.Dark;
            M.Tab = tab;
            Stage = new NotchStage(M, Clock) { Width = W, Height = H };
            Stage.Pill.Close = () => { M.Expanded = false; M.Hovering = false; };
            Win = new Window { Width = W, Height = H, Content = new Grid { Background = new SolidColorBrush(Backdrop), Children = { Stage } } };
            Win.KeyDown += (_, e) => { if (NotchKeys.Handle(M, () => { M.Expanded = false; }, e.Key, e.KeyModifiers)) e.Handled = true; };
            Win.Show();
            if (scale != 1) Win.SetRenderScaling(scale);
            Frame(0);
        }
        public WriteableBitmap Frame(double t)
        {
            Clock.Now = t;
            Stage.Update(t);
            Dispatcher.UIThread.RunJobs();
            return Win.CaptureRenderedFrame()!;
        }
        public void Open(double at) { Clock.Now = at; M.Expanded = true; M.Hovering = false; }
        public void Dispose() { Win.Close(); Prefs.Shared.Appearance = Appearance.Dark; }
        /// The centre of tab button i / the close button, in window coordinates (DIP).
        public Point Button(int i) => new(Stage.PillRect.X + Stage.Pill.ButtonCentre(i), Stage.PillRect.Y + TabPill.RowHeight / 2);
        public Point CloseButton => new(Stage.PillRect.X + Stage.Pill.CloseCentre, Stage.PillRect.Y + TabPill.RowHeight / 2);
    }

    sealed class Pixels
    {
        readonly byte[] _px; readonly int _stride; public readonly int W, H; public readonly double Scale;
        public Pixels(WriteableBitmap b, double scale)
        {
            using var fb = b.Lock();
            _stride = fb.RowBytes; W = fb.Size.Width; H = fb.Size.Height; Scale = scale;
            _px = new byte[_stride * H];
            System.Runtime.InteropServices.Marshal.Copy(fb.Address, _px, 0, _px.Length);
            if (fb.Format == Avalonia.Platform.PixelFormat.Bgra8888)
                for (int i = 0; i + 3 < _px.Length; i += 4) (_px[i], _px[i + 2]) = (_px[i + 2], _px[i]);
        }
        public Pixels(Bitmap b, double scale)
        {
            W = b.PixelSize.Width; H = b.PixelSize.Height; Scale = scale; _stride = W * 4;
            _px = new byte[_stride * H];
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(_px, System.Runtime.InteropServices.GCHandleType.Pinned);
            try { b.CopyPixels(new PixelRect(b.PixelSize), handle.AddrOfPinnedObject(), _px.Length, _stride); }
            finally { handle.Free(); }
            if (b.Format != Avalonia.Platform.PixelFormat.Rgba8888)
                for (int i = 0; i + 3 < _px.Length; i += 4) (_px[i], _px[i + 2]) = (_px[i + 2], _px[i]);
        }
        /// RGB at a point given in DIP.
        public (int r, int g, int b) At(double x, double y)
        {
            int px = (int)(x * Scale), py = (int)(y * Scale), i = py * _stride + px * 4;
            return (_px[i], _px[i + 1], _px[i + 2]);
        }
        public (int r, int g, int b) AtPx(int x, int y) { int i = y * _stride + x * 4; return (_px[i], _px[i + 1], _px[i + 2]); }
        /// Pixels in a DIP circle brighter than <paramref name="min"/> (icon ink on the dark glass).
        public int Ink(Point c, double r, int min = 90)
        {
            int n = 0;
            for (int y = (int)((c.Y - r) * Scale); y <= (int)((c.Y + r) * Scale); y++)
            for (int x = (int)((c.X - r) * Scale); x <= (int)((c.X + r) * Scale); x++)
            {
                double dx = x / Scale - c.X, dy = y / Scale - c.Y;
                if (dx * dx + dy * dy > r * r) continue;
                var (R, G, B) = AtPx(x, y);
                if ((R + G + B) / 3 >= min) n++;
            }
            return n;
        }
    }

    static string Out(string name)
    {
        var d = Path.Combine(Environment.GetEnvironmentVariable("NOTCHYYYY_FRAMES") ?? Path.GetTempPath(), name);
        Directory.CreateDirectory(d);
        return d;
    }

    // ---- 1. complete at every display scale (100 % … 200 %), through the compositor and through the snapshot path ----

    [AvaloniaTheory]
    [InlineData(1.0)] [InlineData(1.25)] [InlineData(1.5)] [InlineData(2.0)]
    public void TabBarDrawsCompletelyAtEveryScale(double scale)
    {
        using var rig = new Rig(scale);
        rig.Open(0.1);
        var frame = rig.Frame(1.5);
        var dir = Out("tabbar-scales");
        frame.Save(Path.Combine(dir, $"compositor-{scale:0.00}.png"), new PngBitmapEncoderOptions());
        var snapPath = Path.Combine(dir, $"snapshot-{scale:0.00}.png");
        DebugHarness.Snap(rig.Win, snapPath);

        using var snap = new Bitmap(snapPath);
        foreach (var (label, p) in new[] { ("compositor", new Pixels(frame, scale)), ("snapshot", new Pixels(snap, scale)) })
        {
            for (int i = 0; i < rig.Stage.Pill.Tabs.Count; i++)
                Assert.True(p.Ink(rig.Button(i), 9) > 12 * scale * scale, $"{label} at {scale}x: tab {i} ({rig.Stage.Pill.Tabs[i]}) icon missing");
            Assert.True(p.Ink(rig.CloseButton, 8, 200) > 10 * scale * scale, $"{label} at {scale}x: close × missing");
            // the close button's glass circle is there (dark, not the magenta backdrop)
            var (r, _, _) = p.At(rig.CloseButton.X + 14, rig.CloseButton.Y);
            Assert.True(r < 60, $"{label} at {scale}x: close button glass missing (red {r})");
        }
    }

    // ---- 2. arrival: drops in from 28 pt higher, fading in, a beat after the notch starts to open ----

    [AvaloniaFact]
    public void TabBarArrivesThenSettlesExactly()
    {
        const double fps = 120;
        using var rig = new Rig();
        var csv = new StringBuilder("t,opacity,expTop,top\n");
        rig.Open(0.1);
        double lastTop = double.NaN;
        for (int i = 12; i <= 160; i++)
        {
            double t = i / fps;
            var p = new Pixels(rig.Frame(t), 1);
            double op = rig.Stage.PillOpacity;
            double expTop = rig.Stage.PillRect.Y + (TabPill.RowHeight - (TabPill.Button + 2 * TabPill.Inset)) / 2 * (rig.Stage.PillRect.Height / TabPill.RowHeight);
            // the capsule's top edge at a column between two icons, scanning down from just under the body
            double colX = rig.Stage.PillRect.X + (rig.Stage.Pill.ButtonCentre(0) + rig.Stage.Pill.ButtonCentre(1)) / 2 * (rig.Stage.PillRect.Width / rig.Stage.Pill.TotalWidth);
            double top = double.NaN;
            for (int y = (int)Math.Ceiling(rig.Stage.CurrentHeight) + 2; y < H; y++)
            {
                var (r, g, b) = p.At(colX, y);
                if (Math.Abs(r - Backdrop.R) + Math.Abs(g - Backdrop.G) + Math.Abs(b - Backdrop.B) > 24) { top = y; break; }
            }
            csv.AppendLine(string.Join(",", new[] { t, op, expTop, top }.Select(v => v.ToString("0.###", CultureInfo.InvariantCulture))));
            if (t < 0.1 + 0.14 - 1e-9) Assert.True(op < 0.001, $"tab bar visible before its 0.14 s delay (t={t:0.000})");
            if (op > 0.5 && !double.IsNaN(top))
                Assert.True(Math.Abs(top - expTop) <= 1.5, $"capsule top at t={t:0.000}: {top} vs {expTop:0.0}");
            lastTop = top;
        }
        File.WriteAllText(Path.Combine(Out("tabbar-arrival"), "measure.csv"), csv.ToString());
        Assert.Equal(1.0, rig.Stage.PillOpacity, 3);
        Assert.Equal(300 + 10 - 1, lastTop);                            // 10 pt under the body; the 44 pt capsule overhangs the 42 pt row by 1
        Assert.Equal(Cx, rig.Stage.PillRect.Center.X, 3);                // centred
    }

    [AvaloniaFact]
    public void TabBarLeavesWithinPointOneSeconds()
    {
        using var rig = new Rig();
        rig.Open(0.1);
        rig.Frame(1.5);
        rig.Clock.Now = 2.0; rig.M.Expanded = false;
        rig.Frame(2.05);
        Assert.InRange(rig.Stage.PillOpacity, 0.05, 0.95);
        rig.Frame(2.101);
        Assert.Equal(0, rig.Stage.PillOpacity, 3);
        Assert.False(rig.Stage.Pill.IsHitTestVisible);
    }

    // ---- 3. clicks, through real hit-testing ----

    [AvaloniaFact]
    public void ClickingEachTabSelectsItAndCloseCloses()
    {
        using var rig = new Rig();
        rig.Open(0.1);
        rig.Frame(1.5);
        var tabs = rig.Stage.Pill.Tabs;
        double t = 1.5;
        for (int i = tabs.Count - 1; i >= 0; i--)
        {
            var at = rig.Button(i);
            rig.Win.MouseMove(at);
            rig.Win.MouseDown(at, MouseButton.Left);
            rig.Win.MouseUp(at, MouseButton.Left);
            Assert.Equal(tabs[i], rig.M.Tab);
            t += 0.6; rig.Frame(t);
            Assert.Equal(rig.Stage.Pill.ButtonCentre(i), rig.Stage.Pill.CurrentIndicator, 1);   // the circle slid there
        }
        // pressing on one tab and releasing on another does nothing (like a real button)
        rig.Win.MouseDown(rig.Button(3), MouseButton.Left);
        rig.Win.MouseUp(rig.Button(5), MouseButton.Left);
        Assert.Equal(tabs[0], rig.M.Tab);
        rig.Win.MouseMove(rig.CloseButton);
        rig.Win.MouseDown(rig.CloseButton, MouseButton.Left);
        rig.Win.MouseUp(rig.CloseButton, MouseButton.Left);
        Assert.False(rig.M.Expanded);
    }

    [AvaloniaFact]
    public void SelectionCircleSlidesSmoothlyAndLandsOnTheButton()
    {
        using var rig = new Rig();
        rig.Open(0.1);
        rig.Frame(1.5);
        rig.Clock.Now = 1.5; rig.M.Tab = Tab.System;
        double target = rig.Stage.Pill.ButtonCentre(rig.Stage.Pill.Tabs.ToList().IndexOf(Tab.System));
        double prev = rig.Stage.Pill.ButtonCentre(0), maxStep = 0;
        for (int i = 1; i <= 90; i++)
        {
            rig.Frame(1.5 + i / 120.0);
            double x = rig.Stage.Pill.CurrentIndicator;
            maxStep = Math.Max(maxStep, Math.Abs(x - prev));
            Assert.True(x <= target + 4, $"overshoots by more than 4 pt ({x - target:0.0})");   // snappy: damping 0.84
            prev = x;
        }
        Assert.Equal(target, prev, 1);
        Assert.True(maxStep < 25, $"jumps {maxStep:0.0} pt in one 120 Hz frame");
        // and the pixels agree: the circle's glass-wash shows right of the System icon, not under Home any more
        var p = new Pixels(rig.Frame(2.5), 1);
        var sys = rig.Button(rig.Stage.Pill.Tabs.ToList().IndexOf(Tab.System));
        var home = rig.Button(0);
        int lumSys = p.At(sys.X + 13, sys.Y).r, lumHome = p.At(home.X + 13, home.Y).r;
        Assert.True(lumSys > lumHome + 15, $"circle not under System ({lumSys} vs {lumHome})");
    }

    // ---- 4. switching tabs: a blur cross-fade in place; nothing else moves; nothing left behind ----

    [AvaloniaFact]
    public void TabSwitchCrossFadesInPlaceAndLeavesNothingBehind()
    {
        using var rig = new Rig();
        rig.Open(0.1);
        var before = new Pixels(rig.Frame(1.5), 1);
        rig.Clock.Now = 1.5; rig.M.Tab = Tab.Clipboard;
        var dir = Out("tab-switch");
        int midDiff = 0;
        for (int i = 1; i <= 60; i++)
        {
            var bmp = rig.Frame(1.5 + i / 120.0);
            if (i % 4 == 0) bmp.Save(Path.Combine(dir, $"f{i:D3}.png"), new PngBitmapEncoderOptions());
            var p = new Pixels(bmp, 1);
            // "Notchyyyy /" and the time never move or flicker during the switch
            for (int y = 8; y < 24; y++)
            for (int x = (int)Cx - 318; x < (int)Cx - 245; x++)
            {
                var a = p.At(x, y); var b = before.At(x, y);
                Assert.True(Math.Abs(a.r - b.r) + Math.Abs(a.g - b.g) + Math.Abs(a.b - b.b) <= 6, $"header crumb changed at t+{i}/120 ({x},{y})");
            }
            if (i == 8) midDiff = Diff(p, before, Cx - 100, 120, Cx + 100, 230);
        }
        Assert.True(midDiff > 200, "nothing happened mid-switch (no cross-fade)");
        // the end state is pixel-identical to a notch opened straight on Clipboard
        var after = new Pixels(rig.Frame(2.5), 1);
        using var fresh = new Rig(tab: Tab.Clipboard);
        fresh.Open(0.1);
        var expect = new Pixels(fresh.Frame(2.5), 1);   // same time since opening, so the shape's springs match exactly
        int diff = Diff(after, expect, Cx - 350, 0, Cx + 350, 300);
        Assert.True(diff == 0, $"{diff} pixels differ from a fresh Clipboard view (something left behind): {string.Join(" ", LastDiffs)}");
    }

    static readonly List<string> LastDiffs = new();
    static int Diff(Pixels a, Pixels b, double x0, double y0, double x1, double y1)
    {
        int n = 0; LastDiffs.Clear();
        for (int y = (int)y0; y < (int)y1; y++)
        for (int x = (int)x0; x < (int)x1; x++)
        {
            var p = a.At(x, y); var q = b.At(x, y);
            if (Math.Abs(p.r - q.r) + Math.Abs(p.g - q.g) + Math.Abs(p.b - q.b) > 6) { n++; if (LastDiffs.Count < 8) LastDiffs.Add($"({x},{y}) {p}≠{q}"); }
        }
        return n;
    }

    // ---- 5. keyboard, through Avalonia's key routing ----

    [AvaloniaFact]
    public void CtrlNumberSwitchesTabsAndEscCloses()
    {
        using var rig = new Rig();
        rig.Win.KeyPress(Key.D2, RawInputModifiers.Control, PhysicalKey.Digit2, "2");
        Assert.Equal(Tab.Home, rig.M.Tab);                                 // closed: keys do nothing
        rig.Open(0.1);
        rig.Frame(1.0);
        rig.Win.KeyPress(Key.D3, RawInputModifiers.Control, PhysicalKey.Digit3, "3");
        Assert.Equal(rig.M.VisibleTabs[2], rig.M.Tab);
        rig.Win.KeyPress(Key.D8, RawInputModifiers.Control, PhysicalKey.Digit8, "8");
        Assert.Equal(Tab.Settings, rig.M.Tab);
        rig.Win.KeyPress(Key.D9, RawInputModifiers.Control, PhysicalKey.Digit9, "9");   // only 8 tabs: ignored
        Assert.Equal(Tab.Settings, rig.M.Tab);
        rig.Win.KeyPress(Key.D1, RawInputModifiers.None, PhysicalKey.Digit1, "1");      // no Ctrl: ignored
        Assert.Equal(Tab.Settings, rig.M.Tab);
        rig.Win.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.False(rig.M.Expanded);
    }

    // ---- 6. hover: an unselected tab swells and brightens; the selected one doesn't swell ----

    [AvaloniaFact]
    public void HoverSwellsUnselectedTabs()
    {
        using var rig = new Rig(2.0);
        rig.Open(0.1);
        var rest = new Pixels(rig.Frame(1.5), 2.0);
        var media = rig.Button(1);
        rig.Clock.Now = 1.5;
        rig.Win.MouseMove(media);
        var hover = new Pixels(rig.Frame(2.2), 2.0);
        int a = rest.Ink(media, 13, 60), b = hover.Ink(media, 13, 60);
        Assert.True(b > a * 1.08, $"hovered icon didn't grow ({a} → {b} px)");
        var home = rig.Button(0);
        rig.Win.MouseMove(home);
        var h2 = new Pixels(rig.Frame(3.0), 2.0);
        int c = rest.Ink(home, 13, 60), d = h2.Ink(home, 13, 60);
        Assert.InRange(d, c * 0.97, c * 1.03);                             // selected: no swell
    }
}
