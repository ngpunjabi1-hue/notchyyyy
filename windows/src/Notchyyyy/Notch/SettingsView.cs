using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Notchyyyy.Motion;
using Notchyyyy.Sprout;
using Notchyyyy.Theme;

namespace Notchyyyy.Notch;

public enum SettingsPage { Appearance, Behaviour, Extras, Tabs, About }

/// <summary>
/// Settings, as on the Mac: a quiet 140 pt sidebar and one page at a time, so each has room to breathe. Pages are
/// anchored to the top (switching never shifts anything) and swap with the blur cross-fade. The page is remembered
/// across rebuilds: changing a setting rebuilds the notch, and that must not send you back to the first page.
/// </summary>
public sealed class SettingsView : Grid
{
    public static SettingsPage LastPage { get; set; } = Enum.TryParse<SettingsPage>(Debug.Value("--page"), true, out var p) ? p : SettingsPage.Appearance;

    private readonly NotchModel _m;
    private readonly IClock _clock;
    private readonly BlurSwap _pages = new() { VerticalAlignment = VerticalAlignment.Top };
    public SettingsSidebar Sidebar { get; }
    public Control? CurrentPage => _pages.Current;

    public SettingsView(NotchModel m, IClock clock)
    {
        _m = m; _clock = clock;
        ColumnDefinitions = new ColumnDefinitions("140,28,*");
        Sidebar = new SettingsSidebar(clock, LastPage, Show) { VerticalAlignment = VerticalAlignment.Top };
        Children.Add(Sidebar);
        SetColumn(_pages, 2);
        Children.Add(_pages);
        _pages.Show(Page(LastPage), clock.Now, animate: false);
    }

    public void Show(SettingsPage page)
    {
        if (page == LastPage && _pages.Current is not null) return;
        LastPage = page;
        _pages.Show(Page(page), _clock.Now, animate: true);
        Sidebar.Select(page);
        Kick();
    }

    // the page cross-fade runs on the stage's frames; ask for them
    private void Kick() { for (Visual? v = this; v is not null; v = v.GetVisualParent()) if (v is NotchStage s) { s.Kick(); return; } }
    public bool Animating(double now) => _pages.Animating(now);
    public void Update(double now) => _pages.Update(now);

    private Control Page(SettingsPage p) => p switch
    {
        SettingsPage.Appearance => Appearance(),
        SettingsPage.Behaviour => Behaviour(),
        SettingsPage.Extras => Extras(),
        SettingsPage.Tabs => Tabs(),
        _ => About(),
    };

    // ---- building blocks ----

    private static TextBlock Label(string s, double size, Color c, FontWeight w = FontWeight.Normal) => new()
    {
        Text = s, FontFamily = LT.Ui, FontSize = LT.TextSize(size), FontWeight = w, Foreground = new SolidColorBrush(c),
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    /// A row: the label (and a line of detail) on the left, the control on the right; at least 50 pt tall.
    private static Control Row(string label, string? detail, Control control, bool enabled = true)
    {
        var words = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { Label(label, 12.5, LT.Text) } };
        if (detail is not null) words.Children.Add(Label(detail, 10.5, LT.Faint));
        control.VerticalAlignment = VerticalAlignment.Center;
        var row = new DockPanel { MinHeight = 50, LastChildFill = true, IsEnabled = enabled, Opacity = enabled ? 1 : 0.4 };
        DockPanel.SetDock(control, Dock.Right);
        control.Margin = new Thickness(12, 0, 0, 0);
        row.Children.Add(control);
        row.Children.Add(words);
        return row;
    }

    private static Control Divider() => new Border { Height = 1, Background = new SolidColorBrush(LT.Border) };

    private static StackPanel Rows(params Control[] items)
    {
        var s = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        for (int i = 0; i < items.Length; i++) { if (i > 0) s.Children.Add(Divider()); s.Children.Add(items[i]); }
        return s;
    }

    private static Prefs P => Prefs.Shared;

    // ---- pages ----

    private Control Appearance() => Rows(
        Row("Theme", "The closed notch always stays black",
            new Segmented<global::Notchyyyy.Theme.Appearance>(_clock, new[] { global::Notchyyyy.Theme.Appearance.Dark, global::Notchyyyy.Theme.Appearance.Light }, a => a == global::Notchyyyy.Theme.Appearance.Dark ? "Dark" : "Light",
                                      () => P.Appearance, a => P.Appearance = a)),
        Row("Accent colour", null, new Swatches(_clock, a => P.Accent = a)),
        Row("Text size", null, new ScaleSlider(_clock, 0.85, 1.2, 9, 14, true, () => P.TextScale, v => P.TextScale = v)),
        Row("Icon size", null, new ScaleSlider(_clock, 0.8, 1.3, 8, 13, false, () => P.IconScale, v => P.IconScale = v)));

