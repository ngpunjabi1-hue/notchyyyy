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
using Notchyyyy.SystemInfo;
using Notchyyyy.Theme;
using Xunit;
using Px = Notchyyyy.Tests.MediaTests.Px;

namespace Notchyyyy.Tests;

/// Step 10: the System tab — readings, history, the cards and their motion.
public class SystemTests
{
    static string Out(string name)
    {
        var d = Path.Combine(Environment.GetEnvironmentVariable("NOTCHYYYY_FRAMES") ?? Path.GetTempPath(), name);
        Directory.CreateDirectory(d);
        return d;
    }

    const double GB = 1_073_741_824;

    [Theory]
    [InlineData(8.1, "8.1 GB")] [InlineData(512, "512 GB")] [InlineData(1900, "1.9 TB")] [InlineData(0.5, "0.5 GB")]
    public void SizesReadNaturally(double gb, string text) => Assert.Equal(text, SystemMonitor.Bytes(gb * GB));

    static SystemSample S(double cpu, double? gpu = 0.2, double mem = 0.5, double disk = 0.62) =>
        new(cpu, Enumerable.Repeat(cpu, 8).ToArray(), gpu, mem * 16 * GB, 16 * GB, disk * 512 * GB, 512 * GB);

    [AvaloniaFact]
    public void HistoryStartsLevelThenScrolls()
    {
        var m = new SystemMonitor();
        m.Apply(S(0.3));
        Assert.All(m.CpuHistory, v => Assert.Equal(0.3, v, 6));   // primed: no rise from zero
        m.Apply(S(0.7));
        Assert.Equal(0.7, m.CpuHistory[^1], 6);
        Assert.Equal(0.3, m.CpuHistory[^2], 6);
        Assert.Equal(SystemMonitor.History, m.CpuHistory.Length);
        Assert.Equal(0.5, m.Mem, 6);
        Assert.Equal(0.62, m.Disk, 6);
    }

    [Fact]
    public void LoadColours()
    {
        Assert.Equal(LT.AccentColor, SystemView.Load(0.3));
        Assert.Equal(Color.FromRgb(237, 166, 51), SystemView.Load(0.7));
        Assert.Equal(LT.Red, SystemView.Load(0.9));
        Assert.Equal(LT.AccentColor, SystemView.Load(0.8, 0.85, 0.95));   // storage is calm until 85 %
    }

    sealed class Rig : IDisposable
    {
        public readonly NotchModel M = new();
        public readonly ManualClock Clock = new() { Now = 1 };
        public readonly NotchStage Stage;
        public readonly Window Win;
        public Rig(bool light = false)
        {
            Prefs.Shared.Appearance = light ? Appearance.Light : Appearance.Dark;
            M.Buddy.Hour = () => 14;
            var src = new SampleSystem();
            M.System.Use(src);
            M.System.Apply(src.Read());
            for (int i = 0; i < 39; i++) M.System.Apply(src.Read());   // a minute of history
            Stage = new NotchStage(M, Clock) { Width = 820, Height = 430 };
            Win = new Window { Width = 820, Height = 430, Content = new Grid { Background = new SolidColorBrush(Color.FromRgb(140, 160, 190)), Children = { Stage } } };
            Win.Show();
            Win.SetRenderScaling(2);
            M.Tab = Tab.System;
            Clock.Now = 1.1; M.Expanded = true;
            Run(2.4);
        }
        public WriteableBitmap Run(double until)
        {
            for (double t = Clock.Now; t <= until + 1e-9; t += 1 / 30.0) { Clock.Now = t; Stage.Update(t); Dispatcher.UIThread.RunJobs(); }
            return Frame();
        }
        public WriteableBitmap Frame()
        {
            foreach (var c in Win.GetVisualDescendants().OfType<LiveCanvas>()) c.Kick();
            Dispatcher.UIThread.RunJobs();
            return Win.CaptureRenderedFrame()!;
        }
        public SystemView View => Stage.Body.CurrentSystem!;
        public void Dispose() { Win.Close(); Prefs.Shared.Appearance = Appearance.Dark; }
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public void CardsLookRight(bool light)
    {
        using var rig = new Rig(light);
        var bmp = rig.Frame();
        bmp.Save(Path.Combine(Out("system"), $"system-{(light ? "light" : "dark")}.png"), new PngBitmapEncoderOptions());
        var s = rig.M.System;
        Assert.Equal(s.Cpu, rig.View.CpuCard.Shown, 6);
        Assert.Equal("Intel Core Ultra 7 155H · 12 cores", rig.View.CpuCard.Detail);
        Assert.Equal("Intel Arc Graphics", rig.View.GpuCard.Detail);
        Assert.Matches(@"^\d+\.\d GB of 16\.0 GB$", rig.View.MemCard.Detail);
        Assert.Equal("195 GB free of 512 GB", rig.View.DiskCard.Detail);
        Assert.Equal(12, rig.View.Cores.Count);
        // each sparkline is drawn: its line's colour shows inside the CPU card's graph area
        var spark = rig.View.CpuCard.Spark;
        var px = new Px(bmp, 2);
        var colour = SystemView.Load(s.Cpu);
        int hits = 0;
        for (double x = 2; x < spark.Bounds.Width - 2; x += 1)
        for (double y = 1; y < spark.Bounds.Height - 1; y += 1)
        {
            var p = spark.TranslatePoint(new Point(x, y), rig.Win)!.Value;
            if (Px.Dist(px.At(p.X, p.Y), colour) < 50) hits++;
        }
        Assert.True(hits > 60, $"sparkline not drawn ({hits} points in its colour)");
    }

