using System.Text.Json;
using Avalonia.Threading;
using Notchyyyy.Theme;
using SkiaSharp;

namespace Notchyyyy.Apps;

/// <summary>An app from the Start menu: its name and the id that opens it (shell:AppsFolder\id).</summary>
public sealed record AppEntry(string Id, string Name);

/// <summary>Where apps come from: the Start menu on Windows; a stand-in list on the Mac preview.</summary>
public interface IAppCatalog
{
    /// Every app, sorted by name (called off the UI thread).
    List<AppEntry> Scan();
    /// The app's icon, <paramref name="px"/> pixels square (called off the UI thread; null if there's none).
    SKImage? Icon(string id, int px);
    void Launch(string id);
}

/// <summary>
/// The pinned apps (the Mac's FavoritesStore): their order, kept in %APPDATA%\Notchyyyy\apps.json, every app for the
/// picker (scanned once, in the background), and icons, looked up once per app on a background thread.
/// </summary>
public sealed class Favorites
{
    private readonly List<string> _ids = new();
    private readonly string _file;
    private readonly bool _readOnly;
    private IAppCatalog? _catalog;
    private List<AppEntry> _installed = new();
    private readonly Dictionary<string, AppEntry> _byId = new();
    private readonly Dictionary<string, Avalonia.Media.Imaging.Bitmap?> _icons = new();
    private readonly HashSet<string> _loading = new();
    private bool _scanning, _hadFile;

    public Favorites(string? file = null, bool? readOnly = null)
    {
        _file = file ?? Path.Combine(Prefs.Folder, "apps.json");
        _readOnly = readOnly ?? (Debug.Sample || Debug.Has("--perf") || Debug.Has("--light") || Debug.Has("--dark") || Debug.Has("--win-selftest"));
    }

    public IReadOnlyList<string> Ids => _ids;
    public IReadOnlyList<AppEntry> Installed => _installed;
    public bool Scanned { get; private set; }
    public int IconPixels { get; set; } = 96;

    /// "Pinned" (the list changed), "Scanned", "Icon" (with the app's id).
    public event Action<string, string?>? Changed;

    public string Name(string id) => _byId.TryGetValue(id, out var e) ? e.Name : id.Split('\\', '!').Last();

    public void Start(IAppCatalog catalog)
    {
        _catalog = catalog;
        Load();
        ScanInBackground();
    }

    /// Scans the Start menu once, then drops pins for apps that are gone; the first time, pins a few defaults.
    public void ScanInBackground()
    {
        if (_scanning || Scanned || _catalog is not { } cat) return;
        _scanning = true;
        var t = new Thread(() =>
        {
            List<AppEntry> apps;
            try { apps = cat.Scan(); }
            catch (Exception e) { Debug.Log($"app scan failed: {e.Message}"); apps = new(); }
            Dispatcher.UIThread.Post(() => Scanned_(apps));
        }) { IsBackground = true, Name = "Notchyyyy app scan" };
        if (OperatingSystem.IsWindows()) t.SetApartmentState(ApartmentState.STA);   // the shell's objects want one
        t.Start();
    }

    private void Scanned_(List<AppEntry> apps)
    {
        _scanning = false; Scanned = true;
        _installed = apps;
        _byId.Clear();
        foreach (var a in apps) _byId[a.Id] = a;
        if (!_hadFile && _ids.Count == 0)
        {
            // the Mac pins Finder, Safari, Music, Notes, Terminal and Settings: Windows' own counterparts
            foreach (var want in Defaults)
                if (apps.FirstOrDefault(a => a.Name.Equals(want, StringComparison.OrdinalIgnoreCase)) is { } hit && !_ids.Contains(hit.Id)) _ids.Add(hit.Id);
            Save();
        }
        else if (apps.Count > 0)
        {
            int before = _ids.Count;
            _ids.RemoveAll(id => !_byId.ContainsKey(id));   // uninstalled
            if (_ids.Count != before) Save();
        }
        Changed?.Invoke("Scanned", null);
        Changed?.Invoke("Pinned", null);
    }

    public static readonly string[] Defaults = { "File Explorer", "Microsoft Edge", "Media Player", "Notepad", "Terminal", "Settings" };

    public bool IsPinned(string id) => _ids.Contains(id);
    public void Add(string id) { if (_ids.Contains(id)) return; _ids.Add(id); Save(); Changed?.Invoke("Pinned", id); }
    public void Remove(string id) { if (!_ids.Remove(id)) return; Save(); Changed?.Invoke("Pinned", id); }
    public void Toggle(string id) { if (IsPinned(id)) Remove(id); else Add(id); }

    /// Moves <paramref name="id"/> to <paramref name="index"/> (dragging while editing).
    public void Move(string id, int index)
    {
        int from = _ids.IndexOf(id);
        index = Math.Clamp(index, 0, _ids.Count - 1);
        if (from < 0 || from == index) return;
        _ids.RemoveAt(from);
        _ids.Insert(index, id);
        Save();
        Changed?.Invoke("Pinned", id);
    }

    public void Launch(string id) => _catalog?.Launch(id);