    private Control Behaviour() => Rows(
        Row("Open on hover", "How quickly the notch opens",
            new Segmented<HoverSpeed>(_clock, Enum.GetValues<HoverSpeed>(), h => h.ToString(), () => P.HoverSpeed, h => P.HoverSpeed = h)),
        Row("Live activity", "Media and timers in the closed pill", new Toggle(_clock, () => P.LiveActivity, v => P.LiveActivity = v)),
        Row("Launch at login", "Start Notchyyyy when you sign in to Windows", new Toggle(_clock, () => Platform.Startup.IsOn, Platform.Startup.Set)));

    private Control Extras()
    {
        bool water = P.WaterReminders;
        var goal = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var goalText = new TextBlock
        {
            Text = $"{P.WaterGoal} glasses", Width = 74, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            FontFamily = LT.Ui, FontSize = LT.TextSize(12), FontWeight = FontWeight.Medium, Foreground = new SolidColorBrush(LT.Text),
        };
        goal.Children.Add(new StepButton(_clock, IconKind.Minus, () => { P.WaterGoal -= 1; goalText.Text = $"{P.WaterGoal} glasses"; }));
        goal.Children.Add(goalText);
        goal.Children.Add(new StepButton(_clock, IconKind.Plus, () => { P.WaterGoal += 1; goalText.Text = $"{P.WaterGoal} glasses"; }));
        Control interval = Row("Remind me every", null,
            new Segmented<WaterInterval>(_clock, Enum.GetValues<WaterInterval>(),
                i => (int)i < 60 ? $"{(int)i}m" : (int)i == 60 ? "1h" : "1.5h", () => P.WaterInterval, i => P.WaterInterval = i), water);
        Control goalRow = Row("Daily goal", null, goal, water);
        var toggle = new Toggle(_clock, () => P.WaterReminders, v =>
        {
            P.WaterReminders = v;
            foreach (var r in new[] { interval, goalRow }) { r.IsEnabled = v; r.Opacity = v ? 1 : 0.4; }
        });
        return Rows(
            Row("Sprout in the notch", "Keeps you company in the closed pill", new Toggle(_clock, () => P.Buddy, v => P.Buddy = v)),
            Row("Water reminders", "Between 8 am and 10 pm, when the notch is closed", toggle),
            interval, goalRow);
    }

    private Control Tabs()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,10,*,10,*"), RowDefinitions = new RowDefinitions("38,8,38") };
        int i = 0;
        foreach (var t in Enum.GetValues<Tab>().Where(t => t.Hideable()))
        {
            var card = new Border
            {
                CornerRadius = new CornerRadius(9), Background = new SolidColorBrush(LT.Surface),
                BorderBrush = new SolidColorBrush(LT.Border), BorderThickness = new Thickness(1), Padding = new Thickness(11, 0, 8, 0),
            };
            var row = new DockPanel { LastChildFill = true };
            var sw = new Toggle(_clock, () => !P.HiddenTabs.Contains(t.Key()), show =>
            {
                P.SetTabHidden(t.Key(), !show);
                if (!show && _m.Tab == t) _m.Tab = Tab.Home;
            });
            DockPanel.SetDock(sw, Dock.Right);
            row.Children.Add(sw);
            row.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center,
                Children = { new IconView(t.Icon(), 15, LT.Muted) { Width = 16, Height = 16 }, Label(t.Title(), 12, LT.Text) },
            });
            card.Child = row;
            SetColumn(card, (i % 3) * 2); SetRow(card, (i / 3) * 2);
            grid.Children.Add(card);
            i++;
        }
        return new StackPanel { Spacing = 10, VerticalAlignment = VerticalAlignment.Top, Children = { Label("Choose what shows in the tab bar", 11, LT.Faint), grid } };
    }

    private Control About()
    {
        var name = new TextBox
        {
            Text = _m.UserName, PlaceholderText = "Name", Width = 180, Height = 28, MinHeight = 28, FontFamily = LT.Ui, FontSize = LT.TextSize(12.5),
            Foreground = new SolidColorBrush(LT.Text), Background = new SolidColorBrush(LT.Surface), BorderBrush = new SolidColorBrush(LT.Border),
            CornerRadius = new CornerRadius(7), Padding = new Thickness(10, 5), TextAlignment = TextAlignment.Right,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        Fields.Quiet(name);
        void Commit() { var n = (name.Text ?? "").Trim(); if (n.Length > 0) { _m.UserName = n; P.UserName = n; } }
        name.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
        name.LostFocus += (_, _) => Commit();
        var nameRow = new DockPanel { LastChildFill = true, Children = { } };
        DockPanel.SetDock(name, Dock.Right);
        nameRow.Children.Add(name);
        nameRow.Children.Add(new Border { VerticalAlignment = VerticalAlignment.Center, Child = Label("Your name", 12.5, LT.Text) });

        var version = typeof(SettingsView).Assembly.GetName().Version;
        var identity = new DockPanel { LastChildFill = true };
        var check = new PillButton(_clock, "Check for Updates", IconKind.Download, null) { Enabled = false };
        ToolTip.SetTip(check, "Updates arrive in step 12");
        DockPanel.SetDock(check, Dock.Right);
        identity.Children.Add(check);
        identity.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 14,
            Children =
            {
                new SproutView(_clock, 48) { Fps = 30, IsHitTestVisible = false },
                new StackPanel
                {
                    Spacing = 3, VerticalAlignment = VerticalAlignment.Center,
                    Children = { Label("Notchyyyy", 15, LT.Text, FontWeight.SemiBold), Label($"Version {version?.ToString(3)} for Windows", 11.5, LT.Muted) },
                },
            },
        });

        var auto = Row("Update automatically", "New versions install by themselves while the notch is idle",
                       new Toggle(_clock, () => P.AutoUpdate, v => P.AutoUpdate = v));
        auto.MinHeight = 0;
        var setup = new PillButton(_clock, "Run setup again", IconKind.Sparkle, () =>
        {
            // back to the start of setup; the notch stays open until it's finished
            OnboardingView.Saved = SetupStep.Name;
            _m.Pinned = true;
            _m.Tab = Tab.Home;
            _m.Onboarded = false;
        });
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10,
            Children =
            {
                setup,
                new PillButton(_clock, "Reset settings", IconKind.Reset, () => P.Reset()),
                new PillButton(_clock, "Quit Notchyyyy", IconKind.Power, () =>
                    (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown()),
            },
        };
        return new StackPanel
        {
            Spacing = 14, VerticalAlignment = VerticalAlignment.Top,
            Children =
            {
                nameRow, Divider(), identity, auto,
                Label("Esc closes the notch · Ctrl+1–Ctrl+9 switch tabs", 11.5, LT.Faint),
                buttons,
            },
        };
    }
}