    /// A new reading: the number counts to it (no jump), the line glides to it over 1.4 s and settles — no pops.
    [AvaloniaFact]
    public void ANewReadingGlidesIn()
    {
        using var rig = new Rig();
        rig.M.System.Apply(S(0.2));
        rig.Run(rig.Clock.Now + 2);
        rig.M.System.Apply(S(0.9, mem: 0.8));
        var shown = new List<int>();
        MediaTests.Px? prev = null;
        var jumps = new List<double>();
        for (int i = 0; i < 100; i++)
        {
            rig.Clock.Now += 1 / 60.0;
            rig.Stage.Update(rig.Clock.Now);
            var px = new Px(rig.Frame(), 2);
            shown.Add(int.Parse(((Percent)rig.View.CpuCard.GetVisualDescendants().OfType<Percent>().First()).Label.TrimEnd('%')));
            if (prev is not null)
            {
                int changed = 0, n = 0;
                for (double y = 46; y < 276; y += 3) for (double x = 98; x < 722; x += 3) { n++; if (Px.Dist(px.At(x, y), prev.At(x, y)) > 40) changed++; }
                jumps.Add((double)changed / n);
            }
            prev = px;
        }
        Assert.Equal(90, shown[^1]);
        // it counts: many in-between values, never a step of more than a third of the way in one frame
        Assert.True(shown.Distinct().Count() > 8, $"only {shown.Distinct().Count()} values shown");
        Assert.True(shown.Zip(shown.Skip(1), (a, b) => Math.Abs(b - a)).Max() < 25, "the number jumped");
        for (int i = 0; i < jumps.Count; i++)
        {
            double around = Math.Max(i > 0 ? jumps[i - 1] : 0, i + 1 < jumps.Count ? jumps[i + 1] : 0);
            Assert.False(jumps[i] > 0.02 && jumps[i] > 2.5 * around, $"a pop at frame {i + 2}: {jumps[i]:P1}");
        }
        Assert.True(jumps.Skip(90).All(j => j < 0.002), "still moving after 1.5 s");
        // the CPU card is red now (over 85 %)
        rig.Frame().Save(Path.Combine(Out("system"), "busy.png"), new PngBitmapEncoderOptions());
    }

    [AvaloniaFact]
    public void CardsBrightenOnHover()
    {
        using var rig = new Rig();
        var card = rig.View.GpuCard;
        rig.Win.MouseMove(card.TranslatePoint(new Point(40, 40), rig.Win)!.Value, RawInputModifiers.None);
        rig.Run(rig.Clock.Now + 0.4);
        Assert.True(card.Hovered);
        Assert.False(rig.View.CpuCard.Hovered);
    }

    /// Without GPU counters (a machine with no graphics chip), the card says so instead of claiming 0 %.
    [AvaloniaFact]
    public void NoGpuReadingsIsSaid()
    {
        using var rig = new Rig();
        rig.M.System.Apply(S(0.3, gpu: null));
        Assert.Equal("No GPU readings here", rig.View.GpuCard.Detail);
    }
}
