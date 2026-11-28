using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Notchyyyy.Motion;
using Notchyyyy.Sprout;
using Notchyyyy.Theme;
using Xunit;

namespace Notchyyyy.Tests;

public class SproutTests
{
    static readonly SproutMood[] All = { SproutMood.Idle, SproutMood.Happy, SproutMood.Sleepy, SproutMood.Thirsty, SproutMood.Cheer,
        SproutMood.Running, SproutMood.Hurt, SproutMood.Surprised, SproutMood.Love, SproutMood.Dizzy, SproutMood.Music };

    static string Out(string name)
    {
        var d = Path.Combine(Environment.GetEnvironmentVariable("NOTCHYYYY_FRAMES") ?? Path.GetTempPath(), name);
        Directory.CreateDirectory(d);
        return d;
    }

    /// Every mood side by side at 68 pt (the Mac app's --mood-sheet), dark and light, at 2x.
    [AvaloniaTheory]
    [InlineData(false, false, Accent.Indigo)] [InlineData(true, false, Accent.Indigo)] [InlineData(false, true, Accent.Indigo)]
    [InlineData(false, false, Accent.Orange)]
    public void MoodSheet(bool light, bool headphones, Accent accent)
    {
        Prefs.Shared.Appearance = light ? Appearance.Light : Appearance.Dark;
        Prefs.Shared.Accent = accent;
        var clock = new ManualClock { Now = 1.0 };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var views = All.Select(m => new SproutView(clock, 68) { Mood = m, Headphones = headphones }).ToList();
        foreach (var v in views) row.Children.Add(v);
        var win = new Window { Width = 11 * 72 + 40, Height = 140, Content = new Border { Background = new SolidColorBrush(LT.Bg), Child = row } };
        win.Show();
        win.SetRenderScaling(2);
        // let the head-phones and gaze settle, then a moment between blinks
        for (double t = 1.0; t <= 3.0; t += 1 / 60.0) { clock.Now = t; foreach (var v in views) v.InvalidateVisual(); Dispatcher.UIThread.RunJobs(); win.CaptureRenderedFrame(); }
        var bmp = win.CaptureRenderedFrame()!;
        bmp.Save(Path.Combine(Out("sprout"), $"moods-{(light ? "light" : "dark")}{(headphones ? "-headphones" : "")}-{accent.ToString().ToLowerInvariant()}.png"), new PngBitmapEncoderOptions());
        for (int i = 0; i < All.Length; i++) Assert.Equal(All[i], views[i].Showing);
        win.Close();
        Prefs.Shared.Appearance = Appearance.Dark;
        Prefs.Shared.Accent = Accent.Indigo;
    }

