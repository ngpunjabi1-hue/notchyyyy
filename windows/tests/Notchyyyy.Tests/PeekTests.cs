using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Notchyyyy.Motion;
using Notchyyyy.Notch;
using Notchyyyy.Sprout;
using Notchyyyy.Theme;
using Notchyyyy.Today;
using Xunit;
using Px = Notchyyyy.Tests.MediaTests.Px;

namespace Notchyyyy.Tests;

/// Step 11: Sprout's senses, water reminders and the peek bubble.
public class PeekTests
{
    static string Out(string name)
    {
        var d = Path.Combine(Environment.GetEnvironmentVariable("NOTCHYYYY_FRAMES") ?? Path.GetTempPath(), name);
        Directory.CreateDirectory(d);
        return d;
    }

    // ---- senses ----

    [AvaloniaFact]
    public void TypingAndSteppingAwayAreSensed()
    {
        var b = new BuddyState { Typing = false, Away = false, Pace = 1 };
        var clock = new ManualClock { Now = 100 };
        var src = new ManualSenses();
        var s = new Senses(b, clock, src);
        // three keys in a second: not yet typing; the fourth: typing
        for (int i = 0; i < 3; i++) { clock.Now += 0.2; src.Pending = 1; s.Sense(); }
        Assert.False(b.Typing);
        clock.Now += 0.2; src.Pending = 1; s.Sense();
        Assert.True(b.Typing);
        // flying: ten a second for a while → the pace rises toward 2
        for (int i = 0; i < 30; i++) { clock.Now += 0.05; src.Pending = i % 2 == 0 ? 1 : 0; s.Sense(); }
        Assert.True(b.Pace > 1.2, $"pace {b.Pace}");
        // a second with no keys: typing stops and the pace goes back to 1
        for (int i = 0; i < 20; i++) { clock.Now += 0.05; s.Sense(); }
        Assert.False(b.Typing);
        Assert.Equal(1, b.Pace);
        // a minute without input: away; any input: back, with a startled little wake-up
        src.SecondsSinceInput = 61; s.Sense();
        Assert.True(b.Away);
        src.SecondsSinceInput = 0.1; s.Sense();
        Assert.False(b.Away);
        Assert.True(b.WakeUntil > clock.Now);
    }

    // ---- water ----

    static Water W(Func<DateTime> clock) => new(Path.Combine(Directory.CreateTempSubdirectory("water").FullName, "water.json"), readOnly: false) { Clock = clock };

    [Fact]
    public void WaterRemindsOnItsIntervalInWakingHours()
    {
        var now = new DateTime(2026, 11, 28, 13, 0, 0);
        var w = W(() => now);
        w.Reminded();                               // the clock starts now
        Assert.Equal(now.AddMinutes(60), w.NextDue());
        now = now.AddMinutes(59);
        Assert.False(w.IsDue());
        Assert.True(w.DueSoon);                     // the heads-up, in the last minute
        now = now.AddMinutes(1);
        Assert.True(w.IsDue());
        w.Later();                                  // fifteen minutes' grace
        Assert.False(w.IsDue());
        now = now.AddMinutes(15);
        Assert.True(w.IsDue());
        w.Drank();
        Assert.Equal(1, w.Count);
        Assert.False(w.IsDue());
        now = new DateTime(2026, 11, 28, 22, 30, 0);   // night: no reminders
        Assert.Null(w.NextDue());
        Assert.False(w.IsDue());
        now = new DateTime(2026, 11, 29, 9, 0, 0);     // a new day starts from zero
        w.IsDue();
        Assert.Equal(0, w.Count);
    }

    [Fact]
    public void TodaysGlassesAreKept()
    {
        var now = new DateTime(2026, 11, 28, 10, 0, 0);
        var file = Path.Combine(Directory.CreateTempSubdirectory("water").FullName, "water.json");
        var a = new Water(file, readOnly: false) { Clock = () => now };
        a.Drank(); a.Drank();
        // the file is read when the store is made: today's count comes back, another day's doesn't
        Assert.Equal(2, new Water(file, readOnly: false, clock: () => now).Count);
        Assert.Equal(0, new Water(file, readOnly: false, clock: () => now.AddDays(1)).Count);
    }

    // ---- the bubble ----

