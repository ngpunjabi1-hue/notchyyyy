using Avalonia.Threading;
using Notchyyyy.Motion;
using Notchyyyy.Notch;

namespace Notchyyyy.Sprout;

/// <summary>What the senses read: seconds since any input, and how many keys went down since the last look.</summary>
public interface ISenseSource
{
    double SecondsSinceInput { get; }
    /// Key presses since the last call (a count only: which keys, never).
    int TakeKeyDowns();
}

/// <summary>
/// Sprout's senses (the Mac's BuddyState.sense): looked at 20 times a second. Typing: four or more key presses in a
/// second; it stops after 0.9 s without a key or when the pace drops under two a second; the pace (1…2) is how fast.
/// Away: a minute without any keyboard, mouse or touchpad input; coming back gives a startled little wake-up.
/// Runs only while Sprout lives in the notch.
/// </summary>
public sealed class Senses
{
    private readonly BuddyState _b;
    private readonly IClock _clock;
    private readonly ISenseSource _src;
    private readonly List<double> _keys = new();
    private DispatcherTimer? _timer;
    private readonly bool _fakeTyping = Theme.Debug.Has("--fake-typing"), _fakeAway = Theme.Debug.Has("--fake-away");

    /// A minute with no input at all (`--away-after N` for tests).
    public static double AwayAfter { get; } = double.TryParse(Theme.Debug.Value("--away-after"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var a) ? a : 60;

    public Senses(BuddyState b, IClock clock, ISenseSource src) { _b = b; _clock = clock; _src = src; }

    /// Something Sprout shows changed (typing started or stopped, the pace moved, away or back).
    public event Action? Changed;

    public bool Running => _timer is not null;

    public void Start()
    {
        if (_timer is not null) return;
        _src.TakeKeyDowns();   // what came before doesn't count
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Background, (_, _) => Sense());
        _timer.Start();
    }

    public void Stop()
    {
        _timer?.Stop(); _timer = null;
        SetTyping(false);
        if (_b.Away) { _b.Away = false; Changed?.Invoke(); }
    }

    /// One look (the timer's; tests call it with their own clock).
    public void Sense()
    {
        double now = _clock.Now;
        int downs = _src.TakeKeyDowns();
        for (int i = 0; i < downs; i++) _keys.Add(now);
        _keys.RemoveAll(t => now - t > 1.5);
        int lastSecond = _keys.Count(t => now - t <= 1.0);
        double rate = _keys.Count / 1.5;
        double sinceKey = _keys.Count > 0 ? now - _keys[^1] : double.PositiveInfinity;
        if (_fakeTyping) { lastSecond = 8; rate = 8; sinceKey = 0; }
        bool changed = false;
        if (!_b.Typing && lastSecond >= 4) { SetTyping(true); changed = true; }
        else if (_b.Typing && (sinceKey > 0.9 || rate < 2)) { SetTyping(false); changed = true; }
        if (_b.Typing)
        {
            double p = Math.Clamp(rate / 5, 1, 2);
            if (Math.Abs(p - _b.Pace) > 0.2) { _b.Pace = p; changed = true; }
        }
        double sinceAny = _fakeAway ? 9999 : _src.SecondsSinceInput;
        bool away = sinceAny >= AwayAfter;
        if (away != _b.Away)
        {
            _b.Away = away;
            if (!away) _b.WakeUntil = now + 1.3;   // back! a startled little wake-up
            changed = true;
        }
        if (changed) Changed?.Invoke();
    }

    private void SetTyping(bool on)
    {
        _b.Typing = on;
        if (!on) _b.Pace = 1;
    }
}

/// <summary>Nothing to sense (the Mac preview): never typing, never away (the fake flags still work).</summary>
public sealed class NoSenses : ISenseSource
{
    public double SecondsSinceInput => 0;
    public int TakeKeyDowns() => 0;
}

/// <summary>A source a test drives.</summary>
public sealed class ManualSenses : ISenseSource
{
    public double SecondsSinceInput { get; set; }
    public int Pending { get; set; }
    public int TakeKeyDowns() { int n = Pending; Pending = 0; return n; }
}
