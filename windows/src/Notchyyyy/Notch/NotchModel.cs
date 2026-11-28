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

/// <summary>
/// What the little Sprout in the closed pill knows about you (the Mac's BuddyState): where the pointer is, whether
/// you're typing fast or have stepped away, and when it last celebrated. <see cref="Senses"/> keeps it up to date;
/// the test flags --fake-typing and --fake-away stand in for the real sensors.
/// </summary>
public sealed class BuddyState
{
    public Avalonia.Point Gaze { get; set; }
    public double CheerUntil { get; set; } = double.NegativeInfinity;
    public bool Typing { get; set; } = Theme.Debug.Has("--fake-typing");
    /// 1 at a steady typing pace, up to 2 when you're flying
    public double Pace { get; set; } = Theme.Debug.Has("--fake-typing") ? 1.6 : 1;
    public bool Away { get; set; } = Theme.Debug.Has("--fake-away");
    public double WakeUntil { get; set; } = double.NegativeInfinity;
    /// The hour of the day (a test can pin it).
    public Func<int> Hour { get; set; } = () => Theme.Wall.Now().Hour;
}

/// What the peek bubble says: time for water, a cheer after a glass, or that Notchyyyy just updated itself.
public enum Peek { None, Hydrate, Cheer, Updated }

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

    /// Finished the first-run setup (kept in the settings; Settings › About can run it again).
    public bool Onboarded
    {
        get => Prefs.Shared.Onboarded;
        set { if (Prefs.Shared.Onboarded == value) return; Prefs.Shared.Onboarded = value; Changed?.Invoke(nameof(Onboarded)); }
    }

    // ---- the peek bubble: the closed notch swells a little to say something ----

    /// What the bubble says (None: no bubble).
    public Peek Peeking { get; private set; }
    private double _peekUntil;
    private Action? _peekDone;
    /// The bubble shows (only while the notch is closed).
    public bool PeekShown => Peeking != Peek.None && !Expanded;
    public Size PeekSize => new(420, NotchSize.Height + 60);

    /// Shows the bubble for <paramref name="seconds"/>; <paramref name="timedOut"/> runs if it's left alone that long.
    public void ShowPeek(Peek p, double now, double seconds, Action? timedOut = null)
    {
        Peeking = p; _peekUntil = now + seconds; _peekDone = timedOut;
        Changed?.Invoke(nameof(Peeking));
    }

    /// Tells the views something changed that the model doesn't own ("Water", …).
    public void Notify(string what) => Changed?.Invoke(what);

    public void EndPeek()
    {
        if (Peeking == Peek.None) return;
        Peeking = Peek.None; _peekDone = null;
        Changed?.Invoke(nameof(Peeking));
    }

    /// Called with the app's clock (the shell's pointer tick): a bubble left alone tucks itself away.
    public void TickPeek(double now)
    {
        if (Peeking == Peek.None || now < _peekUntil) return;
        var done = _peekDone;
        EndPeek();
        done?.Invoke();
    }

    public BuddyState Buddy { get; } = new();

    /// Asked during setup (plan step 11); Home greets you by it. `--name` stands in for it in tests.
    public string UserName { get; set; } = Theme.Debug.Value("--name") ?? Prefs.Shared.UserName;
    public string FirstName => UserName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

    /// Glasses of water today.
    public int Glasses => Water.Count;

    /// Water: glasses today, reminders, the heads-up before one.
    public global::Notchyyyy.Today.Water Water { get; } = new();

    /// The little Sprout's mood, most important first (the Mac's NotchBuddy).
    public Sprout.SproutMood BuddyMood(double now) =>
        now < Buddy.CheerUntil ? Sprout.SproutMood.Cheer :
        Hovering ? Sprout.SproutMood.Happy :
        Buddy.Typing ? Sprout.SproutMood.Running :
        now < Buddy.WakeUntil ? Sprout.SproutMood.Surprised :
        Buddy.Away ? Sprout.SproutMood.Sleepy :
        MusicPlaying ? Sprout.SproutMood.Music :
        Buddy.Hour() >= 23 || Buddy.Hour() < 6 ? Sprout.SproutMood.Sleepy : Sprout.SproutMood.Idle;

    public bool Expanded { get => _expanded; set => Set(ref _expanded, value, nameof(Expanded)); }
    public bool Hovering { get => _hovering; set => Set(ref _hovering, value, nameof(Hovering)); }

    /// What's playing (Windows' media sessions; the demo track with `--demo`).
    public global::Notchyyyy.Media.MediaPlayer Media { get; }

    /// The last 40 copies (Windows' clipboard; samples with `--sample`).
    public global::Notchyyyy.Clipboard.Clips Clips { get; } = new();

    /// The pinned apps and every Start-menu app (a stand-in list on the Mac preview).
    public global::Notchyyyy.Apps.Favorites Apps { get; } = new();

    /// The day's tasks (Today) and the stopwatch.
    public global::Notchyyyy.Today.Tasks Tasks { get; } = new();
    public global::Notchyyyy.Today.StopwatchModel Stopwatch { get; } = new(new Motion.SystemClock());

    /// CPU, GPU, memory and storage (Windows' counters; a calm stand-in on the Mac preview).
    public global::Notchyyyy.SystemInfo.SystemMonitor System { get; } = new();

    public NotchModel()
    {
        Media = new global::Notchyyyy.Media.MediaPlayer(new Motion.SystemClock());
        Media.Changed += what =>
        {
            if (what is "Playing" or "Track") MusicPlaying = Media.IsPlaying;
            // the pill shows the artwork and its colour
            if (what is "Art" or "Track") Changed?.Invoke("MediaArt");
        };
        // the stopwatch's time shows in the closed pill while it runs
        Stopwatch.Changed += () => Changed?.Invoke("Stopwatch");
    }

    /// Music is playing (follows <see cref="Media"/>; tests may set it directly).
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

    public bool LiveActivity => Prefs.Shared.LiveActivity && (MusicPlaying || Stopwatch.Running);
    /// The stopwatch runs and live activities are on: its icon and time take the pill (the Mac's CollapsedBar.timing).
    public bool Timing => Prefs.Shared.LiveActivity && Stopwatch.Running;
    /// Sprout sits in the pill unless the stopwatch's icon needs the place.
    public bool ShowsBuddy => Prefs.Shared.Buddy && !Timing;
    /// something shows in the closed pill: a live activity, or Sprout
    public bool Activity => LiveActivity || Prefs.Shared.Buddy;
    /// room the live activity needs beside the pill's centre
    public double Extra => Timing ? 124 : 92;   // the time needs a little more room than an icon

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
