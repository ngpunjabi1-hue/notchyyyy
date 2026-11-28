using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Threading;
using Notchyyyy.Motion;
using Notchyyyy.Theme;

namespace Notchyyyy.Today;

/// <summary>A task for a day (the Mac's DayTask): a title, an optional time, the day it belongs to, and done.</summary>
public sealed class DayTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public DateTime? Time { get; set; }
    public DateTime Day { get; set; }
    public bool Done { get; set; }
}

/// <summary>Shows a reminder when a task is due: a Windows notification (or nothing, on the Mac preview).</summary>
public interface IReminders
{
    void Show(DayTask task);
}

/// The Mac preview: reminders are only remembered (tests read them).
public sealed class MemoryReminders : IReminders
{
    public List<DayTask> Shown { get; } = new();
    public void Show(DayTask task) => Shown.Add(task);
}

/// <summary>
/// The day's plan (the Mac's TasksStore): tasks with optional times, a reminder at each time, and anything left
/// undone from earlier days carried over to today. Kept in %APPDATA%\Notchyyyy\tasks.json; finished tasks older than
/// two weeks are dropped, unfinished ones never.
/// </summary>
public sealed class Tasks
{
    private readonly List<DayTask> _tasks = new();
    private readonly string _file;
    private readonly bool _readOnly;
    private IReminders? _reminders;
    private readonly HashSet<Guid> _reminded = new();
    private DispatcherTimer? _timer;

    public Tasks(string? file = null, bool? readOnly = null, bool? samples = null)
    {
        _file = file ?? Path.Combine(Prefs.Folder, "tasks.json");
        _readOnly = readOnly ?? (Debug.Sample || Debug.Has("--perf") || Debug.Has("--light") || Debug.Has("--dark") || Debug.Has("--win-selftest"));
        if (samples ?? Debug.Sample) _tasks.AddRange(Samples());
        else Load();
    }

    public IReadOnlyList<DayTask> All => _tasks;
    /// "Changed" for any change to the list; "Reminded" with the task when a reminder went out.
    public event Action<string, DayTask?>? Changed;

    /// The time of day (the app's wall clock; tests give their own).
    public Func<DateTime> Clock { get; set; } = () => Wall.Now();
    DateTime Now => Clock();
    DateTime TodayStart => Now.Date;

    public IReadOnlyList<DayTask> Today => _tasks.Where(t => t.Day == TodayStart).OrderBy(t => t, Order).ToList();
    public IReadOnlyList<DayTask> Carried => _tasks.Where(t => t.Day < TodayStart && !t.Done).OrderBy(t => t, Order).ToList();
    public int DoneCount => Today.Count(t => t.Done);
    /// The next timed task not yet done (a minute's grace after its time).
    public DayTask? NextUp => Today.FirstOrDefault(t => !t.Done && t.Time is { } x && x >= Now.AddSeconds(-60));

    /// Timed tasks first, by time; then the rest by title.
    private static readonly Comparer<DayTask> Order = Comparer<DayTask>.Create((a, b) =>
        (a.Time, b.Time) switch
        {
            ({ } x, { } y) => x.CompareTo(y),
            (null, not null) => 1,
            (not null, null) => -1,
            _ => string.Compare(a.Title, b.Title, StringComparison.CurrentCulture),
        });

