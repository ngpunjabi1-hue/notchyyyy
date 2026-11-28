using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Notchyyyy.Motion;
using Notchyyyy.Theme;

namespace Notchyyyy.Notch;

/// <summary>
/// Swaps one child for another in place: the outgoing view fades out while the incoming one fades in, both on the
/// snappy spring, on top of each other (nothing slides in from a side, so nothing shifts).
///
/// The Mac app's "blurFade" also blurs them. On Windows that blur is off by default: at 125 % and up, Avalonia's
/// BlurEffect softens text by the same amount at any radius from 0.05 to 0.5 pt (measured), so a view could never
/// sharpen smoothly — it snapped from soft to crisp in one frame as the effect came off (and the reverse as a view
/// started to leave). `--blur-swap` brings it back for comparison.
/// </summary>
public sealed class BlurSwap : Panel
{
    private readonly List<(Control View, SpringValue Amount)> _items = new();

    public Control? Current => _items.Count > 0 ? _items[^1].View : null;
    /// Plain cross-fade (the default); `--blur-swap` adds the Mac's blur, for comparison.
    public static bool NoBlur { get; set; } = !global::Notchyyyy.Theme.Debug.Has("--blur-swap");

    public void Show(Control view, double now, bool animate)
    {
        // without animation, nothing else stays behind: whatever was showing goes at once (a view told to fade out
        // here, while nothing drives the fade, stayed on screen under the new one — seen on Windows)
        if (!animate)
        {
            foreach (var (other, _) in _items.Where(i => !ReferenceEquals(i.View, view)).ToList()) Children.Remove(other);
            _items.RemoveAll(i => !ReferenceEquals(i.View, view));
        }
        // a view that is still fading out comes back from where it is, instead of being added twice
        int back = _items.FindIndex(i => ReferenceEquals(i.View, view));
        if (back >= 0)
        {
            var (_, returning) = _items[back];
            _items.RemoveAt(back);
            foreach (var (_, a) in _items) a.AnimateTo(1, LT.Snappy, now);
            if (animate) returning.AnimateTo(0, LT.Snappy, now); else returning.Snap(0);
            _items.Add((view, returning));
            Update(now);
            return;
        }
        foreach (var (_, a) in _items) a.AnimateTo(1, LT.Snappy, now);   // everything already here leaves
        var amount = new SpringValue(animate ? 1 : 0, 0.002);
        if (animate) amount.AnimateTo(0, LT.Snappy, now);
        _items.Add((view, amount));
        Children.Add(view);
        Update(now);
    }

    public bool Animating(double now) => _items.Any(i => !i.Amount.IsSettled(now));

    public void Update(double now)
    {
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            var (view, amount) = _items[i];
            double a = Math.Clamp(amount.Value(now), 0, 1);
            bool leaving = i < _items.Count - 1;
            if (leaving && a >= 0.999 && amount.IsSettled(now))
            {
                Children.Remove(view);
                _items.RemoveAt(i);
                continue;
            }
            view.Opacity = 1 - a;
            view.Effect = a > 0.01 && !NoBlur ? new BlurEffect { Radius = a * 8 } : null;
        }
    }
}

/// <summary>
/// The open notch: a header row ("Notchyyyy / Home" on the left, the time and the menu on the right)
/// and the tab's content below it.
/// </summary>
public sealed class ExpandedBody : Grid
{
    private readonly TextBlock _time;
    private readonly UpdateChip _updateChip;
    public UpdateChip UpdateChip => _updateChip;

    /// An update downloaded (or went): the chip in the header.
    public void RefreshUpdate()
    {
        var v = _m.Updater?.Available;
        _updateChip.IsVisible = v is not null;
        if (v is not null) _updateChip.Set(v);
    }
    private readonly BlurSwap _title = new();
    private readonly BlurSwap _content = new();
    private readonly NotchModel _m;
    private readonly IClock _clock;

    /// The Home view when it's the tab on show (the shell steers the big Sprout's gaze through it).
    public HomeView? CurrentHome => _content.Current as HomeView;

