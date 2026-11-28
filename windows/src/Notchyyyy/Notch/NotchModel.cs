using Avalonia;
using Notchyyyy.Theme;

namespace Notchyyyy.Notch;

/// The tabs, in the Mac app's order. Agent, Notes and Play arrive in release 2.
public enum Tab { Home, Media, Today, Apps, Clipboard, Stopwatch, System, Settings }

public static class TabInfo
{
    public static string Title(this Tab t) => t switch
    {
        Tab.Media => "Media", Tab.Today => "Today", Tab.Apps => "Apps", Tab.Clipboard => "Clipboard",
        Tab.Stopwatch => "Stopwatch", Tab.System => "System", Tab.Settings => "Settings", _ => "Home",
    };
    /// The Mac app's saved names (Settings › Tabs keeps hidden tabs by these).
    public static string Key(this Tab t) => t switch
    {
        Tab.Media => "music", Tab.Clipboard => "clip", _ => t.ToString().ToLowerInvariant(),
    };
    public static IconKind Icon(this Tab t) => t switch
    {
        Tab.Media => IconKind.Media, Tab.Today => IconKind.Today, Tab.Apps => IconKind.Apps, Tab.Clipboard => IconKind.Clipboard,
        Tab.Stopwatch => IconKind.Stopwatch, Tab.System => IconKind.System, Tab.Settings => IconKind.Settings, _ => IconKind.Home,
    };
    /// Home and Settings can't be hidden; everything else can.
    public static bool Hideable(this Tab t) => t != Tab.Home && t != Tab.Settings;
    /// Which plan step fills the tab in (shown until then).
    public static int Step(this Tab t) => t switch
    {
        Tab.Home => 4, Tab.Settings => 5, Tab.Media => 6, Tab.Clipboard => 7, Tab.Apps => 8,
        Tab.Today or Tab.Stopwatch => 9, _ => 10,
    };
}

/// The state the notch is drawn from. Views listen to <see cref="Changed"/>.
public sealed class NotchModel
{
    public event Action<string>? Changed;

    private bool _expanded, _hovering, _musicPlaying;
    private Tab _tab = Tab.Home;

    public Tab Tab
    {
        get => _tab;
        set { if (_tab == value) return; _tab = value; Changed?.Invoke(nameof(Tab)); }
    }

    public IReadOnlyList<Tab> VisibleTabs =>
        Enum.GetValues<Tab>().Where(t => !t.Hideable() || !Prefs.Shared.HiddenTabs.Contains(t.Key())).ToList();

    /// Finished the first-run setup (onboarding arrives in plan step 11; until then, always).
    public bool Onboarded => true;

    public bool Expanded { get => _expanded; set => Set(ref _expanded, value, nameof(Expanded)); }
    public bool Hovering { get => _hovering; set => Set(ref _hovering, value, nameof(Hovering)); }

    /// Stand-in for the media session until the Media step (plan step 6): `--demo` plays.
    public bool MusicPlaying { get => _musicPlaying; set => Set(ref _musicPlaying, value, nameof(MusicPlaying)); }

    /// Windows laptops have no notch: a slim pill centred at the top of the screen, like the Mac app on
    /// screens without one. Its height is also the open body's header row.
    public Size NotchSize { get; } = new(180, 32);
    public Size BodySize { get; } = new(700, 300);
    public double PillGap => 10;
    public double PillHeight => 42;

    /// While true the notch stays open (`--open`, onboarding, a dialog).
    public bool Pinned { get; set; }
    /// A menu is open: its items can sit outside the notch, so don't collapse.
    public bool MenuOpen { get; set; }

    public bool LiveActivity => Prefs.Shared.LiveActivity && MusicPlaying;
    /// something shows in the closed pill: a live activity, or Sprout
    public bool Activity => LiveActivity || Prefs.Shared.Buddy;
    /// room the live activity needs beside the pill's centre
    public double Extra => 92;

    /// The closed pill's size (it swells a little under the pointer).
    public Size ClosedSize => new(NotchSize.Width + 12 + (Activity ? Extra : 0) + (Hovering ? 16 : 0),
                                  NotchSize.Height + (Hovering ? 5 : 0));

    private void Set(ref bool field, bool value, string name)
    {
        if (field == value) return;
        field = value;
        Changed?.Invoke(name);
    }
}