    static double EyeJump(SproutPose a, SproutPose b)
    {
        double m = 0;
        for (int i = 0; i < 2; i++)
        {
            var x = a.Eyes[i]; var y = b.Eyes[i];
            foreach (var (p, q) in new[] { (x.A, y.A), (x.M, y.M), (x.B, y.B) })
                m = Math.Max(m, Math.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y)));
            m = Math.Max(m, Math.Abs(x.W - y.W));
        }
        return m;
    }

    /// Every mood to every other: the eyes travel smoothly (no frame jumps more than 1.6 design units at 60 fps;
    /// a blink alone moves about 1), the blend lands exactly on the new pose, and signs fade rather than pop.
    [Fact]
    public void EveryMoodMorphsSmoothlyIntoEveryOther()
    {
        double worst = 0; string worstPair = "";
        foreach (var from in All)
        foreach (var to in All)
        {
            if (from == to) continue;
            var tw = new SproutTween();
            double t = 10.3;                                   // between blinks
            tw.Pose(from, default, t);
            SproutPose prev = tw.Pose(from, default, t += 1 / 60.0).Pose;
            for (int f = 0; f < 45; f++)
            {
                var cur = tw.Pose(to, default, t += 1 / 60.0).Pose;
                double j = EyeJump(prev, cur);
                if (j > worst) { worst = j; worstPair = $"{from}→{to} frame {f}"; }
                foreach (var (_, a) in cur.Signs) Assert.InRange(a, 0, 1.0001);
                prev = cur;
            }
            var end = SproutPose.For(to, t);
            Assert.True(EyeJump(prev, end) < 1e-9, $"{from}→{to} doesn't land on the {to} pose");
        }
        Assert.True(worst <= 1.6, $"eyes jump {worst:0.00} units in one frame ({worstPair})");
    }

    /// Changing mood halfway through a change starts from what's on screen: no jump at the switch.
    [Fact]
    public void ChangingMindMidMorphHasNoJump()
    {
        foreach (var a in All) foreach (var b in All) foreach (var c in All)
        {
            if (a == b || b == c) continue;
            var tw = new SproutTween();
            double t = 10.3;
            tw.Pose(a, default, t);
            for (int f = 0; f < 12; f++) tw.Pose(b, default, t += 1 / 60.0);      // 0.2 s into a→b
            var before = tw.Pose(b, default, t += 1 / 60.0).Pose;
            var after = tw.Pose(c, default, t += 1 / 60.0).Pose;
            Assert.True(EyeJump(before, after) <= 1.6, $"{a}→{b} interrupted by {c}: jump {EyeJump(before, after):0.00}");
        }
    }

    /// A strip of frames through idle → happy → sleepy → surprised → hurt → idle, for looking at.
    [AvaloniaFact]
    public void MorphStrip()
    {
        var clock = new ManualClock { Now = 10.3 };
        var v = new SproutView(clock, 68);
        var win = new Window { Width = 100, Height = 100, Content = new Border { Background = Brushes.Black, Child = v } };
        win.Show();
        win.SetRenderScaling(2);
        var dir = Out("sprout-morph");
        var seq = new[] { SproutMood.Idle, SproutMood.Happy, SproutMood.Sleepy, SproutMood.Surprised, SproutMood.Hurt, SproutMood.Idle };
        int n = 0;
        foreach (var m in seq)
        {
            v.Mood = m;
            for (int f = 0; f < 30; f++)
            {
                clock.Now += 1 / 60.0;
                v.InvalidateVisual(); Dispatcher.UIThread.RunJobs();
                var bmp = win.CaptureRenderedFrame()!;
                if (f % 3 == 0) bmp.Save(Path.Combine(dir, $"f{n++:D3}.png"), new PngBitmapEncoderOptions());
            }
        }
        win.Close();
    }

    /// The little Sprout in the closed pill, in every state, most important first (the Mac's NotchBuddy rules).
    [AvaloniaFact]
    public void ClosedPillSproutInEveryState()
    {
        var cases = new (string Name, Action<Notch.NotchModel, double> Set, SproutMood Expect)[]
        {
            ("idle", (m, t) => { }, SproutMood.Idle),
            ("hover", (m, t) => m.Hovering = true, SproutMood.Happy),
            ("typing fast", (m, t) => { m.Buddy.Typing = true; m.Buddy.Pace = 1.8; }, SproutMood.Running),
            ("music", (m, t) => m.MusicPlaying = true, SproutMood.Music),
            ("away", (m, t) => m.Buddy.Away = true, SproutMood.Sleepy),
            ("back from away", (m, t) => m.Buddy.WakeUntil = t + 5, SproutMood.Surprised),
            ("drank water", (m, t) => m.Buddy.CheerUntil = t + 5, SproutMood.Cheer),
            ("night", (m, t) => m.Buddy.Hour = () => 1, SproutMood.Sleepy),
            ("hover beats typing", (m, t) => { m.Hovering = true; m.Buddy.Typing = true; }, SproutMood.Happy),
            ("cheer beats hover", (m, t) => { m.Hovering = true; m.Buddy.CheerUntil = t + 5; }, SproutMood.Cheer),
            ("typing beats music", (m, t) => { m.Buddy.Typing = true; m.MusicPlaying = true; }, SproutMood.Running),
        };
        var dir = Out("buddy");
        foreach (var (name, set, expect) in cases)
        {
            var model = new Notch.NotchModel();
            model.Buddy.Hour = () => 14;
            var clock = new ManualClock { Now = 1 };
            var stage = new Notch.NotchStage(model, clock) { Width = 820, Height = 430 };
            var win = new Window { Width = 820, Height = 430, Content = new Grid { Background = new SolidColorBrush(Color.FromRgb(140, 160, 190)), Children = { stage } } };
            win.Show();
            win.SetRenderScaling(2);
            set(model, clock.Now);
            WriteableBitmap? bmp = null;
            for (double t = 1; t <= 2.5; t += 1 / 30.0)
            {
                clock.Now = t; stage.Update(t); stage.Buddy.InvalidateVisual(); Dispatcher.UIThread.RunJobs();
                bmp = win.CaptureRenderedFrame();
            }
            Assert.True(stage.Buddy.Showing == expect, $"{name}: Sprout shows {stage.Buddy.Showing}, expected {expect}");
            if (model.MusicPlaying)
            {
                // the music bars show on the right as soon as music plays (they used to stay blank: fixed)
                using var fb = bmp!.Lock();
                var px = new byte[fb.RowBytes * fb.Size.Height];
                System.Runtime.InteropServices.Marshal.Copy(fb.Address, px, 0, px.Length);
                int lit = 0;
                double barsX = 410 + (180 + 12 + 92) / 2.0 - 18 - 12;   // centre of the trailing slot (pt)
                for (int y = 6 * 2; y < 26 * 2; y++)
                for (int x = (int)((barsX - 10) * 2); x < (int)((barsX + 10) * 2); x++)
                    if (px[y * fb.RowBytes + x * 4 + 1] > 60) lit++;
                Assert.True(lit > 20, $"{name}: no music bars ({lit} lit pixels)");
            }
            bmp!.Save(Path.Combine(dir, $"{name.Replace(' ', '-')}.png"), new PngBitmapEncoderOptions());
            win.Close();
        }
    }
}
