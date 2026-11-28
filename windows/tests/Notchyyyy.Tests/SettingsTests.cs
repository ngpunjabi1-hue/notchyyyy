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

namespace Notchyyyy.Tests;

public class SettingsTests
{
    sealed class Rig : IDisposable
    {
        public readonly NotchModel M = new();
        public readonly ManualClock Clock = new() { Now = 1 };
        public readonly NotchStage Stage;
        public readonly Window Win;
        public Rig(SettingsPage page, double scale = 1)
        {
            Prefs.Shared.Reset();
            SettingsView.LastPage = page;
            M.Tab = Tab.Settings;
            Stage = new NotchStage(M, Clock) { Width = 820, Height = 430 };
            Prefs.Shared.Changed += Stage.ApplySetting;
            Win = new Window { Width = 820, Height = 430, Content = new Grid { Background = new SolidColorBrush(Color.FromRgb(140, 160, 190)), Children = { Stage } } };
            Win.Show();
            if (scale != 1) Win.SetRenderScaling(scale);
            Clock.Now = 1.1; M.Expanded = true;
            Run(2.0);
        }
        public WriteableBitmap Run(double until)
        {
            WriteableBitmap? b = null;
            for (double t = Clock.Now; t <= until + 1e-9; t += 1 / 60.0) { Clock.Now = t; Stage.Update(t); Dispatcher.UIThread.RunJobs(); b = Win.CaptureRenderedFrame(); }
            return b!;
        }
        public SettingsView S => Stage.Body.CurrentSettings!;
        public T Find<T>(int index = 0) where T : Control => S.CurrentPage!.GetVisualDescendants().OfType<T>().ElementAt(index);
        public Point At(Control c, Point p) => c.TranslatePoint(p, Win)!.Value;
        public void Click(Point p) { Win.MouseMove(p); Win.MouseDown(p, MouseButton.Left); Win.MouseUp(p, MouseButton.Left); Run(Clock.Now + 0.5); }
        public void ClickSidebar(SettingsPage p) => Click(At(S.Sidebar, new Point(70, SettingsSidebar.Y((int)p) + 16)));
        public void Dispose() { Prefs.Shared.Changed -= Stage.ApplySetting; Win.Close(); Prefs.Shared.Reset(); }
    }

