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
using Xunit;

namespace Notchyyyy.Tests;

public class HomeTests
{
    static string Out(string name)
    {
        var d = Path.Combine(Environment.GetEnvironmentVariable("NOTCHYYYY_FRAMES") ?? Path.GetTempPath(), name);
        Directory.CreateDirectory(d);
        return d;
    }

    sealed class Rig : IDisposable
    {
        public readonly NotchModel M = new();
        public readonly ManualClock Clock = new() { Now = 1 };
        public readonly NotchStage Stage;
        public readonly Window Win;
        public Rig(string name, bool light = false, int hour = 14, double scale = 2)
        {
            Prefs.Shared.Appearance = light ? Appearance.Light : Appearance.Dark;
            M.UserName = name;
            M.Buddy.Hour = () => hour;
            Stage = new NotchStage(M, Clock) { Width = 820, Height = 430 };
            Win = new Window { Width = 820, Height = 430, Content = new Grid { Background = new SolidColorBrush(Color.FromRgb(140, 160, 190)), Children = { Stage } } };
            Win.Show();
            if (scale != 1) Win.SetRenderScaling(scale);
            Clock.Now = 1.1; M.Expanded = true;
        }
        public WriteableBitmap Run(double until)
        {
            WriteableBitmap? b = null;
            for (double t = Clock.Now; t <= until; t += 1 / 30.0)
            {
                Clock.Now = t; Stage.Update(t);
                if (Stage.Body.CurrentHome is { } h) h.Sprout.InvalidateVisual();
                Dispatcher.UIThread.RunJobs();
                b = Win.CaptureRenderedFrame();
            }
            return b!;
        }
        public HomeView Home => Stage.Body.CurrentHome!;
        public void Dispose() { Win.Close(); Prefs.Shared.Appearance = Appearance.Dark; }
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public void HomeLooksRight(bool light)
    {
        using var rig = new Rig("Alex Morgan", light);
        var bmp = rig.Run(2.5);
        bmp.Save(Path.Combine(Out("home"), $"home-{(light ? "light" : "dark")}.png"), new PngBitmapEncoderOptions());
        Assert.NotNull(rig.Stage.Body.CurrentHome);
        Assert.Equal("Good afternoon, Alex", GreetingText(rig.Home));
    }

    static string GreetingText(HomeView h) => h.GetVisualDescendants().OfType<TextBlock>().First(t => t.FontSize > 15 || t.Text!.StartsWith("Good") || t.Text.StartsWith("Hello")).Text!;

    /// The greeting stays on one line and always shows the whole first name: a long one shrinks the text a little.
    [AvaloniaFact]
    public void LongNameShrinksButIsNeverCut()
    {
        using var rig = new Rig("Maximiliana-Alexandrina Smith");
        var bmp = rig.Run(2.0);
        bmp.Save(Path.Combine(Out("home"), "home-long-name.png"), new PngBitmapEncoderOptions());
        var tb = rig.Home.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text!.StartsWith("Good"));
        Assert.Equal("Good afternoon, Maximiliana-Alexandrina", tb.Text);
        Assert.True(tb.FontSize < 19, "a long greeting should shrink");
        Assert.True(tb.FontSize >= 19 * 0.7 - 0.01, "never below 70 %");
        // drawn on one line, and in full: the whole text at its size fits the width it was given
        Assert.True(tb.DesiredSize.Height < 30, "wrapped onto two lines");
        var full = new FormattedText(tb.Text!, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                                     new Typeface(LT.Ui, FontStyle.Normal, FontWeight.SemiBold), tb.FontSize, null);
        Assert.True(full.Width <= tb.Bounds.Width + 0.5, $"cut off: needs {full.Width:0.0} pt, has {tb.Bounds.Width:0.0}");
    }

    [Theory]
    [InlineData(5, "Good morning")] [InlineData(11, "Good morning")] [InlineData(12, "Good afternoon")]
    [InlineData(16, "Good afternoon")] [InlineData(17, "Good evening")] [InlineData(21, "Good evening")]
    [InlineData(22, "Hello, night owl")] [InlineData(3, "Hello, night owl")]
    public void GreetingFollowsTheClock(int hour, string expect) => Assert.Equal(expect, HomeView.Greeting(hour));