    /// The icon if it's ready; otherwise null now, and "Icon" fires once it's loaded.
    public Avalonia.Media.Imaging.Bitmap? Icon(string id)
    {
        if (_icons.TryGetValue(id, out var b)) return b;
        if (_catalog is not { } cat || !_loading.Add(id)) return null;
        int px = IconPixels;
        IconWorker.Queue(() =>
        {
            Avalonia.Media.Imaging.Bitmap? bmp = null;
            try
            {
                using var img = cat.Icon(id, px);
                if (img is not null)
                {
                    using var data = img.Encode(SKEncodedImageFormat.Png, 100);
                    using var ms = new MemoryStream(data.ToArray());
                    bmp = new Avalonia.Media.Imaging.Bitmap(ms);
                }
            }
            catch (Exception e) { Debug.Log($"icon for {id} failed: {e.Message}"); }
            Dispatcher.UIThread.Post(() => { _icons[id] = bmp; _loading.Remove(id); Changed?.Invoke("Icon", id); });
        });
        return null;
    }

    /// Icons come from one background thread (an STA on Windows, as the shell wants), one at a time.
    private static class IconWorker
    {
        private static readonly System.Collections.Concurrent.BlockingCollection<Action> Work = new();
        static IconWorker()
        {
            var t = new Thread(() => { foreach (var a in Work.GetConsumingEnumerable()) a(); }) { IsBackground = true, Name = "Notchyyyy icons" };
            if (OperatingSystem.IsWindows()) t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }
        public static void Queue(Action a) => Work.Add(a);
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_file)) return;
            _hadFile = true;
            _ids.AddRange(JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_file)) ?? new());
        }
        catch (Exception e) { Debug.Log($"pinned apps unreadable: {e.Message}"); }
    }

    private void Save()
    {
        if (_readOnly) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            var tmp = _file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_ids));
            File.Move(tmp, _file, overwrite: true);
        }
        catch (Exception e) { Debug.Log($"pinned apps not saved: {e.Message}"); }
    }
}

/// <summary>
/// The Mac preview's stand-in for the Start menu: Windows apps with drawn icons (a coloured tile and a letter), so
/// the shelf and the picker can be seen and tested. Launching does nothing.
/// </summary>
public sealed class SampleCatalog : IAppCatalog
{
    private static readonly (string Name, uint A, uint B)[] Apps =
    {
        ("Calculator", 0x4A4A4A, 0x1F1F1F), ("Calendar", 0x2F7BE0, 0x1B4FA8), ("Camera", 0x3A3A3A, 0x101010),
        ("Clock", 0x2D6FD4, 0x174A9C), ("Excel", 0x21A366, 0x107C41), ("File Explorer", 0xF8C94A, 0xE3A21A),
        ("Mail", 0x2C8CF2, 0x1561C7), ("Media Player", 0xF25C8A, 0xC21F66), ("Microsoft Edge", 0x37C6D0, 0x1A73C9),
        ("Microsoft Store", 0x3B8DEB, 0x1E5FC2), ("Notepad", 0x7DB7F0, 0x3D7FC4), ("OneNote", 0xA758D6, 0x7719AA),
        ("Outlook", 0x2A8FE6, 0x0F64B8), ("Paint", 0xF0A23B, 0xD4612C), ("Photos", 0x4FB1F2, 0x2A6FD6),
        ("PowerPoint", 0xED6C47, 0xC43E1C), ("Settings", 0x7A7F8A, 0x4B4F58), ("Snipping Tool", 0xE84E8A, 0xB22C6A),
        ("Spotify", 0x1ED760, 0x139C45), ("Task Manager", 0x3FA0E0, 0x1F6BB0), ("Teams", 0x7B83EB, 0x4B53BC),
        ("Terminal", 0x3C3C3C, 0x0C0C0C), ("Visual Studio Code", 0x3DA6F0, 0x1268B3), ("Word", 0x2B7CD3, 0x185ABD),
        ("Xbox", 0x3FB33F, 0x107C10),
    };

    public List<AppEntry> Scan() => Apps.Select(a => new AppEntry("sample:" + a.Name, a.Name)).ToList();

    public SKImage? Icon(string id, int px)
    {
        var a = Apps.FirstOrDefault(x => "sample:" + x.Name == id);
        if (a.Name is null) return null;
        using var surface = SKSurface.Create(new SKImageInfo(px, px));
        var c = surface.Canvas;
        float k = px / 48f;
        c.Scale(k);
        using (var p = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, 48),
                new[] { new SKColor(0xFF000000 | a.A), new SKColor(0xFF000000 | a.B) }, SKShaderTileMode.Clamp),
        }) c.DrawRoundRect(new SKRect(3, 3, 45, 45), 10, 10, p);
        using (var font = new SKFont(SKTypeface.FromFamilyName(null, SKFontStyle.Bold), 22))
        using (var p = new SKPaint { Color = SKColors.White, IsAntialias = true })
        {
            var letter = a.Name[..1];
            float w = font.MeasureText(letter);
            c.DrawText(letter, 24 - w / 2, 32, font, p);
        }
        return surface.Snapshot();
    }

    public void Launch(string id) { Launched.Add(id); }
    public List<string> Launched { get; } = new();
}
