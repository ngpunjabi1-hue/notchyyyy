using System.Text.Json;
using Avalonia.Media;
using Notchyyyy.Motion;

namespace Notchyyyy.Theme;

/// Test-only launch flags: they never change saved data.
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

/// Everything the user can change in Settings, saved as JSON in the user's app-data folder.
public sealed class Prefs
{
    public static Prefs Shared { get; } = Load();

    public Accent Accent { get; set; } = Accent.Indigo;
    public Appearance Appearance { get; set; } = Appearance.Dark;
    public double TextScale { get; set; } = 1;
    public double IconScale { get; set; } = 1;
    public HoverSpeed HoverSpeed { get; set; } = HoverSpeed.Normal;
    public bool Haptics { get; set; } = true;
    public bool LiveActivity { get; set; } = true;
    public List<string> HiddenTabs { get; set; } = new();
    /// Sprout beside the pill. Off until Sprout is built (plan step 4).
    public bool Buddy { get; set; } = false;

    public double HoverDelay => HoverSpeed switch { HoverSpeed.Instant => 0.03, HoverSpeed.Relaxed => 0.35, _ => 0.12 };

    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Notchyyyy");
    private static string FilePath => Path.Combine(Folder, "settings.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private static Prefs Load()
    {
        Prefs p;
        try { p = File.Exists(FilePath) ? JsonSerializer.Deserialize<Prefs>(File.ReadAllText(FilePath), Json) ?? new() : new(); }
        catch { p = new(); }
        if (Debug.Has("--light")) p.Appearance = Appearance.Light;
        if (Debug.Has("--dark")) p.Appearance = Appearance.Dark;
        return p;
    }

    public void Save()
    {
        if (Debug.Sample || Debug.Has("--light") || Debug.Has("--dark")) return;
        try
        {
            Directory.CreateDirectory(Folder);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, FilePath, overwrite: true);
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
