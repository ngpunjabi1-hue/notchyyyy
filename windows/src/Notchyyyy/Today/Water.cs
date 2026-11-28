using System.Text.Json;
using Notchyyyy.Theme;

namespace Notchyyyy.Today;

/// <summary>
/// Water (the Mac's HydrationStore): glasses today, a reminder every 30 min … 1.5 h between 8 am and 10 pm, and a
/// heads-up in the closed notch in the minute before one is due. Reminders, the interval and the goal are settings
/// (Settings › Extras); today's count is kept in %APPDATA%\Notchyyyy\water.json and starts again each morning.
/// </summary>
public sealed class Water
{
    private readonly string _file;
    private readonly bool _readOnly;
    private DateTime _lastDrink, _day;
    private DateTime? _snoozeUntil;

    /// Reminders only between these hours (8 am to 10 pm).
    public const int FromHour = 8, ToHour = 22;

    public Water(string? file = null, bool? readOnly = null, Func<DateTime>? clock = null)
    {
        if (clock is not null) Clock = clock;
        _file = file ?? Path.Combine(Prefs.Folder, "water.json");
        _readOnly = readOnly ?? (Debug.Sample || Debug.Has("--perf") || Debug.Has("--light") || Debug.Has("--dark") || Debug.Has("--win-selftest"));
        Load();
        // never nag the moment the app starts: the clock begins now
        _lastDrink = Now;
        // test-only: the next reminder is due in N seconds
        if (double.TryParse(Debug.Value("--due-in"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s))
            _lastDrink = Now.AddSeconds(s).AddMinutes(-Interval);
        Prefs.Shared.Changed += name => { if (name is nameof(Prefs.WaterReminders) or nameof(Prefs.WaterInterval)) { _lastDrink = Now; _snoozeUntil = null; } };
    }

    /// The time of day (the app's wall clock; tests give their own).
    public Func<DateTime> Clock { get; set; } = () => Wall.Now();
    private DateTime Now => Clock();

    public bool Enabled => Prefs.Shared.WaterReminders;
    public int Goal => Prefs.Shared.WaterGoal;
    public int Interval => (int)Prefs.Shared.WaterInterval;
    public int Count { get; private set; }

    /// "Drank", "Undo", "Reminded", "Later".
    public event Action<string>? Changed;

    /// When the next reminder will come, or null (off, or outside the waking hours).
    public DateTime? NextDue()
    {
        if (!Enabled || Now.Hour < FromHour || Now.Hour >= ToHour) return null;
        return _snoozeUntil ?? _lastDrink.AddMinutes(Interval);
    }

    public bool IsDue()
    {
        RollDay();
        return NextDue() is { } due && Now >= due;
    }

    /// The next reminder is under a minute away: the closed notch shows the water ring as a heads-up.
    public bool DueSoon => NextDue() is { } due && Now >= due.AddSeconds(-60);

    public void Drank()
    {
        RollDay();
        Count++;
        _lastDrink = Now; _snoozeUntil = null;
        Save();
        Changed?.Invoke("Drank");
    }

    public void Undo() { if (Count == 0) return; Count--; Save(); Changed?.Invoke("Undo"); }

    /// Asked again in a little while (the bubble's Later, or a bubble left alone).
    public void Later(double minutes = 15) { _snoozeUntil = Now.AddMinutes(minutes); Changed?.Invoke("Later"); }

    /// A reminder was shown: the next waits a full interval even if this one is ignored.
    public void Reminded() { _lastDrink = Now; _snoozeUntil = null; Changed?.Invoke("Reminded"); }

    private void RollDay()
    {
        if (_day == Now.Date) return;
        _day = Now.Date; Count = 0;
        Save();
    }

    private sealed record Saved(DateTime Day, int Count);

    private void Load()
    {
        _day = Now.Date;
        try
        {
            if (_readOnly || !File.Exists(_file)) return;
            var s = JsonSerializer.Deserialize<Saved>(File.ReadAllText(_file));
            if (s is not null && s.Day.Date == Now.Date) Count = s.Count;
        }
        catch (Exception e) { Debug.Log($"water count unreadable: {e.Message}"); }
    }

    private void Save()
    {
        if (_readOnly) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file + ".tmp", JsonSerializer.Serialize(new Saved(_day, Count)));
            File.Move(_file + ".tmp", _file, overwrite: true);
        }
        catch (Exception e) { Debug.Log($"water count not saved: {e.Message}"); }
    }
}
