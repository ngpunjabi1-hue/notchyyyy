using System.Text.Json;
using Avalonia.Media;
using Notchyyyy.Motion;

namespace Notchyyyy.Theme;

/// Test-only launch flags: they never change saved data.
/// The wall-clock time shown to people (the header clock, the date, the greeting). Tests freeze it so a minute
/// ticking over mid-test can't change the pixels.
public static class Wall
{
    public static Func<DateTime> Now { get; set; } = () => DateTime.Now;
}

public static class Debug
{
    public static string[] Args { get; set; } = Environment.GetCommandLineArgs();
    public static bool Has(string flag) => Args.Contains(flag);
    public static string? Value(string flag)
    {
        int i = Array.IndexOf(Args, flag);
        return i >= 0 && i + 1 < Args.Length ? Args[i + 1] : null;
    }
    /// --sample: in-memory sample data; nothing is written to disk.
    public static bool Sample => Has("--sample");
}

public enum Accent { Indigo, Blue, Teal, Green, Yellow, Orange, Pink, Graphite }
public enum Appearance { Dark, Light }
public enum HoverSpeed { Instant, Normal, Relaxed }

public enum WaterInterval { M30 = 30, M45 = 45, M60 = 60, M90 = 90 }

/// <summary>
/// Everything the user can change in Settings, saved as JSON in %APPDATA%\Notchyyyy\settings.json the moment it
/// changes (written to a temporary file and moved into place, so a crash or an update can never leave half a file).
/// Test flags (--light, --dark, --sample) change nothing on disk.
/// </summary>
public sealed class Prefs
{
    public static Prefs Shared { get; } = Load();

    /// Raised after any change, with the property's name. Visual changes rebuild the notch's content.
    public event Action<string>? Changed;

    private Accent _accent = Accent.Indigo;
    private Appearance _appearance = Appearance.Dark;
    private double _textScale = 1, _iconScale = 1;
    private HoverSpeed _hoverSpeed = HoverSpeed.Normal;
    private bool _haptics = true, _liveActivity = true, _buddy = true, _water = true, _autoUpdate = true;
    private List<string> _hiddenTabs = new();
    private string _userName = "";
    private WaterInterval _waterInterval = WaterInterval.M60;
    private int _waterGoal = 8;

    public Accent Accent { get => _accent; set => Set(ref _accent, value); }
    public Appearance Appearance { get => _appearance; set => Set(ref _appearance, value); }
    public double TextScale { get => _textScale; set => Set(ref _textScale, Math.Clamp(value, 0.85, 1.2)); }
    public double IconScale { get => _iconScale; set => Set(ref _iconScale, Math.Clamp(value, 0.8, 1.3)); }
    public HoverSpeed HoverSpeed { get => _hoverSpeed; set => Set(ref _hoverSpeed, value); }
    public bool Haptics { get => _haptics; set => Set(ref _haptics, value); }
    public bool LiveActivity { get => _liveActivity; set => Set(ref _liveActivity, value); }
    public List<string> HiddenTabs { get => _hiddenTabs; set => Set(ref _hiddenTabs, value ?? new()); }
    /// Sprout lives in the closed pill.
    public bool Buddy { get => _buddy; set => Set(ref _buddy, value); }
    /// Asked during setup; Home greets you by it.
    public string UserName { get => _userName; set => Set(ref _userName, value ?? ""); }
    public bool WaterReminders { get => _water; set => Set(ref _water, value); }
    public WaterInterval WaterInterval { get => _waterInterval; set => Set(ref _waterInterval, value); }
    public int WaterGoal { get => _waterGoal; set => Set(ref _waterGoal, Math.Clamp(value, 1, 20)); }
    public bool AutoUpdate { get => _autoUpdate; set => Set(ref _autoUpdate, value); }

    public double HoverDelay => HoverSpeed switch { HoverSpeed.Instant => 0.03, HoverSpeed.Relaxed => 0.35, _ => 0.12 };

    /// Show or hide a tab (by its saved name). Lists are replaced, not edited, so a change is always noticed.
    public void SetTabHidden(string key, bool hidden)
    {
        var next = _hiddenTabs.Where(k => k != key).ToList();
        if (hidden) next.Add(key);
        HiddenTabs = next;
    }

