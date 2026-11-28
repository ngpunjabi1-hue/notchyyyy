using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Notchyyyy.Motion;
using Notchyyyy.Notch;
using Notchyyyy.Theme;
using Notchyyyy.Updates;
using Xunit;

namespace Notchyyyy.Tests;

/// Step 12, the app's side of updates (installing and updating themselves are checked on the Windows runner).
public class UpdateTests
{
    static string Out(string name)
    {
        var d = Path.Combine(Environment.GetEnvironmentVariable("NOTCHYYYY_FRAMES") ?? Path.GetTempPath(), name);
        Directory.CreateDirectory(d);
        return d;
    }

    /// Without a release (no feed built in) updates are off and nothing ever goes to the network.
    [Fact]
    public void NoFeedMeansNoUpdates()
    {
        Assert.Equal("", Updater.BuiltInFeed);
        var u = new Updater();
        Assert.False(u.Enabled);
        Assert.Null(u.Available);
        Assert.Equal("0.1.0", Updater.CurrentVersion);
    }

    /// The "Updated" bubble shows once: when the version that ran last was a different one.
    [Fact]
    public void UpdatedIsSaidOnce()
    {
        var p = Prefs.Shared;
        string saved = p.LastRunVersion;
        try
        {
            p.LastRunVersion = "";
            Assert.Null(Updater.UpdatedFrom());            // a first run: nothing to say
            Assert.Equal("0.1.0", p.LastRunVersion);
            Assert.Null(Updater.UpdatedFrom());            // the same version again
            p.LastRunVersion = "0.0.9";
            Assert.Equal("0.0.9", Updater.UpdatedFrom());   // after an update from 0.0.9
            Assert.Null(Updater.UpdatedFrom());            // and only once
        }
        finally { p.LastRunVersion = saved; }
    }

    sealed class Rig : IDisposable
    {
        public readonly NotchModel M = new();
        public readonly ManualClock Clock = new() { Now = 1 };
        public readonly NotchStage Stage;
        public readonly Window Win;
        public Rig(Tab tab = Tab.Home, bool open = true)
        {
            M.Buddy.Hour = () => 14;
            M.Tab = tab;
            Stage = new NotchStage(M, Clock) { Width = 820, Height = 430 };
            Win = new Window { Width = 820, Height = 430, Content = new Grid { Background = new SolidColorBrush(Color.FromRgb(140, 160, 190)), Children = { Stage } } };
            Win.Show();
            Win.SetRenderScaling(2);
            if (open) { Clock.Now = 1.1; M.Expanded = true; }
            Run(2.2);
        }
        public WriteableBitmap Run(double until)
        {
            for (double t = Clock.Now; t <= until + 1e-9; t += 1 / 30.0) { Clock.Now = t; M.TickPeek(t); Stage.Update(t); Dispatcher.UIThread.RunJobs(); }
            Dispatcher.UIThread.RunJobs();
            return Win.CaptureRenderedFrame()!;
        }
        public void Dispose() => Win.Close();
    }

    [AvaloniaFact]
    public void TheUpdatedBubbleSaysTheVersion()
    {
        using var rig = new Rig(open: false);
        rig.M.ShowPeek(Peek.Updated, rig.Clock.Now, 4);
        var bmp = rig.Run(rig.Clock.Now + 1.2);
        bmp.Save(Path.Combine(Out("updates"), "updated-bubble.png"), new PngBitmapEncoderOptions());
        Assert.Equal("Updated to 0.1.0", rig.Stage.Peek.Title);
        Assert.Equal("Notchyyyy keeps itself up to date", rig.Stage.Peek.Detail);
        Assert.False(rig.Stage.Peek.DrankButton.IsVisible);
        rig.Run(rig.Clock.Now + 4);
        Assert.Equal(Peek.None, rig.M.Peeking);           // it tucks itself away after 4 s
    }

    [AvaloniaFact]
    public void TheHeaderChipOffersTheUpdate()
    {
        using var rig = new Rig();
        var chip = rig.Stage.Body.UpdateChip;
        Assert.False(chip.IsVisible);                      // nothing waiting
        chip.Set("0.2.0");
        chip.IsVisible = true;
        var bmp = rig.Run(rig.Clock.Now + 0.3);
        bmp.Save(Path.Combine(Out("updates"), "header-chip.png"), new PngBitmapEncoderOptions());
        Assert.Equal("Update 0.2.0", chip.Title);
        var px = new MediaTests.Px(bmp, 2);
        var c = chip.TranslatePoint(new Point(4, 10), rig.Win)!.Value;
        // the accent at 85 % over the black header, as on the Mac (fully the accent under the pointer)
        var a = LT.AccentColor;
        var expect = Color.FromRgb((byte)(a.R * 0.85), (byte)(a.G * 0.85), (byte)(a.B * 0.85));
        Assert.True(MediaTests.Px.Dist(px.At(c.X, c.Y), expect) < 12, $"chip not in the accent: {px.At(c.X, c.Y)} vs {expect}");
    }

    /// Before Notchyyyy is installed, Settings › About says why Check for Updates can't be used.
    [AvaloniaFact]
    public void CheckForUpdatesExplainsItself()
    {
        Notch.SettingsView.LastPage = SettingsPage.About;
        try
        {
            using var rig = new Rig(Tab.Settings);
            var check = rig.Stage.Body.GetVisualDescendants().OfType<PillButton>().First(b => b.Title == "Check for Updates");
            Assert.False(check.Enabled);
            Assert.Equal("Updates arrive once Notchyyyy is installed", ToolTip.GetTip(check));
        }
        finally { Notch.SettingsView.LastPage = SettingsPage.Appearance; }
    }
}