    /// Starts reminding (a check every 15 s, so a reminder is at most 15 s late).
    public void Start(IReminders reminders)
    {
        _reminders = reminders;
        // anything already past due when the app starts isn't announced late
        foreach (var t in _tasks.Where(t => t.Time is { } x && x <= Now)) _reminded.Add(t.Id);
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(15), DispatcherPriority.Background, (_, _) => CheckDue());
        _timer.Start();
    }

    /// Sends the reminders that have come due (the timer calls this; tests call it directly).
    public void CheckDue()
    {
        foreach (var t in _tasks.Where(t => !t.Done && t.Time is { } x && x <= Now && x > Now.AddMinutes(-10) && !_reminded.Contains(t.Id)).ToList())
        {
            _reminded.Add(t.Id);
            _reminders?.Show(t);
            Changed?.Invoke("Reminded", t);
        }
    }

    /// "Call mom at 5pm" → "Call mom" at 17:00 today. An explicit time wins.
    public DayTask? Add(string raw, DateTime? time = null)
    {
        var title = raw.Trim();
        if (title.Length == 0) return null;
        if (time is null && TimeParse.Find(title) is { } found)
        {
            time = TodayStart + found.Time;
            var rest = title.Remove(found.Index, found.Length);
            rest = Regex.Replace(rest, @"\s+(at|by|@)\s*$", "", RegexOptions.IgnoreCase);
            rest = Regex.Replace(rest, @"\s{2,}", " ").Trim().Trim(',', '.', ';', ':', '-', '–').Trim();
            if (rest.Length > 0) title = rest;
        }
        var task = new DayTask { Title = title, Time = time is { } tt ? TodayStart + tt.TimeOfDay : null, Day = TodayStart };
        _tasks.Add(task);
        if (task.Time is { } due && due <= Now) _reminded.Add(task.Id);   // already past: no reminder
        Save();
        Changed?.Invoke("Changed", task);
        return task;
    }

    public void Toggle(DayTask t)
    {
        t.Done = !t.Done;
        if (t.Done) t.Day = TodayStart;   // finishing a carried-over task counts it for today
        Save();
        Changed?.Invoke("Changed", t);
    }

    public void MoveToToday(DayTask t)
    {
        t.Day = TodayStart;
        if (t.Time is { } x) { t.Time = TodayStart + x.TimeOfDay; if (t.Time > Now) _reminded.Remove(t.Id); }
        Save();
        Changed?.Invoke("Changed", t);
    }

    public void Delete(DayTask t) { if (_tasks.Remove(t)) { Save(); Changed?.Invoke("Changed", t); } }

    public void ClearDone() { if (_tasks.RemoveAll(t => t.Done) > 0) { Save(); Changed?.Invoke("Changed", null); } }

    // ---- kept on disk ----

    private void Load()
    {
        try
        {
            if (!File.Exists(_file)) return;
            _tasks.AddRange(JsonSerializer.Deserialize<List<DayTask>>(File.ReadAllText(_file)) ?? new());
            var cutoff = TodayStart.AddDays(-14);
            _tasks.RemoveAll(t => t.Done && t.Day < cutoff);
        }
        catch (Exception e) { Debug.Log($"tasks unreadable: {e.Message}"); }
    }

    private void Save()
    {
        if (_readOnly) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            var tmp = _file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_tasks));
            File.Move(tmp, _file, overwrite: true);
        }
        catch (Exception e) { Debug.Log($"tasks not saved: {e.Message}"); }
    }

    /// The Mac's samples.
    public static IEnumerable<DayTask> Samples()
    {
        var today = Wall.Now().Date;
        DateTime At(int h, int m) => today.AddHours(h).AddMinutes(m);
        return new[]
        {
            new DayTask { Title = "Stand-up with the design team", Time = At(9, 30), Day = today, Done = true },
            new DayTask { Title = "Review the onboarding flow", Time = At(11, 0), Day = today, Done = true },
            new DayTask { Title = "Lunch with Priya", Time = At(13, 30), Day = today },
            new DayTask { Title = "Call mom", Time = At(17, 0), Day = today },
            new DayTask { Title = "Book flights for the offsite", Day = today },
            new DayTask { Title = "Send the invoice", Time = today.AddDays(-1).AddHours(16), Day = today.AddDays(-1) },
        };
    }

    /// "now", "in 25 min", "in 2h 5m · 17:00" (the Mac's countdown).
    public static string Countdown(DateTime t, DateTime? now = null)
    {
        int m = (int)((t - (now ?? Wall.Now())).TotalMinutes);
        if (m <= 0) return "now";
        if (m < 60) return $"in {m} min";
        return $"in {m / 60}h {m % 60}m · {ShortTime(t)}";
    }

    public static string ShortTime(DateTime t) => t.ToString("t", CultureInfo.CurrentCulture);
}

