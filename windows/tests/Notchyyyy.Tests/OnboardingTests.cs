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
using Notchyyyy.Theme;
using Xunit;
using Px = Notchyyyy.Tests.MediaTests.Px;

namespace Notchyyyy.Tests;

/// Step 11: the first-run setup — name, look, tabs, water, done.
public class OnboardingTests
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
        public Rig(bool light = false, SetupStep step = SetupStep.Name)
        {
            Prefs.Shared.Appearance = light ? Appearance.Light : Appearance.Dark;
            Prefs.Shared.Onboarded = false;
            OnboardingView.Saved = step;
            M.Buddy.Hour = () => 14;
            M.UserName = "";
            M.Pinned = true;
            Stage = new NotchStage(M, Clock) { Width = 820, Height = 430 };
            Win = new Window { Width = 820, Height = 430, Content = new Grid { Background = new SolidColorBrush(Color.FromRgb(140, 160, 190)), Children = { Stage } } };
            Win.Show();
            Win.SetRenderScaling(2);
            Prefs.Shared.Changed += OnPref;
            Clock.Now = 1.1; M.Expanded = true;
            Run(2.4);
        }
        // the shell's wiring: a theme or accent change rebuilds the body
        void OnPref(string name) => Stage.ApplySetting(name);
        public WriteableBitmap Run(double until)
        {
            for (double t = Clock.Now; t <= until + 1e-9; t += 1 / 30.0) { Clock.Now = t; Stage.Update(t); Dispatcher.UIThread.RunJobs(); }
            return Frame();
        }
        public WriteableBitmap Frame() { Dispatcher.UIThread.RunJobs(); return Win.CaptureRenderedFrame()!; }
        public OnboardingView Setup => Stage.Body.CurrentSetup!;
        public Point Centre(Control c, double dx = 0, double dy = 0) => c.TranslatePoint(new Point(c.Bounds.Width / 2 + dx, c.Bounds.Height / 2 + dy), Win)!.Value;
        public void Click(Point p) { Win.MouseMove(p, RawInputModifiers.None); Win.MouseDown(p, MouseButton.Left); Win.MouseUp(p, MouseButton.Left); Dispatcher.UIThread.RunJobs(); }
        public void Continue() { Setup.Primary.Press(); Run(Clock.Now + 0.8); }
        public void Dispose()
        {
            Prefs.Shared.Changed -= OnPref;
            Win.Close();
            var p = Prefs.Shared;
            p.Appearance = Appearance.Dark; p.Accent = Accent.Indigo; p.Onboarded = true; p.HiddenTabs = new(); p.WaterReminders = true; p.WaterGoal = 8;
            OnboardingView.Saved = SetupStep.Name;
        }
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public void SetupOpensAndAsksYourName(bool light)
    {
        using var rig = new Rig(light);
        var bmp = rig.Frame();
        bmp.Save(Path.Combine(Out("setup"), $"1-name-{(light ? "light" : "dark")}.png"), new PngBitmapEncoderOptions());
        Assert.NotNull(rig.Stage.Body.CurrentSetup);
        Assert.Equal(0, rig.Stage.PillOpacity, 3);                 // the tab bar waits until setup is finished
        Assert.Equal("Welcome", rig.Stage.Body.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text is "Welcome" or "Home").Text);
        Assert.False(rig.Setup.Primary.IsOn);                      // a name first
        rig.Setup.Primary.Press();
        Assert.Equal(SetupStep.Name, rig.Setup.Step);
    }

    /// Typing a name enables Continue; Enter goes on; the name is kept and greets you at the end.
    [AvaloniaFact]
    public void TheWholeSetup()
    {
        using var rig = new Rig();
        rig.Click(rig.Centre(rig.Setup.NameBox));
        rig.Win.KeyTextInput("Robin Hale");
        Dispatcher.UIThread.RunJobs();
        Assert.True(rig.Setup.Primary.IsOn);
        Assert.Equal(Notchyyyy.Sprout.SproutMood.Happy, rig.Setup.Mood);
        rig.Win.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        rig.Run(rig.Clock.Now + 0.8);
        Assert.Equal(SetupStep.Look, rig.Setup.Step);
        Assert.Equal("Robin Hale", rig.M.UserName);
        rig.Frame().Save(Path.Combine(Out("setup"), "2-look.png"), new PngBitmapEncoderOptions());

        // Look: the light card; the body rebuilds in the new theme and setup stays on this page
        var light = rig.Setup.CurrentPage.GetVisualDescendants().OfType<ThemeCard>().Last();
        rig.Click(rig.Centre(light, 0, -10));
        rig.Run(rig.Clock.Now + 0.6);
        Assert.Equal(Appearance.Light, Prefs.Shared.Appearance);
        Assert.Equal(SetupStep.Look, rig.Setup.Step);
        rig.Frame().Save(Path.Combine(Out("setup"), "2-look-light.png"), new PngBitmapEncoderOptions());

        // Tabs: hide Stopwatch
        rig.Continue();
        Assert.Equal(SetupStep.Tabs, rig.Setup.Step);
        var stopwatch = rig.Setup.CurrentPage.GetVisualDescendants().OfType<TabChoice>().First(c => c.Tab == Tab.Stopwatch);
        rig.Click(rig.Centre(stopwatch));
        Assert.Contains(Tab.Stopwatch.Key(), Prefs.Shared.HiddenTabs);
        rig.Run(rig.Clock.Now + 0.4);
        rig.Frame().Save(Path.Combine(Out("setup"), "3-tabs.png"), new PngBitmapEncoderOptions());

        // Water: Sprout is thirsty while reminders are on; the goal goes up
        rig.Continue();
        Assert.Equal(SetupStep.Water, rig.Setup.Step);
        Assert.Equal(Notchyyyy.Sprout.SproutMood.Thirsty, rig.Setup.Mood);
        var plus = rig.Setup.CurrentPage.GetVisualDescendants().OfType<StepButton>().Last();
        rig.Click(rig.Centre(plus));
        Assert.Equal(9, Prefs.Shared.WaterGoal);
        rig.Frame().Save(Path.Combine(Out("setup"), "4-water.png"), new PngBitmapEncoderOptions());

        // Back, and forward again
        rig.Click(rig.Centre(rig.Setup.Back));
        rig.Run(rig.Clock.Now + 0.8);
        Assert.Equal(SetupStep.Tabs, rig.Setup.Step);
        rig.Continue(); rig.Continue();
        Assert.Equal(SetupStep.Done, rig.Setup.Step);
        Assert.Contains(rig.Setup.CurrentPage.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "You're all set, Robin");
        Assert.Equal("Start using Notchyyyy", rig.Setup.Primary.Title);
        rig.Frame().Save(Path.Combine(Out("setup"), "5-done.png"), new PngBitmapEncoderOptions());

        // Start: setup is over, Home and the tab bar arrive, the notch no longer stays pinned open
        rig.Click(rig.Centre(rig.Setup.Primary));
        Assert.True(rig.M.Onboarded);
        Assert.False(rig.M.Pinned);
        rig.Run(rig.Clock.Now + 1.0);
        Assert.NotNull(rig.Stage.Body.CurrentHome);
        Assert.Null(rig.Stage.Body.CurrentSetup);
        Assert.Equal(1, rig.Stage.PillOpacity, 2);
        Assert.DoesNotContain(Tab.Stopwatch, rig.M.VisibleTabs);
        rig.Frame().Save(Path.Combine(Out("setup"), "6-home.png"), new PngBitmapEncoderOptions());
    }

    /// Run setup again (Settings › About) brings it back from the start, and hides the tab bar while it runs.
    [AvaloniaFact]
    public void SetupCanRunAgain()
    {
        using var rig = new Rig(step: SetupStep.Done);
        rig.Setup.Primary.Press();
        rig.Run(rig.Clock.Now + 1.0);
        Assert.True(rig.M.Onboarded);
        rig.M.Tab = Tab.Settings;
        rig.Run(rig.Clock.Now + 0.6);
        Notch.SettingsView.LastPage = SettingsPage.About;
        rig.M.Tab = Tab.Home; rig.M.Tab = Tab.Settings;
        rig.Run(rig.Clock.Now + 0.6);
        var again = rig.Stage.Body.GetVisualDescendants().OfType<PillButton>().First(b => b.Title == "Run setup again");
        Assert.True(again.Enabled);
        rig.Click(rig.Centre(again));
        rig.Run(rig.Clock.Now + 1.0);
        Assert.False(rig.M.Onboarded);
        Assert.Equal(SetupStep.Name, rig.Setup.Step);
        Assert.Equal(0, rig.Stage.PillOpacity, 2);
        Notch.SettingsView.LastPage = SettingsPage.Appearance;
    }

    /// Frame by frame at 120 Hz: moving to the next page (a slide and fade) and finishing (setup → Home, the tab bar
    /// arriving). No pops.
    [AvaloniaTheory]
    [InlineData("next")] [InlineData("finish")]
    public void SetupMovesWithoutPops(string what)
    {
        using var rig = new Rig(step: what == "next" ? SetupStep.Look : SetupStep.Done);
        rig.M.UserName = "Robin";
        if (what == "next") rig.Setup.Primary.Press(); else rig.Setup.Primary.Press();
        MediaTests.Px? prev = null;
        var jumps = new List<double>();
        for (int i = 0; i < 96; i++)
        {
            rig.Clock.Now += 1 / 120.0;
            rig.Stage.Update(rig.Clock.Now);
            var px = new Px(rig.Frame(), 2);
            if (prev is not null)
            {
                int changed = 0, n = 0;
                // the right-hand column (Sprout on the left keeps moving: its idle animation)
                for (double y = 46; y < 360; y += 3) for (double x = 330; x < 760; x += 3) { n++; if (Px.Dist(px.At(x, y), prev.At(x, y)) > 40) changed++; }
                jumps.Add((double)changed / n);
            }
            prev = px;
        }
        File.WriteAllText(Path.Combine(Out("setup-motion"), what + ".txt"), string.Join("\n", jumps.Select(j => j.ToString("0.0000"))));
        Assert.True(jumps.Take(40).Sum() > 0.05, "nothing moved");
        for (int i = 0; i < jumps.Count; i++)
        {
            double around = Math.Max(i > 0 ? jumps[i - 1] : 0, i + 1 < jumps.Count ? jumps[i + 1] : 0);
            Assert.False(jumps[i] > 0.02 && jumps[i] > 2.5 * around, $"a pop at frame {i + 2}: {jumps[i]:P1} (around {around:P1})");
        }
    }
}