    // Home is made once and kept: building it again on every switch cost a visible stall (measured on Windows)
    private HomeView? _home;
    private MediaView? _media;
    private ClipboardView? _clipboard;
    private AppsView? _apps;
    private TodayView? _today;
    private StopwatchView? _stopwatch;
    private SystemView? _system;
    private Control Content(Tab tab) => tab switch
    {
        Tab.Home => _home ??= new HomeView(_m, _clock),
        Tab.Media => _media ??= new MediaView(_m, _clock),
        Tab.Clipboard => _clipboard ??= new ClipboardView(_m, _clock),
        Tab.Apps => _apps ??= new AppsView(_m, _clock),
        Tab.Today => _today ??= new TodayView(_m, _clock),
        Tab.Stopwatch => _stopwatch ??= new StopwatchView(_m, _clock),
        Tab.System => _system ??= new SystemView(_m, _clock),
        Tab.Settings => new SettingsView(_m, _clock),
        _ => new TabPlaceholder(tab),
    };

    public SettingsView? CurrentSettings => _content.Current as SettingsView;
    public MediaView? CurrentMedia => _content.Current as MediaView;
    public ClipboardView? CurrentClipboard => _content.Current as ClipboardView;
    public AppsView? CurrentApps => _content.Current as AppsView;
    public TodayView? CurrentToday => _content.Current as TodayView;
    public StopwatchView? CurrentStopwatch => _content.Current as StopwatchView;
    public SystemView? CurrentSystem => _content.Current as SystemView;

    public ExpandedBody(NotchModel m, IClock clock)
    {
        _m = m; _clock = clock;
        Width = m.BodySize.Width; Height = m.BodySize.Height;
        RowDefinitions = new RowDefinitions($"{m.NotchSize.Height},*");

        var crumbs = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center,
            Children = { Label("Notchyyyy", LT.Faint), Label("/", LT.Faint), _title },
        };
        _updateChip = new UpdateChip(clock, () => _m.Updater?.InstallNow()) { IsVisible = m.Updater?.Available is not null };
        if (m.Updater?.Available is { } waiting) _updateChip.Set(waiting);
        _time = new TextBlock
        {
            FontFamily = LT.Mono, FontSize = LT.TextSize(11), Foreground = new SolidColorBrush(LT.Muted),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _updateChip, _time, new IconView(IconKind.More, 13, LT.Muted) { Width = 22, Height = 18 } },
        };
        Children.Add(new Grid { Margin = new Thickness(14 + 18, 0), Children = { crumbs, right } });

        var host = new Border { Margin = new Thickness(14 + 24, 14, 14 + 24, 24), Child = _content };
        SetRow(host, 1);
        Children.Add(host);

        Showing = m.Tab;
        _setup = !m.Onboarded;
        _title.Show(Label(_setup ? "Welcome" : m.Tab.Title(), LT.Text), clock.Now, animate: false);
        _content.Show(_setup ? (_onboarding = new OnboardingView(m, clock)) : Content(m.Tab), clock.Now, animate: false);
        UpdateTime();
        DispatcherTimer.Run(() => { UpdateTime(); return true; }, TimeSpan.FromSeconds(15));
    }

    public Tab Showing { get; private set; }

    /// Cross-fades the header title and the content to <paramref name="tab"/>.
    private bool _setup;
    private OnboardingView? _onboarding;
    /// The first-run setup, while it shows.
    public OnboardingView? CurrentSetup => _content.Current as OnboardingView;

    /// The setup finished (or is to run again): cross-fade between it and the tab.
    public void SetupChanged(double now, bool animate)
    {
        bool setup = !_m.Onboarded;
        if (setup == _setup) return;
        _setup = setup;
        Showing = _m.Tab;
        _title.Show(Label(setup ? "Welcome" : _m.Tab.Title(), LT.Text), now, animate);
        _content.Show(setup ? (_onboarding = new OnboardingView(_m, _clock)) : Content(_m.Tab), now, animate);
    }

    public void SwitchTo(Tab tab, double now, bool animate = true)
    {
        if (tab == Showing || _setup) { Showing = tab; return; }
        Showing = tab;
        _title.Show(Label(tab.Title(), LT.Text), now, animate);
        _content.Show(Content(tab), now, animate);
    }

    public bool Animating(double now) => _title.Animating(now) || _content.Animating(now) || (CurrentSettings?.Animating(now) ?? false);

    public void Update(double now) { _title.Update(now); _content.Update(now); CurrentSettings?.Update(now); }

    private static TextBlock Label(string s, Color c) => new()
    {
        Text = s, Foreground = new SolidColorBrush(c), FontFamily = LT.Ui,
        FontSize = LT.TextSize(12), FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center,
    };

    private void UpdateTime() => _time.Text = global::Notchyyyy.Theme.Wall.Now().ToString("HH:mm");
}