    /// The big Sprout's design line y = 76 lands exactly on the open body's bottom edge, as on the Mac.
    [AvaloniaFact]
    public void SproutRisesFromTheBottomEdge()
    {
        using var rig = new Rig("Alex");
        rig.Run(2.0);
        var s = rig.Home.Sprout;
        double canvas = 300 * 1.3, k = canvas * 0.77 / 100, rectTop = (300 - canvas) / 2 + canvas * 0.115;
        double lineInBody = HomeView.ContentOrigin.Y + Canvas.GetTop(s) + rectTop + 76 * k;
        Assert.Equal(300, lineInBody, 3);
        double centreX = HomeView.ContentOrigin.X + Canvas.GetLeft(s) + 150;
        Assert.Equal(14 + 24 + 103, centreX, 3);                     // centred in its 206 pt column
    }

    /// Poking the big Sprout: giggle, love, startled, annoyed, then dizzy on the fifth quick tap.
    [AvaloniaFact]
    public void PokingEscalatesToDizzy()
    {
        using var rig = new Rig("Alex", scale: 1);
        rig.Run(2.0);
        var s = rig.Home.Sprout;
        var origin = s.TranslatePoint(new Point(150, 150), rig.Win)!.Value;
        var expect = new[] { SproutMood.Cheer, SproutMood.Love, SproutMood.Surprised, SproutMood.Hurt, SproutMood.Dizzy };
        foreach (var e in expect)
        {
            rig.Win.MouseMove(origin);
            rig.Win.MouseDown(origin, MouseButton.Left);
            rig.Win.MouseUp(origin, MouseButton.Left);
            rig.Run(rig.Clock.Now + 0.4);
            Assert.Equal(e, s.Showing);
        }
        rig.Win.MouseMove(new Point(700, 420));                          // off Sprout
        rig.Run(rig.Clock.Now + 4.0);
        Assert.Equal(SproutMood.Idle, s.Showing);                         // recovered
    }

    /// Water: the count goes up and the little Sprout in the pill cheers.
    [AvaloniaFact]
    public void WaterLogsAGlassAndSproutCheers()
    {
        using var rig = new Rig("Alex", scale: 1);
        rig.Run(2.0);
        var water = rig.Home.GetVisualDescendants().OfType<QuickAction>().Last();
        var p = water.TranslatePoint(new Point(water.Bounds.Width / 2, 14), rig.Win)!.Value;
        rig.Win.MouseMove(p);
        rig.Win.MouseDown(p, MouseButton.Left);
        rig.Win.MouseUp(p, MouseButton.Left);
        Assert.Equal(1, rig.M.Glasses);
        Assert.Equal("1", water.Title);
        Assert.Equal(SproutMood.Cheer, rig.M.BuddyMood(rig.Clock.Now));
    }

    /// The glance rows open their tabs.
    [AvaloniaFact]
    public void GlanceRowsOpenTheirTabs()
    {
        using var rig = new Rig("Alex", scale: 1);
        rig.Run(2.0);
        var rows = rig.Home.GetVisualDescendants().OfType<GlanceRow>().ToList();
        var expect = new[] { Tab.Media, Tab.Today, Tab.Clipboard };
        for (int i = 0; i < 3; i++)
        {
            if (rig.M.Tab != Tab.Home) { rig.M.Tab = Tab.Home; rig.Run(rig.Clock.Now + 0.6); rows = rig.Home.GetVisualDescendants().OfType<GlanceRow>().ToList(); }
            var p = rows[i].TranslatePoint(new Point(200, 23), rig.Win)!.Value;
            rig.Win.MouseMove(p);
            rig.Win.MouseDown(p, MouseButton.Left);
            rig.Win.MouseUp(p, MouseButton.Left);
            Assert.Equal(expect[i], rig.M.Tab);
        }
    }
}