/// The settings sidebar: five rows; the chosen one sits on a soft tile that slides between them.
public sealed class SettingsSidebar : SpringControl
{
    public const double ItemHeight = 32, Gap = 4;
    private readonly Action<SettingsPage> _choose;
    private readonly SpringValue _y;
    private readonly SpringValue[] _hover;
    private int _hot = -1;
    private SettingsPage _sel;
    private static readonly SettingsPage[] Pages = Enum.GetValues<SettingsPage>();

    public SettingsSidebar(IClock clock, SettingsPage selected, Action<SettingsPage> choose) : base(clock)
    {
        _choose = choose; _sel = selected;
        _y = Spring(Y((int)selected), 0.01);
        _hover = Pages.Select(_ => Spring(0)).ToArray();
        Width = 140; Height = Pages.Length * (ItemHeight + Gap) - Gap;
        Cursor = new Cursor(StandardCursorType.Hand);
    }
    public static double Y(int i) => i * (ItemHeight + Gap);
    public double SelectionY => _y.Value(Now);

    public void Select(SettingsPage p) { _sel = p; Go(_y, Y((int)p), LT.Snappy); }

    private static IconKind Icon(SettingsPage p) => p switch
    {
        SettingsPage.Appearance => IconKind.Brush, SettingsPage.Behaviour => IconKind.Pointer, SettingsPage.Extras => IconKind.Drop,
        SettingsPage.Tabs => IconKind.Grid, _ => IconKind.Info,
    };

    private int At(Point p) { int i = (int)(p.Y / (ItemHeight + Gap)); return i >= 0 && i < Pages.Length && p.Y - Y(i) <= ItemHeight ? i : -1; }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        int i = At(e.GetPosition(this));
        if (i == _hot) return;
        if (_hot >= 0) Go(_hover[_hot], 0, LT.Hover);
        _hot = i;
        if (i >= 0) Go(_hover[i], 1, LT.Hover);
    }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); if (_hot >= 0) Go(_hover[_hot], 0, LT.Hover); _hot = -1; }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        int i = At(e.GetPosition(this));
        if (i >= 0) _choose(Pages[i]);
    }

    public override void Render(DrawingContext ctx)
    {
        for (int i = 0; i < Pages.Length; i++)
        {
            double h = V(_hover[i].Value(Now));
            if (Pages[i] != _sel && h > 0.01)
                ctx.DrawRectangle(new SolidColorBrush(LT.Wash.WithOpacity(0.03 * h)), null, new Rect(0, Y(i), 140, ItemHeight), 8, 8);
        }
        ctx.DrawRectangle(new SolidColorBrush(LT.Wash.WithOpacity(0.07)), null, new Rect(0, _y.Value(Now), 140, ItemHeight), 8, 8);
        for (int i = 0; i < Pages.Length; i++)
        {
            bool sel = Pages[i] == _sel;
            double h = V(_hover[i].Value(Now));
            var c = sel ? LT.Text : Mix(LT.Muted, LT.Text.WithOpacity(0.8), h);
            Icons.Draw(ctx, Icon(Pages[i]), new Point(10 + 8, Y(i) + ItemHeight / 2), 14 * Prefs.Shared.IconScale, c);
            var t = Text(Pages[i].ToString(), 12.5, c, sel ? FontWeight.Medium : FontWeight.Normal);
            ctx.DrawText(t, new Point(10 + 16 + 10, Y(i) + (ItemHeight - t.Height) / 2));
        }
    }
}