/// Draws one icon centred in its bounds.
public sealed class IconView : Control
{
    private readonly IconKind _kind;
    private readonly double _size;
    public Color Color { get; set; }
    public IconView(IconKind kind, double size, Color color) { _kind = kind; _size = size; Color = color; }
    public override void Render(DrawingContext ctx) =>
        Icons.Draw(ctx, _kind, new Point(Bounds.Width / 2, Bounds.Height / 2), _size, Color);
}

/// Until a tab is built, a quiet empty state like the Mac app's EmptyNote: a glyph and two lines.
public sealed class TabPlaceholder : StackPanel
{
    public TabPlaceholder(Tab tab)
    {
        Spacing = 6;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        Children.Add(new IconView(tab.Icon(), 22, LT.Faint) { Width = 26, Height = 26, HorizontalAlignment = HorizontalAlignment.Center });
        Children.Add(new TextBlock
        {
            Text = tab.Title(), FontFamily = LT.Ui, FontSize = LT.TextSize(12), FontWeight = FontWeight.Medium,
            Foreground = new SolidColorBrush(LT.Muted), HorizontalAlignment = HorizontalAlignment.Center,
        });
        Children.Add(new TextBlock
        {
            Text = $"Arrives in step {tab.Step()}", FontFamily = LT.Ui, FontSize = LT.TextSize(11),
            Foreground = new SolidColorBrush(LT.Faint), HorizontalAlignment = HorizontalAlignment.Center,
        });
    }
}

/// "Update 0.2.0" in the open notch's header (the Mac's UpdateChip): installs now; otherwise it installs by itself
/// once the notch is idle.
public sealed class UpdateChip : SpringControl
{
    private readonly Action _install;
    private readonly SpringValue _hover;
    private string _version = "";
    public UpdateChip(IClock clock, Action install) : base(clock)
    {
        _install = install; _hover = Spring(0);
        Height = 20; Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand); VerticalAlignment = VerticalAlignment.Center;
    }
    public string Title => $"Update {_version}";
    public void Set(string version)
    {
        _version = version;
        ToolTip.SetTip(this, $"Notchyyyy {version} is ready: it installs by itself when the notch is idle, or click to install now");
        InvalidateMeasure(); InvalidateVisual();
    }
    protected override Size MeasureOverride(Size available) => new(8 + 14 + Text(Title, 10.5, Colors.White, FontWeight.SemiBold).Width + 8, 20);
    protected override void OnPointerEntered(Avalonia.Input.PointerEventArgs e) { base.OnPointerEntered(e); Go(_hover, 1, LT.Hover); }
    protected override void OnPointerExited(Avalonia.Input.PointerEventArgs e) { base.OnPointerExited(e); Go(_hover, 0, LT.Hover); }
    protected override void OnPointerPressed(Avalonia.Input.PointerPressedEventArgs e) { base.OnPointerPressed(e); e.Handled = true; }
    protected override void OnPointerReleased(Avalonia.Input.PointerReleasedEventArgs e) { base.OnPointerReleased(e); if (new Rect(Bounds.Size).Contains(e.GetPosition(this))) _install(); }
    public override void Render(DrawingContext ctx)
    {
        double h = V(_hover.Value(Now));
        ctx.DrawRectangle(new SolidColorBrush(LT.AccentColor.WithOpacity(0.85 + 0.15 * h)), null, new Rect(Bounds.Size), 10, 10);
        Icons.Draw(ctx, IconKind.Download, new Point(8 + 5, 10), 11, Colors.White);
        var t = Text(Title, 10.5, Colors.White, FontWeight.SemiBold);
        ctx.DrawText(t, new Point(8 + 14, 10 - t.Height / 2));
    }
}