    private bool _loading;
    /// While the file is being read, the reader goes through the setters: they must neither save nor announce.
    [ThreadStatic] private static bool _reading;
    private void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string name = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        if (_loading || _reading) return;
        Save();
        Changed?.Invoke(name);
    }

    /// Everything back to how it first was (your name stays).
    public void Reset()
    {
        Accent = Accent.Indigo; Appearance = Appearance.Dark; TextScale = 1; IconScale = 1; HoverSpeed = HoverSpeed.Normal;
        Haptics = true; LiveActivity = true; HiddenTabs = new(); Buddy = true;
        WaterReminders = true; WaterInterval = WaterInterval.M60; WaterGoal = 8; AutoUpdate = true;
    }

    /// Where settings live: %APPDATA%\Notchyyyy (on the Mac preview, ~/.config/Notchyyyy). `--settings-dir` for tests.
    public static string Folder => Debug.Value("--settings-dir")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Notchyyyy");
    public static string FilePath => Path.Combine(Folder, "settings.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// Test flags that must never touch the saved settings.
    private static bool ReadOnly => Debug.Sample || Debug.Has("--light") || Debug.Has("--dark") || Debug.Has("--perf") || Debug.Has("--win-selftest");

    private bool _readOnly;

    public static Prefs Load(string? path = null)
    {
        Prefs p;
        string? explicitPath = path;
        path ??= FilePath;
        try
        {
            _reading = true;
            p = File.Exists(path) ? JsonSerializer.Deserialize<Prefs>(File.ReadAllText(path), Json) ?? new Prefs() : new Prefs();
        }
        catch { p = new Prefs(); }     // a damaged file never stops the app: it starts with the defaults
        finally { _reading = false; }
        p._loading = true;
        if (Debug.Has("--light")) p.Appearance = Appearance.Light;
        if (Debug.Has("--dark")) p.Appearance = Appearance.Dark;
        p._loading = false;
        p.Path_ = path;
        // the app's own settings are read-only under test flags; a file a test names explicitly is always writable
        p._readOnly = explicitPath is null && ReadOnly;
        return p;
    }

    [System.Text.Json.Serialization.JsonIgnore] public string? Path_ { get; private set; }

    public void Save()
    {
        if (_readOnly) return;
        var path = Path_ ?? FilePath;
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, path, overwrite: true);
        }
        catch { /* never crash over settings */ }
    }
}

/// Linear-style design tokens, the same values as the Mac app's LT.
public static class LT
{
    private static Prefs P => Prefs.Shared;
    public static bool Light => P.Appearance == Appearance.Light;

    static Color Rgb(double r, double g, double b, double a = 1) =>
        Color.FromArgb((byte)Math.Round(a * 255), (byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
    public static Color WithOpacity(this Color c, double a) => Color.FromArgb((byte)Math.Round(c.A * a), c.R, c.G, c.B);

    /// The open body. Dark is pure black, like the closed pill.
    public static Color Bg => Light ? Rgb(0.975, 0.976, 0.98) : Colors.Black;
    public static Color Surface => Light ? Rgb(0.93, 0.935, 0.945) : Rgb(0.078, 0.082, 0.09);
    public static Color SurfaceHi => Light ? Rgb(0.895, 0.9, 0.915) : Rgb(0.11, 0.114, 0.125);
    /// The ink for translucent washes and hairlines: white on dark, black on light.
    public static Color Wash => Light ? Colors.Black : Colors.White;
    public static Color Border => Wash.WithOpacity(Light ? 0.09 : 0.075);
    public static Color BorderHi => Wash.WithOpacity(Light ? 0.16 : 0.14);
    public static Color Text => Light ? Rgb(0.1, 0.105, 0.12) : Rgb(0.969, 0.973, 0.973);
    public static Color Muted => Light ? Rgb(0.42, 0.435, 0.46) : Rgb(0.541, 0.561, 0.596);
    public static Color Faint => Light ? Rgb(0.62, 0.635, 0.66) : Rgb(0.36, 0.37, 0.40);
    public static Color AccentColor => AccentOf(P.Accent);
    public static readonly Color Green = Rgb(0.30, 0.72, 0.53);
    public static readonly Color Red = Rgb(0.92, 0.36, 0.36);

    public static Color AccentOf(Accent a) => a switch
    {
        Accent.Indigo => Rgb(0.369, 0.416, 0.824),
        Accent.Blue => Rgb(0.26, 0.52, 0.96),
        Accent.Teal => Rgb(0.18, 0.70, 0.72),
        Accent.Green => Rgb(0.30, 0.72, 0.45),
        Accent.Yellow => Rgb(0.93, 0.72, 0.20),
        Accent.Orange => Rgb(0.95, 0.50, 0.22),
        Accent.Pink => Rgb(0.91, 0.36, 0.62),
        _ => Rgb(0.55, 0.57, 0.62),
    };

    // The notch's motion, tuned as a pair so it reads as one liquid gesture:
    // opening, the width leads and the height follows a beat later with a little bounce (it pours open);
    // closing is quicker and calm, height first, no bounce (it drains back up).
    public static readonly SpringSpec OpenWide = new(0.42, 0.18);
    public static readonly SpringSpec OpenTall = new(0.52, 0.22, 0.03);
    public static readonly SpringSpec CloseTall = new(0.34, 0);
    public static readonly SpringSpec CloseWide = new(0.40, 0, 0.04);
    public static readonly SpringSpec Hover = SpringSpec.Response(0.3, 0.78);
    public static readonly SpringSpec Snappy = SpringSpec.Response(0.32, 0.84);
    public static readonly SpringSpec Liquid = SpringSpec.Response(0.46, 0.74);

    public static readonly FontFamily Ui = new("fonts:Inter#Inter, $Default");
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, monospace");
    /// A text size scaled by the text-size setting.
    public static double TextSize(double size) => size * P.TextScale;
}
