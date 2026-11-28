using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Threading;
using Notchyyyy.Theme;
using SkiaSharp;

namespace Notchyyyy.Clipboard;

/// <summary>A colour copied as text (#FF5733, FF5733, rgb(…), hsl(…)): the Mac app's ColorValue.</summary>
public sealed record ColorValue(double R, double G, double B, double A, string Original)
{
    static int B255(double v) => (int)Math.Round(v * 255, MidpointRounding.AwayFromZero);
    static string Alpha(double a) => a.ToString("0.##", CultureInfo.InvariantCulture);

    public string Hex => $"#{B255(R):X2}{B255(G):X2}{B255(B):X2}" + (A < 1 ? $"{B255(A):X2}" : "");
    public string Rgb
    {
        get
        {
            var c = $"{B255(R)}, {B255(G)}, {B255(B)}";
            return A < 1 ? $"rgba({c}, {Alpha(A)})" : $"rgb({c})";
        }
    }
    public string Hsl
    {
        get
        {
            double mx = Math.Max(R, Math.Max(G, B)), mn = Math.Min(R, Math.Min(G, B)), l = (mx + mn) / 2, d = mx - mn, h = 0, s = 0;
            if (d > 0)
            {
                s = l > 0.5 ? d / (2 - mx - mn) : d / (mx + mn);
                h = mx == R ? (G - B) / d + (G < B ? 6 : 0) : mx == G ? (B - R) / d + 2 : (R - G) / d + 4;
                h *= 60;
            }
            var c = $"{(int)Math.Round(h)}, {(int)Math.Round(s * 100)}%, {(int)Math.Round(l * 100)}%";
            return A < 1 ? $"hsla({c}, {Alpha(A)})" : $"hsl({c})";
        }
    }
    /// Light swatches get dark text, dark ones light text.
    public bool IsLight => 0.2126 * R + 0.7152 * G + 0.0722 * B > 0.6;
    public Avalonia.Media.Color Color => Avalonia.Media.Color.FromArgb((byte)B255(A), (byte)B255(R), (byte)B255(G), (byte)B255(B));

    static readonly Regex HexRe = new(@"^#?([0-9a-fA-F]{3,4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$");
    static readonly Regex NonNum = new(@"[^0-9.%]+");