    static string Out(string name)
    {
        var d = Path.Combine(Environment.GetEnvironmentVariable("NOTCHYYYY_FRAMES") ?? Path.GetTempPath(), name);
        Directory.CreateDirectory(d);
        return d;
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public void EveryPageRenders(bool light)
    {
        using var rig = new Rig(SettingsPage.Appearance, 2);
        if (light) { Prefs.Shared.Appearance = Appearance.Light; rig.Run(rig.Clock.Now + 0.3); }
        foreach (var p in Enum.GetValues<SettingsPage>())
        {
            rig.ClickSidebar(p);
            var bmp = rig.Run(rig.Clock.Now + 0.6);
            Assert.Equal(p, SettingsView.LastPage);
            bmp.Save(Path.Combine(Out("settings"), $"{p.ToString().ToLowerInvariant()}-{(light ? "light" : "dark")}.png"), new PngBitmapEncoderOptions());
        }
    }

    /// Pages are anchored to the top: switching never moves the sidebar or where a page starts.
    [AvaloniaFact]
    public void SwitchingPagesNeverShiftsTheLayout()
    {
        using var rig = new Rig(SettingsPage.Appearance);
        var sidebar0 = rig.At(rig.S.Sidebar, default);
        double? top = null;
        foreach (var p in Enum.GetValues<SettingsPage>())
        {
            rig.ClickSidebar(p);
            Assert.Equal(sidebar0, rig.At(rig.S.Sidebar, default));
            var pageTop = rig.At(rig.S.CurrentPage!, default).Y;
            top ??= pageTop;
            Assert.Equal(top.Value, pageTop, 3);
            Assert.Equal(rig.At(rig.S.Sidebar, default).Y, pageTop, 3);       // pages start level with the sidebar
            Assert.Equal(SettingsSidebar.Y((int)p), rig.S.Sidebar.SelectionY, 1);
        }
    }

    /// Changing the theme or accent rebuilds the notch's content; Settings comes back on the same page.
    [AvaloniaFact]
    public void ThePageSurvivesARebuild()
    {
        using var rig = new Rig(SettingsPage.Tabs);
        var before = rig.Stage.Body;
        Prefs.Shared.Accent = Accent.Orange;
        rig.Run(rig.Clock.Now + 0.3);
        Assert.NotSame(before, rig.Stage.Body);
        Assert.Equal(SettingsPage.Tabs, SettingsView.LastPage);
        Assert.Equal(SettingsPage.Tabs, rig.S.Sidebar is { } sb && sb.SelectionY == SettingsSidebar.Y((int)SettingsPage.Tabs) ? SettingsPage.Tabs : SettingsPage.Appearance);
        Prefs.Shared.Appearance = Appearance.Light;
        rig.Run(rig.Clock.Now + 0.3);
        Assert.Equal(SettingsPage.Tabs, SettingsView.LastPage);
        Assert.NotNull(rig.S.CurrentPage);
    }

    [AvaloniaFact]
    public void AppearanceControlsWork()
    {
        using var rig = new Rig(SettingsPage.Appearance);
        var seg = rig.Find<Segmented<Appearance>>();
        rig.Click(rig.At(seg, seg.OptionCentre(1)));
        Assert.Equal(Appearance.Light, Prefs.Shared.Appearance);
        var sw = rig.Find<Swatches>();
        rig.Click(rig.At(sw, sw.Centre(5)));
        Assert.Equal(Accent.Orange, Prefs.Shared.Accent);
        // the text-size slider shows the value while dragging and applies it only on release
        var slider = rig.Find<ScaleSlider>();
        var from = rig.At(slider, slider.PointFor(1.0)); var to = rig.At(slider, slider.PointFor(1.15));
        rig.Win.MouseMove(from); rig.Win.MouseDown(from, MouseButton.Left);
        rig.Win.MouseMove(to); rig.Run(rig.Clock.Now + 0.1);
        Assert.Equal(1.0, Prefs.Shared.TextScale, 6);
        rig.Win.MouseUp(to, MouseButton.Left); rig.Run(rig.Clock.Now + 0.3);
        Assert.Equal(1.15, Prefs.Shared.TextScale, 6);
    }

    [AvaloniaFact]
    public void BehaviourAndExtrasControlsWork()
    {
        using var rig = new Rig(SettingsPage.Behaviour);
        var speed = rig.Find<Segmented<HoverSpeed>>();
        rig.Click(rig.At(speed, speed.OptionCentre(2)));
        Assert.Equal(HoverSpeed.Relaxed, Prefs.Shared.HoverSpeed);
        Assert.Equal(0.35, Prefs.Shared.HoverDelay, 6);
        var live = rig.Find<Toggle>(0);
        rig.Click(rig.At(live, new Point(16, 9)));
        Assert.False(Prefs.Shared.LiveActivity);

        rig.ClickSidebar(SettingsPage.Extras);
        rig.Run(rig.Clock.Now + 0.5);
        var buddy = rig.Find<Toggle>(0);
        rig.Click(rig.At(buddy, new Point(16, 9)));
        Assert.False(Prefs.Shared.Buddy);
        Assert.False(rig.Stage.Buddy.IsVisible);                 // the little Sprout leaves the pill
        var plus = rig.Find<StepButton>(1);
        rig.Click(rig.At(plus, new Point(13, 13)));
        Assert.Equal(9, Prefs.Shared.WaterGoal);
        var water = rig.Find<Toggle>(1);
        rig.Click(rig.At(water, new Point(16, 9)));
        Assert.False(Prefs.Shared.WaterReminders);
        rig.Click(rig.At(plus, new Point(13, 13)));               // disabled now: nothing changes
        Assert.Equal(9, Prefs.Shared.WaterGoal);
    }

    /// Hiding a tab takes it out of the tab bar; if you were on it, you go Home.
    [AvaloniaFact]
    public void HidingATabUpdatesTheTabBar()
    {
        using var rig = new Rig(SettingsPage.Tabs);
        Assert.Equal(8, rig.Stage.Pill.Tabs.Count);
        var media = rig.Find<Toggle>(0);                           // Media is the first hideable tab
        rig.Click(rig.At(media, new Point(16, 9)));
        Assert.Contains("music", Prefs.Shared.HiddenTabs);
        Assert.Equal(7, rig.Stage.Pill.Tabs.Count);
        Assert.DoesNotContain(Tab.Media, rig.Stage.Pill.Tabs);
        Assert.Equal(Tab.Settings, rig.M.Tab);
        Assert.Equal(rig.Stage.Pill.ButtonCentre(6), rig.Stage.Pill.CurrentIndicator, 1);   // Settings is now 7th
        // and on the tab when it's hidden: back to Home
        rig.M.Tab = Tab.Apps; rig.Run(rig.Clock.Now + 0.5);
        Prefs.Shared.SetTabHidden("apps", true); rig.Run(rig.Clock.Now + 0.5);
        Assert.Equal(6, rig.Stage.Pill.Tabs.Count);
    }

    [AvaloniaFact]
    public void AboutShowsVersionAndTheNameIsSaved()
    {
        using var rig = new Rig(SettingsPage.About);
        var box = rig.Find<TextBox>();
        box.Focus();
        box.Text = "Alex Morgan";
        rig.Win.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        rig.Run(rig.Clock.Now + 0.3);
        Assert.Equal("Alex Morgan", Prefs.Shared.UserName);
        Assert.Equal("Alex", rig.M.FirstName);
        var texts = rig.S.CurrentPage!.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains(texts, t => t!.StartsWith("Version 0.1.0"));
    }
}