    sealed class Rig : IDisposable
    {
        public readonly NotchModel M = new();
        public readonly ManualClock Clock = new() { Now = 1 };
        public readonly NotchStage Stage;
        public readonly HoverTracker Tracker;
        public readonly Window Win;
        public bool Interactive;
        public Rig(bool light = false)
        {
            Prefs.Shared.Appearance = light ? Appearance.Light : Appearance.Dark;
            M.Buddy.Hour = () => 14;
            M.UserName = "Alex Morgan";
            Stage = new NotchStage(M, Clock) { Width = 820, Height = 430 };
            Tracker = new HoverTracker(M, Clock) { SetInteractive = on => Interactive = on };
            Win = new Window { Width = 820, Height = 430, Content = new Grid { Background = new SolidColorBrush(Color.FromRgb(140, 160, 190)), Children = { Stage } } };
            Win.Show();
            Win.SetRenderScaling(2);
            Run(1.5);
        }
        public void Remind() { M.Water.Reminded(); M.ShowPeek(Peek.Hydrate, Clock.Now, 40, () => M.Water.Later(15)); }
        public WriteableBitmap Run(double until, Point? pointer = null)
        {
            for (double t = Clock.Now; t <= until + 1e-9; t += 1 / 30.0)
            {
                Clock.Now = t; M.TickPeek(t); Tracker.Tick(pointer ?? new Point(0, 300)); Stage.Update(t); Dispatcher.UIThread.RunJobs();
            }
            return Frame();
        }
        public WriteableBitmap Frame() { Dispatcher.UIThread.RunJobs(); return Win.CaptureRenderedFrame()!; }
        public Point Centre(Control c) => c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), Win)!.Value;
        public void Dispose() { Win.Close(); Prefs.Shared.Appearance = Appearance.Dark; }
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public void TheBubbleSwellsAndAsks(bool light)
    {
        using var rig = new Rig(light);
        rig.Remind();
        var bmp = rig.Run(rig.Clock.Now + 1.2);
        bmp.Save(Path.Combine(Out("peek"), $"bubble-{(light ? "light" : "dark")}.png"), new PngBitmapEncoderOptions());
        Assert.Equal(420, rig.Stage.CurrentWidth, 0);
        Assert.Equal(rig.M.NotchSize.Height + 60, rig.Stage.CurrentHeight, 0);
        Assert.Equal("Time for a sip, Alex", rig.Stage.Peek.Title);
        Assert.Equal("0 of 8 glasses so far", rig.Stage.Peek.Detail);
        // always black, in either theme
        var px = new Px(bmp, 2);
        var c = px.At(410 - 150, 70);
        Assert.True(c.R < 20 && c.G < 20 && c.B < 20, $"bubble not black: {c}");
        // the Drank button is water-blue
        var d = rig.Centre(rig.Stage.Peek.DrankButton);
        Assert.True(Px.Dist(px.At(d.X + 28, d.Y), PeekBubble.WaterBlue) < 70, $"Drank not blue: {px.At(d.X + 28, d.Y)}");   // right of its word
    }

    /// Drank: a glass is counted, Sprout cheers ("Nice, that's 1 today"), and 1.8 s later the bubble tucks away.
    [AvaloniaFact]
    public void DrankCheersThenTucksAway()
    {
        using var rig = new Rig();
        rig.Remind();
        rig.Run(rig.Clock.Now + 1.0);
        var drank = rig.Centre(rig.Stage.Peek.DrankButton);
        rig.Run(rig.Clock.Now + 0.1, new Point(drank.X - 410, drank.Y));   // the pointer is over the bubble: it takes clicks
        Assert.True(rig.Interactive);
        rig.Win.MouseMove(drank, RawInputModifiers.None);
        rig.Win.MouseDown(drank, MouseButton.Left); rig.Win.MouseUp(drank, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, rig.M.Water.Count);
        Assert.Equal(Peek.Cheer, rig.M.Peeking);
        Assert.Equal("Nice, that's 1 today", rig.Stage.Peek.Title);
        Assert.Equal("7 more to reach your goal", rig.Stage.Peek.Detail);
        Assert.True(rig.M.Buddy.CheerUntil > rig.Clock.Now);
        rig.Frame().Save(Path.Combine(Out("peek"), "cheer.png"), new PngBitmapEncoderOptions());
        rig.Run(rig.Clock.Now + 2.5, new Point(drank.X - 410, drank.Y));
        Assert.Equal(Peek.None, rig.M.Peeking);
        Assert.False(rig.M.Expanded);                     // it didn't spring open under the pointer that clicked
        Assert.Equal(rig.M.ClosedSize.Width, rig.Stage.CurrentWidth, 0);
    }

    [AvaloniaFact]
    public void LaterAndLeftAlone()
    {
        using var rig = new Rig();
        rig.Remind();
        rig.Run(rig.Clock.Now + 1.0);
        rig.Stage.Peek.LaterButton.Press();
        Assert.Equal(Peek.None, rig.M.Peeking);
        Assert.Equal(0, rig.M.Water.Count);
        rig.Run(rig.Clock.Now + 1.0);
        // left alone for 40 s it tucks itself away and asks again in 15 minutes
        rig.Remind();
        rig.Run(rig.Clock.Now + 41);
        Assert.Equal(Peek.None, rig.M.Peeking);
        Assert.Equal(Wall.Now().AddMinutes(15), rig.M.Water.NextDue()!.Value, TimeSpan.FromSeconds(1));
    }

    /// While the bubble is up the notch doesn't open under the pointer; opening the notch (an Expand) ends it.
    [AvaloniaFact]
    public void TheNotchDoesntOpenUnderTheBubble()
    {
        using var rig = new Rig();
        rig.Remind();
        rig.Run(rig.Clock.Now + 2.0, new Point(0, 10));    // resting on the pill's spot
        Assert.False(rig.M.Expanded);
        Assert.False(rig.M.Hovering);
        rig.Tracker.Expand();
        Assert.Equal(Peek.None, rig.M.Peeking);
        Assert.True(rig.M.Expanded);
    }

    [AvaloniaFact]
    public void TheHeadsUpRingShowsBeforeAReminder()
    {
        using var rig = new Rig();
        var now = Wall.Now();
        rig.M.Water.Clock = () => now;
        rig.M.Water.Reminded();
        now = now.AddMinutes(59.5);
        Assert.True(rig.M.Water.DueSoon);
        rig.M.Notify("Water");
        var bmp = rig.Run(rig.Clock.Now + 0.3);
        bmp.Save(Path.Combine(Out("peek"), "heads-up.png"), new PngBitmapEncoderOptions());
        var px = new Px(bmp, 2);
        double right = 410 + rig.M.ClosedSize.Width / 2 - 18 - 12;
        int blue = 0;
        for (double x = right - 9; x <= right + 9; x += 0.5) for (double y = 7; y <= 25; y += 0.5) if (Px.Dist(px.At(x, y), PeekBubble.WaterBlue) < 80) blue++;
        Assert.True(blue > 6, $"no water ring in the pill ({blue})");
    }

    /// Frame by frame at 120 Hz: the bubble swelling out and tucking back. No pops.
    [AvaloniaTheory]
    [InlineData("swell")] [InlineData("tuck")]
    public void TheBubbleMovesWithoutPops(string what)
    {
        using var rig = new Rig();
        if (what == "tuck") { rig.Remind(); rig.Run(rig.Clock.Now + 1.2); rig.M.EndPeek(); }
        else rig.Remind();
        MediaTests.Px? prev = null;
        var jumps = new List<double>();
        for (int i = 0; i < 96; i++)
        {
            rig.Clock.Now += 1 / 120.0;
            rig.Stage.Update(rig.Clock.Now);
            var bmp = rig.Frame();
            if (Environment.GetEnvironmentVariable("NOTCHYYYY_ALLFRAMES") is not null) bmp.Save(Path.Combine(Out("peek-motion-" + what), $"f{i:D3}.png"), new PngBitmapEncoderOptions());
            var px = new Px(bmp, 2);
            if (prev is not null)
            {
                int changed = 0, n = 0;
                // the bubble, leaving out Sprout on its left (it keeps moving: that's its idle animation)
                for (double y = 0; y < 100; y += 2) for (double x = 260; x < 640; x += 2) { n++; if (Px.Dist(px.At(x, y), prev.At(x, y)) > 40) changed++; }
                jumps.Add((double)changed / n);
            }
            prev = px;
        }
        File.WriteAllText(Path.Combine(Out("peek-motion"), what + ".txt"), string.Join("\n", jumps.Select(j => j.ToString("0.0000"))));
        Assert.True(jumps.Take(40).Sum() > 0.05, "nothing moved");
        for (int i = 0; i < jumps.Count; i++)
        {
            double around = Math.Max(i > 0 ? jumps[i - 1] : 0, i + 1 < jumps.Count ? jumps[i + 1] : 0);
            Assert.False(jumps[i] > 0.02 && jumps[i] > 2.5 * around, $"a pop at frame {i + 2}: {jumps[i]:P1} (around {around:P1})");
        }
        // settled after 0.7 s, but for the springy height's last tenth of a point: one row of edge pixels stepping its
        // antialiasing a quarter at a time (measured: 50 % → 25 % coverage), under 2 % of the area and never a pop
        Assert.True(jumps.Skip(84).All(j => j < 0.02), "still moving after 0.7 s");
    }
}