public class HomeLifetimeTests
{
    /// Switching away from Home stops its refresh timer (no timers pile up over tab switches).
    [AvaloniaFact]
    public void LeavingHomeStopsItsTimer()
    {
        var m = new NotchModel(); var clock = new ManualClock { Now = 1 };
        var stage = new NotchStage(m, clock) { Width = 820, Height = 430 };
        var win = new Window { Width = 820, Height = 430, Content = stage };
        win.Show();
        clock.Now = 1.1; m.Expanded = true;
        for (double t = 1.1; t < 2; t += 0.05) { clock.Now = t; stage.Update(t); Dispatcher.UIThread.RunJobs(); }
        var home = stage.Body.CurrentHome!;
        Assert.True(home.Refreshing);
        m.Tab = Tab.Apps;
        for (double t = 2; t < 3; t += 0.05) { clock.Now = t; stage.Update(t); Dispatcher.UIThread.RunJobs(); }
        Assert.Null(stage.Body.CurrentHome);
        Assert.False(home.Refreshing);
        win.Close();
    }
}

public class HomeReuseTests
{
    /// Home is built once: going back to it (even halfway through it fading out) brings back the same view,
    /// never a second copy, and it ends fully visible.
    [AvaloniaFact]
    public void SwitchingBackMidFadeReusesHome()
    {
        var m = new NotchModel(); var clock = new ManualClock { Now = 1 };
        var stage = new NotchStage(m, clock) { Width = 820, Height = 430 };
        var win = new Window { Width = 820, Height = 430, Content = stage };
        win.Show();
        void Run(double until) { for (double t = clock.Now; t <= until; t += 1 / 60.0) { clock.Now = t; stage.Update(t); Dispatcher.UIThread.RunJobs(); } }
        clock.Now = 1.1; m.Expanded = true;
        Run(2.0);
        var home = stage.Body.CurrentHome!;
        clock.Now = 2.0; m.Tab = Tab.Media;
        Run(2.05);                                            // Home is half faded out
        clock.Now = 2.05; m.Tab = Tab.Home;
        Run(3.0);
        Assert.Same(home, stage.Body.CurrentHome);
        var swap = (Panel)home.Parent!;
        Assert.Equal(1, swap.Children.Count(c => c is HomeView));
        Assert.Equal(1.0, home.Opacity, 3);
        Assert.Null(home.Effect);
        // and once more after it had fully left
        clock.Now = 3.0; m.Tab = Tab.Apps; Run(4.0);
        Assert.Null(stage.Body.CurrentHome);
        clock.Now = 4.0; m.Tab = Tab.Home; Run(5.0);
        Assert.Same(home, stage.Body.CurrentHome);
        Assert.Equal(1.0, home.Opacity, 3);
        Assert.True(home.Refreshing);
        win.Close();
    }

    /// A name changed in Settings shows on Home straight away when you go back to it.
    [AvaloniaFact]
    public void ANewNameShowsWhenYouReturnHome()
    {
        var m = new NotchModel { UserName = "Alex" }; var clock = new ManualClock { Now = 1 };
        m.Buddy.Hour = () => 9;                     // before Home is built
        var stage = new NotchStage(m, clock) { Width = 820, Height = 430 };
        var win = new Window { Width = 820, Height = 430, Content = stage };
        win.Show();
        void Run(double until) { for (double t = clock.Now; t <= until; t += 1 / 60.0) { clock.Now = t; stage.Update(t); Dispatcher.UIThread.RunJobs(); } }
        clock.Now = 1.1; m.Expanded = true; Run(2.0);
        string Greeting() => stage.Body.CurrentHome!.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text!.StartsWith("Good")).Text!;
        Assert.Equal("Good morning, Alex", Greeting());
        clock.Now = 2.0; m.Tab = Tab.Settings; Run(3.0);
        m.UserName = "Robin";
        clock.Now = 3.0; m.Tab = Tab.Home; Run(4.0);
        Assert.Equal("Good morning, Robin", Greeting());
        win.Close();
    }
}