/// <summary>
/// Finds a time of day in a task's title, the way the Mac's date detector does for the common cases:
/// "5pm", "5:30 pm", "17:00", "at 5", "9.15am", "noon", "midnight".
/// </summary>
public static class TimeParse
{
    private static readonly Regex Clock = new(
        @"(?<![\w:])(?:(?<at>at|by|@)\s*)?(?:(?<h>\d{1,2})(?:[:.](?<m>\d{2}))?\s*(?<ap>a\.?m\.?|p\.?m\.?)(?!\w)|(?<h24>\d{1,2})[:.](?<m24>\d{2})(?!\d)|(?<word>noon|midnight)(?!\w))",
        RegexOptions.IgnoreCase);
    private static readonly Regex AtHour = new(@"(?<![\w:])(?:at|@)\s+(?<h>\d{1,2})(?![\w:.])", RegexOptions.IgnoreCase);

    public static (TimeSpan Time, int Index, int Length)? Find(string s)
    {
        foreach (Match m in Clock.Matches(s))
        {
            TimeSpan? t = null;
            if (m.Groups["word"].Success) t = m.Groups["word"].Value.ToLowerInvariant() == "noon" ? TimeSpan.FromHours(12) : TimeSpan.Zero;
            else if (m.Groups["ap"].Success)
            {
                int h = int.Parse(m.Groups["h"].Value), min = m.Groups["m"].Success ? int.Parse(m.Groups["m"].Value) : 0;
                bool pm = m.Groups["ap"].Value.StartsWith("p", StringComparison.OrdinalIgnoreCase);
                if (h is >= 1 and <= 12 && min < 60) t = new TimeSpan(h % 12 + (pm ? 12 : 0), min, 0);
            }
            else if (m.Groups["h24"].Success)
            {
                int h = int.Parse(m.Groups["h24"].Value), min = int.Parse(m.Groups["m24"].Value);
                if (h < 24 && min < 60) t = new TimeSpan(h, min, 0);
            }
            if (t is { } ok) return (ok, m.Index, m.Length);
        }
        // "at 5": an hour alone after "at" — a waking hour (7…11 morning, 12…6 afternoon), as people mean it
        if (AtHour.Match(s) is { Success: true } a && int.Parse(a.Groups["h"].Value) is var hr and >= 1 and <= 12)
            return (TimeSpan.FromHours(hr < 7 || hr == 12 ? (hr == 12 ? 12 : hr + 12) : hr), a.Index, a.Length);
        return null;
    }
}

/// <summary>The stopwatch (the Mac's): start / stop, laps (newest first), reset. Time is the app's clock.</summary>
public sealed class StopwatchModel
{
    private double? _started;
    private double _banked;
    private readonly List<double> _laps = new();
    public IClock Clock { get; set; }

    public StopwatchModel(IClock clock) => Clock = clock;

    public bool Running => _started is not null;
    public IReadOnlyList<double> Laps => _laps;
    public bool HasTime => Running || _banked > 0;
    public event Action? Changed;

    public double Elapsed(double? now = null) => _banked + (_started is { } s ? (now ?? Clock.Now) - s : 0);

    public void Toggle()
    {
        if (_started is { } s) { _banked += Clock.Now - s; _started = null; }
        else _started = Clock.Now;
        Changed?.Invoke();
    }

    public void Lap()
    {
        if (!Running) return;
        _laps.Insert(0, Elapsed() - _laps.Sum());
        Changed?.Invoke();
    }

    public void Reset()
    {
        _started = null; _banked = 0; _laps.Clear();
        Changed?.Invoke();
    }

    /// "02:05.37"; "1:02:05" past an hour; "2:05" without hundredths (the closed pill).
    public static string Format(double t, bool centis = true)
    {
        double total = Math.Max(0, t);
        int h = (int)total / 3600, m = (int)total / 60 % 60, s = (int)total % 60, cs = (int)((total - Math.Floor(total)) * 100);
        if (h > 0) return $"{h}:{m:00}:{s:00}";
        return centis ? $"{m:00}:{s:00}.{cs:00}" : $"{m}:{s:00}";
    }
}
