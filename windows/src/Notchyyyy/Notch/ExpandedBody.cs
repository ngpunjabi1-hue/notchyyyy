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
    private readonly BlurSwap _title = new();
    private readonly BlurSwap _content = new();
    private readonly NotchModel _m;
    private readonly IClock _clock;

    /// The Home view when it's the tab on show (the shell steers the big Sprout's gaze through it).
    public HomeView? CurrentHome => _content.Current as HomeView;

    // Home is made once and kept: building it again on every switch cost a visible stall (measured on Windows)
    private HomeView? _home;
    private MediaView? _media;
    private Control Content(Tab tab) => tab switch
    {
        Tab.Home => _home ??= new HomeView(_m, _clock),
        Tab.Media => _media ??= new MediaView(_m, _clock),
        Tab.Settings => new SettingsView(_m, _clock),
        _ => new TabPlaceholder(tab),
    };

    public SettingsView? CurrentSettings => _content.Current as SettingsView;
    public MediaView? CurrentMedia => _content.Current as MediaView;

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
        _time = new TextBlock
        {
            FontFamily = LT.Mono, FontSize = LT.TextSize(11), Foreground = new SolidColorBrush(LT.Muted),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _time, new IconView(IconKind.More, 13, LT.Muted) { Width = 22, Height = 18 } },
        };
        Children.Add(new Grid { Margin = new Thickness(14 + 18, 0), Children = { crumbs, right } });

        var host = new Border { Margin = new Thickness(14 + 24, 14, 14 + 24, 24), Child = _content };
        SetRow(host, 1);
        Children.Add(host);

        Showing = m.Tab;
        _title.Show(Label(m.Tab.Title(), LT.Text), clock.Now, animate: false);
        _content.Show(Content(m.Tab), clock.Now, animate: false);
        UpdateTime();
        DispatcherTimer.Run(() => { UpdateTime(); return true; }, TimeSpan.FromSeconds(15));
    }

    public Tab Showing { get; private set; }

    /// Cross-fades the header title and the content to <paramref name="tab"/>.
    public void SwitchTo(Tab tab, double now, bool animate = true)
    {
        if (tab == Showing) return;
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