    public static ColorValue? Parse(string raw)
    {
        var s = raw.Trim();
        if (s.Length == 0 || s.Length > 48 || s.Contains('\n')) return null;
        var m = HexRe.Match(s);
        if (m.Success)
        {
            var h = m.Groups[1].Value;
            bool hadHash = s.StartsWith('#');
            // without '#' only 6 or 8 digits, and only if it isn't just a word ("FACADE", "decade")
            if (!hadHash && (h.Length < 6 || !(h.Any(char.IsDigit) || h == h.ToUpperInvariant()))) return null;
            if (h.Length <= 4) h = string.Concat(h.Select(c => $"{c}{c}"));
            ulong v = ulong.Parse(h, NumberStyles.HexNumber);
            bool has = h.Length == 8;
            double r = ((v >> (has ? 24 : 16)) & 0xFF) / 255.0, g = ((v >> (has ? 16 : 8)) & 0xFF) / 255.0;
            double b = ((v >> (has ? 8 : 0)) & 0xFF) / 255.0, a = has ? (v & 0xFF) / 255.0 : 1;
            return new ColorValue(r, g, b, a, s);
        }
        var lower = s.ToLowerInvariant();
        var nums = NonNum.Replace(lower, " ").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        static double? Num(string t, double scale) =>
            t.EndsWith('%') ? (double.TryParse(t[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p / 100 : null)
                            : (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n / scale : null);
        if (lower.StartsWith("rgb") && nums.Length >= 3 && Num(nums[0], 255) is { } rr && Num(nums[1], 255) is { } gg && Num(nums[2], 255) is { } bb)
        {
            double a = nums.Length > 3 ? Num(nums[3], 1) ?? 1 : 1;
            return new ColorValue(Math.Min(1, rr), Math.Min(1, gg), Math.Min(1, bb), Math.Min(1, a), s);
        }
        if (lower.StartsWith("hsl") && nums.Length >= 3 && double.TryParse(nums[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var hh)
            && Num(nums[1], 100) is { } sat && Num(nums[2], 100) is { } l)
        {
            double c = (1 - Math.Abs(2 * l - 1)) * sat, x = c * (1 - Math.Abs(hh / 60 % 2 - 1)), mm = l - c / 2;
            var (r, g, b) = (hh % 360) switch
            {
                < 60 => (c, x, 0.0), < 120 => (x, c, 0.0), < 180 => (0.0, c, x),
                < 240 => (0.0, x, c), < 300 => (x, 0.0, c), _ => (c, 0.0, x),
            };
            double a = nums.Length > 3 ? Num(nums[3], 1) ?? 1 : 1;
            return new ColorValue(r + mm, g + mm, b + mm, Math.Min(1, a), s);
        }
        return null;
    }
}

public enum ClipKind { Text, Image, Color, Svg }

/// <summary>One copy. Images keep their PNG bytes (what's saved and copied back) and a decoded image for drawing.</summary>
public sealed class ClipItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTime Date { get; set; } = Wall.Now();
    public ClipKind Kind { get; init; }
    public string Text { get; init; } = "";          // text, or SVG code
    public ColorValue? Color { get; init; }
    public byte[]? Png { get; init; }                // images
    /// For drawing: the image, or the SVG rendered (null if it can't be).
    public SKImage? Picture { get; set; }
    public Avalonia.Media.Imaging.Bitmap? Bitmap { get; set; }
    /// An SVG's own size, for the "SVG · 24×24" label.
    public (int W, int H)? SvgSize { get; set; }

    /// What makes two copies "the same thing" (a re-copy moves it to the front instead of duplicating it).
    public string? Key => Kind switch
    {
        ClipKind.Text => "t:" + Text,
        ClipKind.Color => "c:" + Color!.Hex,
        ClipKind.Svg => "s:" + Text,
        _ => null,
    };

    public string Preview => Kind switch
    {
        ClipKind.Text => Text.Trim().Replace("\r\n", " ").Replace('\n', ' '),
        ClipKind.Image => "Image",
        ClipKind.Color => Color!.Hex,
        _ => "SVG icon",
    };

    public string Label => Kind switch
    {
        ClipKind.Text => "Text",
        ClipKind.Image => "Image",
        ClipKind.Color => Color!.A < 1 ? $"Colour · {(int)Math.Round(Color.A * 100)}%" : "Colour",
        _ => SvgSize is { } s ? $"SVG · {s.W}×{s.H}" : "SVG",
    };
}

/// <summary>
/// What's on the clipboard after a copy, read by the platform: the formats Notchyyyy cares about. The rules that
/// turn it into a card are the same everywhere (and tested on the Mac); only reading and writing are Windows-only.
/// </summary>
public sealed record ClipSnapshot(
    string? Text = null, byte[]? Png = null, string? Svg = null, IReadOnlyList<string>? Files = null,
    bool Private = false)
{
    /// The Mac's rules: SVG first, then a colour, then files, then images (unless there's real text), then text.
    public ClipItem? ToItem()
    {
        if (Private) return null;     // password managers and anything asking not to be kept
        if (Svg is { } svg && Clips.IsSvg(svg)) return new ClipItem { Kind = ClipKind.Svg, Text = svg };
        if (Text is { } t0 && Clips.IsSvg(t0)) return new ClipItem { Kind = ClipKind.Svg, Text = t0.Trim() };
        if (Text is { } t1 && ColorValue.Parse(t1) is { } c) return new ClipItem { Kind = ClipKind.Color, Color = c };
        if (Files is { Count: > 0 } files)
        {
            if (files.FirstOrDefault(f => f.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) is { } svgFile && Clips.TryRead(svgFile) is { } code && Clips.IsSvg(code))
                return new ClipItem { Kind = ClipKind.Svg, Text = code.Trim() };
            if (files.FirstOrDefault(Clips.IsImageFile) is { } imgFile && Clips.TryReadBytes(imgFile) is { } bytes && Clips.ToPng(bytes) is { } png)
                return new ClipItem { Kind = ClipKind.Image, Png = png };
            return new ClipItem { Kind = ClipKind.Text, Text = string.Join("\n", files) };
        }
        // spreadsheets and slides put a picture of the selection next to its text: real text wins (a bare link doesn't)
        var s = Text?.Trim() ?? "";
        bool textIsLink = !s.Contains(' ') && !s.Contains('\n') && (s.StartsWith("http://") || s.StartsWith("https://"));
        if (Png is { Length: > 0 } && (s.Length == 0 || textIsLink)) return new ClipItem { Kind = ClipKind.Image, Png = Png };
        if (s.Length > 0) return new ClipItem { Kind = ClipKind.Text, Text = Text! };
        return null;
    }
}

/// <summary>What the platform does: read the clipboard when it changes, and write to it.</summary>
public interface IClipboardSource
{
    /// Starts watching; <paramref name="changed"/> is called on the UI thread after each copy (not our own).
    void Start(Action<ClipSnapshot> changed);
    void WriteText(string text, string? svg = null);
    void WritePng(byte[] png);
}

/// <summary>The Mac preview: nothing to watch; copying back only remembers (tests read it).</summary>
public sealed class MemoryClipboard : IClipboardSource
{
    public string? LastText { get; private set; }
    public byte[]? LastPng { get; private set; }
    public string? LastSvg { get; private set; }
    public void Start(Action<ClipSnapshot> changed) { }
    public void WriteText(string text, string? svg = null) { LastText = text; LastSvg = svg; LastPng = null; }
    public void WritePng(byte[] png) { LastPng = png; LastText = null; LastSvg = null; }
}

/// <summary>
/// The last 40 copies (the Mac's ClipboardStore): text, images, colours and SVG icons. Kept on disk in
/// %APPDATA%\Notchyyyy\clipboard (history.json, and a PNG per image), so they survive restarts and updates.
/// </summary>
public sealed class Clips
{
    public const int Max = 40;
    private readonly List<ClipItem> _items = new();
    private IClipboardSource? _source;
    private readonly string _folder;
    private readonly bool _readOnly, _samples;
    private DispatcherTimer? _saveTimer;

    /// <paramref name="folder"/>, <paramref name="readOnly"/> and <paramref name="samples"/> are for tests; by default
    /// the history is %APPDATA%\Notchyyyy\clipboard, and `--sample` shows the samples and writes nothing.
    public Clips(string? folder = null, bool? readOnly = null, bool? samples = null)
    {
        _folder = folder ?? Path.Combine(Prefs.Folder, "clipboard");
        _samples = samples ?? Debug.Sample;
        _readOnly = readOnly ?? (Debug.Sample || Debug.Has("--perf") || Debug.Has("--light") || Debug.Has("--dark") || Debug.Has("--win-selftest"));
    }

    public IReadOnlyList<ClipItem> Items => _items;
    /// "id" or "id:format" for a moment after copying, for the ✓.
    public string? JustCopied { get; private set; }
    public IClipboardSource? Source => _source;

    /// What changed: "Inserted" (index 0), "Removed", "Moved", "Cleared", "Copied", "Loaded".
    public event Action<string, ClipItem?>? Changed;

    public void Start(IClipboardSource source)
    {
        _source = source;
        if (_samples) { foreach (var i in Samples().Reverse()) Insert(i, notify: false); Changed?.Invoke("Loaded", null); }
        else Load();
        source.Start(snap => { if (snap.ToItem() is { } item) Add(item); });
    }

    /// A new copy: to the front (a copy of something already here moves it there).
    public void Add(ClipItem item)
    {
        Prepare(item);
        if (item.Key is { } k && _items.FirstOrDefault(i => i.Key == k) is { } same)
        {
            _items.Remove(same);
            same.Date = Wall.Now();
            _items.Insert(0, same);
            Changed?.Invoke("Moved", same);
        }
        else Insert(item, notify: true);
        ScheduleSave();
    }

    private void Insert(ClipItem item, bool notify)
    {
        Prepare(item);
        _items.Insert(0, item);
        while (_items.Count > Max)
        {
            var last = _items[^1];
            _items.RemoveAt(_items.Count - 1);
            if (notify) Changed?.Invoke("Removed", last);
        }
        if (notify) Changed?.Invoke("Inserted", item);
    }

    public void Remove(ClipItem item)
    {
        if (!_items.Remove(item)) return;
        Changed?.Invoke("Removed", item);
        ScheduleSave();
    }

    public void Clear()
    {
        if (_items.Count == 0) return;
        _items.Clear();
        Changed?.Invoke("Cleared", null);
        ScheduleSave();
    }

    /// Copies an item back the way it came in; <paramref name="format"/> picks hex / rgb / hsl, or svg / png.
    public void Copy(ClipItem item, string? format = null)
    {
        var src = _source;
        switch (item.Kind)
        {
            case ClipKind.Text: src?.WriteText(item.Text); break;
            case ClipKind.Image: if (item.Png is { } png) src?.WritePng(png); break;
            case ClipKind.Color:
                var c = item.Color!;
                src?.WriteText(format switch { "rgb" => c.Rgb, "hsl" => c.Hsl, "hex" => c.Hex, _ => c.Original.Length > 0 ? c.Original : c.Hex });
                break;
            case ClipKind.Svg:
                if (format == "png" && SvgPng(item.Text) is { } svgPng) src?.WritePng(svgPng);
                else src?.WriteText(item.Text, item.Text);   // code editors read the text, design tools the SVG format
                break;
        }
        string tag = format is null ? item.Id.ToString() : $"{item.Id}:{format}";
        JustCopied = tag;
        int at = _items.IndexOf(item);
        if (at > 0) { _items.RemoveAt(at); _items.Insert(0, item); }
        item.Date = Wall.Now();
        Changed?.Invoke(at > 0 ? "Moved" : "Copied", item);
        ScheduleSave();
        DispatcherTimer.RunOnce(() => { if (JustCopied == tag) { JustCopied = null; Changed?.Invoke("Copied", item); } }, TimeSpan.FromSeconds(1.2));
    }

    // ---- kinds ----

    public static bool IsSvg(string s)
    {
        var t = s.Trim();
        if (t.Length >= 512_000) return false;
        var head = t[..Math.Min(400, t.Length)].ToLowerInvariant();
        return (head.StartsWith("<svg") || (head.StartsWith("<?xml") && head.Contains("<svg"))) && t.EndsWith("</svg>", StringComparison.OrdinalIgnoreCase);
    }

    static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff" };
    public static bool IsImageFile(string f) => ImageExts.Contains(Path.GetExtension(f));
    internal static string? TryRead(string f) { try { return new FileInfo(f).Length < 512_000 ? File.ReadAllText(f) : null; } catch { return null; } }
    internal static byte[]? TryReadBytes(string f) { try { return new FileInfo(f).Length < 40_000_000 ? File.ReadAllBytes(f) : null; } catch { return null; } }

    /// Any image bytes as PNG (what's kept and copied back).
    public static byte[]? ToPng(byte[] bytes)
    {
        try
        {
            using var bmp = SKBitmap.Decode(bytes);
            if (bmp is null) return null;
            using var img = SKImage.FromBitmap(bmp);
            using var data = img.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }
        catch { return null; }
    }

    /// Decodes what each card draws: images, and SVGs rendered.
    private static void Prepare(ClipItem item)
    {
        if (item.Picture is not null) return;
        try
        {
            if (item.Kind == ClipKind.Image && item.Png is { } png)
            {
                using var bmp = SKBitmap.Decode(png);
                if (bmp is null) return;
                // cards are 156 × 138 pt: 600 px is sharp at 3x and keeps memory small for 40 images
                SKBitmap use = bmp; SKBitmap? small = null;
                int max = Math.Max(bmp.Width, bmp.Height);
                if (max > 600)
                {
                    double k = 600.0 / max;
                    small = bmp.Resize(new SKImageInfo(Math.Max(1, (int)(bmp.Width * k)), Math.Max(1, (int)(bmp.Height * k))), new SKSamplingOptions(SKCubicResampler.Mitchell));
                    use = small ?? bmp;
                }
                item.Picture = SKImage.FromBitmap(use);
                item.Bitmap = ToBitmap(item.Picture);
                small?.Dispose();
            }
            else if (item.Kind == ClipKind.Svg)
            {
                var (pic, w, h) = RenderSvg(item.Text, 256);
                if (pic is not null) { item.Picture = pic; item.Bitmap = ToBitmap(pic); item.SvgSize = (w, h); }
            }
        }
        catch (Exception e) { Debug.Log($"clipboard preview failed: {e.Message}"); }
    }

    private static Avalonia.Media.Imaging.Bitmap ToBitmap(SKImage img)
    {
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        using var ms = new MemoryStream(data.ToArray());
        return new Avalonia.Media.Imaging.Bitmap(ms);
    }

    /// Renders SVG code to an image <paramref name="px"/> pixels on its longer side; also returns its own size.
    /// Icons drawn in "currentColor" come out black, as on the Mac.
    public static (SKImage? Image, int W, int H) RenderSvg(string code, int px)
    {
        try
        {
            using var svg = new Svg.Skia.SKSvg();
            using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(code));
            var pic = svg.Load(ms);
            if (pic is null) return (null, 0, 0);
            var b = pic.CullRect;
            if (b.Width <= 0 || b.Height <= 0) return (null, 0, 0);
            float k = px / Math.Max(b.Width, b.Height);
            int w = Math.Max(1, (int)Math.Round(b.Width * k)), h = Math.Max(1, (int)Math.Round(b.Height * k));
            using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
            surface.Canvas.Clear(SKColors.Transparent);
            surface.Canvas.Scale(k);
            surface.Canvas.Translate(-b.Left, -b.Top);
            surface.Canvas.DrawPicture(pic);
            return (surface.Snapshot(), (int)Math.Round(b.Width), (int)Math.Round(b.Height));
        }
        catch { return (null, 0, 0); }
    }

    /// An SVG as a crisp PNG (at least 256 px, 4× its own size).
    public static byte[]? SvgPng(string code)
    {
        var (img0, w, h) = RenderSvg(code, 24);
        img0?.Dispose();
        if (w == 0) return null;
        int px = (int)Math.Max(256, Math.Max(w, h) * 4);
        var (img, _, _) = RenderSvg(code, px);
        if (img is null) return null;
        using (img)
        using (var data = img.Encode(SKEncodedImageFormat.Png, 100)) return data.ToArray();
    }

    // ---- kept on disk ----

    private sealed record Saved(Guid Id, DateTime Date, string Kind, string? Value);

    private string IndexFile => Path.Combine(_folder, "history.json");
    private string ImageFile(Guid id) => Path.Combine(_folder, id + ".png");

    private void Load()
    {
        try
        {
            if (!File.Exists(IndexFile)) return;
            var saved = JsonSerializer.Deserialize<List<Saved>>(File.ReadAllText(IndexFile)) ?? new();
            foreach (var e in saved.AsEnumerable().Reverse())
            {
                ClipItem? item = e.Kind switch
                {
                    "text" when e.Value is { } v => new ClipItem { Id = e.Id, Date = e.Date, Kind = ClipKind.Text, Text = v },
                    "color" when e.Value is { } v && ColorValue.Parse(v) is { } c => new ClipItem { Id = e.Id, Date = e.Date, Kind = ClipKind.Color, Color = c },
                    "svg" when e.Value is { } v => new ClipItem { Id = e.Id, Date = e.Date, Kind = ClipKind.Svg, Text = v },
                    "image" when File.Exists(ImageFile(e.Id)) => new ClipItem { Id = e.Id, Date = e.Date, Kind = ClipKind.Image, Png = File.ReadAllBytes(ImageFile(e.Id)) },
                    _ => null,
                };
                if (item is not null) Insert(item, notify: false);
            }
            Changed?.Invoke("Loaded", null);
        }
        catch (Exception e) { Debug.Log($"clipboard history unreadable: {e.Message}"); }
    }

    /// Writes 0.6 s after a change (a burst of changes is one write), off the UI thread.
    private void ScheduleSave()
    {
        if (_readOnly) return;
        _saveTimer?.Stop();
        _saveTimer = new DispatcherTimer(TimeSpan.FromSeconds(0.6), DispatcherPriority.Background, (_, _) => { _saveTimer?.Stop(); SaveNow(); });
        _saveTimer.Start();
    }

    /// Saves right away (tests; quitting).
    public Task SaveNow()
    {
        if (_readOnly) return Task.CompletedTask;
        var list = _items.ToList();
        var folder = _folder;
        return Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(folder);
                var entries = list.Select(i => new Saved(i.Id, i.Date, i.Kind switch
                {
                    ClipKind.Text => "text", ClipKind.Color => "color", ClipKind.Svg => "svg", _ => "image",
                }, i.Kind switch { ClipKind.Text or ClipKind.Svg => i.Text, ClipKind.Color => i.Color!.Hex, _ => null })).ToList();
                foreach (var i in list.Where(i => i.Kind == ClipKind.Image && i.Png is not null && !File.Exists(ImageFile(i.Id))))
                    WriteAtomic(ImageFile(i.Id), i.Png!);
                WriteAtomic(IndexFile, JsonSerializer.SerializeToUtf8Bytes(entries));
                // images no longer in the list are deleted
                var keep = list.Where(i => i.Kind == ClipKind.Image).Select(i => i.Id + ".png").ToHashSet();
                foreach (var f in Directory.GetFiles(folder, "*.png"))
                    if (!keep.Contains(Path.GetFileName(f))) File.Delete(f);
            }
            catch (Exception e) { Debug.Log($"clipboard history not saved: {e.Message}"); }
        });
    }

    private static void WriteAtomic(string path, byte[] data)
    {
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, data);
        File.Move(tmp, path, overwrite: true);
    }

    // ---- samples (--sample), the Mac's ----

    public static IEnumerable<ClipItem> Samples()
    {
        const string star = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"24\" height=\"24\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><path d=\"M12 2l3.09 6.26L22 9.27l-5 4.87 1.18 6.88L12 17.77l-6.18 3.25L7 14.14 2 9.27l6.91-1.01L12 2z\"/></svg>";
        var now = Wall.Now();
        byte[] img;
        using (var surface = SKSurface.Create(new SKImageInfo(320, 200)))
        {
            using var p = new SKPaint
            {
                Shader = SKShader.CreateLinearGradient(new SKPoint(0, 200), new SKPoint(320, 0),
                    new[] { new SKColor(51, 89, 230), new SKColor(179, 77, 204) }, SKShaderTileMode.Clamp),
            };
            surface.Canvas.DrawRect(0, 0, 320, 200, p);
            using var snap = surface.Snapshot();
            using var data = snap.Encode(SKEncodedImageFormat.Png, 100);
            img = data.ToArray();
        }
        return new[]
        {
            new ClipItem { Date = now, Kind = ClipKind.Color, Color = ColorValue.Parse("#5E6AD2") },
            new ClipItem { Date = now.AddSeconds(-60), Kind = ClipKind.Svg, Text = star },
            new ClipItem { Date = now.AddSeconds(-90), Kind = ClipKind.Color, Color = ColorValue.Parse("rgb(242, 201, 76)") },
            new ClipItem { Date = now.AddSeconds(-120), Kind = ClipKind.Image, Png = img },
            new ClipItem { Date = now.AddSeconds(-600), Kind = ClipKind.Text, Text = "Personally the kid is ready to take on more responsibilities and be ready for the outer world." },
            new ClipItem { Date = now.AddSeconds(-3600), Kind = ClipKind.Text, Text = "npm install -g @openai/codex" },
        };
    }

    /// "now", "5m", "3h", "2d" (the Mac's relative()).
    public static string Relative(DateTime d)
    {
        int s = (int)(Wall.Now() - d).TotalSeconds;
        return s < 60 ? "now" : s < 3600 ? $"{s / 60}m" : s < 86400 ? $"{s / 3600}h" : $"{s / 86400}d";
    }
}
