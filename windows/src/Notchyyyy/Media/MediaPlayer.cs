using Avalonia.Media;
using Avalonia.Threading;
using Notchyyyy.Motion;
using Notchyyyy.Theme;
using SkiaSharp;

namespace Notchyyyy.Media;

public enum RepeatMode { Off, All, One }

/// <summary>What a media source reports: one complete picture of the current track (null = nothing playing).</summary>
public sealed record MediaReport(
    string Title, string Artist, string Album, string AppName, string AppId,
    bool Playing, double Duration, double Position, DateTimeOffset PositionStamp,
    string ArtKey, Func<Task<byte[]?>>? LoadArt,
    bool CanSeek, bool CanShuffle, bool CanRepeat, bool Shuffle, RepeatMode Repeat);

/// <summary>Where tracks come from: Windows' media sessions (SMTC), or the demo track on the Mac preview.</summary>
public interface IMediaSource
{
    /// Starts watching; <paramref name="report"/> may be called on any thread.
    void Start(Action<MediaReport?> report);
    Task TogglePlay();
    Task Next();
    Task Previous();
    Task Seek(double seconds);
    Task SetShuffle(bool on);
    Task SetRepeat(RepeatMode mode);
    /// Brings the playing app forward (or, with nothing playing, opens a music app).
    void Open();
    /// The empty state's button: what <see cref="Open"/> opens when nothing is playing.
    string OpenLabel { get; }
    /// Shown when the source can't be reached at all (null when it's fine).
    string? Unavailable { get; }
}

/// <summary>
/// The one media player the app shows (the Mac app's MusicController): the current track, where it is, its artwork
/// with the artwork's own colours, and the commands. Lives on the UI thread; sources report from anywhere.
/// </summary>
public sealed class MediaPlayer
{
    private IMediaSource? _source;
    private double _holdUntil = double.NegativeInfinity;
    private string _artKey = "";
    private int _artRequest;

    public MediaPlayer(IClock clock) => Clock = clock;

    /// The app's clock (the stage hands its own over, so positions and animations share one timeline).
    public IClock Clock { get; set; }
    private IClock _clock => Clock;

    public bool HasTrack { get; private set; }
    public bool IsPlaying { get; private set; }
    public string Title { get; private set; } = "";
    public string Artist { get; private set; } = "";
    public string Album { get; private set; } = "";
    public string AppName { get; private set; } = "";
    public string AppId { get; private set; } = "";
    public double Duration { get; private set; }
    /// Where the track was at <see cref="PositionAt"/> (clock seconds).
    public double Position { get; private set; }
    public double PositionAt { get; private set; }
    public bool CanSeek { get; private set; }
    public bool CanShuffle { get; private set; }
    public bool CanRepeat { get; private set; }
    public bool Shuffle { get; private set; }
    public RepeatMode Repeat { get; private set; }
    /// The artwork, for drawing on the render thread (Skia) and in controls (Avalonia).
    public SKImage? ArtImage { get; private set; }
    public Avalonia.Media.Imaging.Bitmap? Art { get; private set; }
    /// The artwork's own colour, lifted so it reads on black (the accent when there's no artwork).
    public Color Tint { get; private set; } = LT.AccentColor;
    /// The artwork shrunk to 3 × 3: nine colours that keep its layout (empty without artwork).
    public Color[] Palette { get; private set; } = Array.Empty<Color>();
    public string? Unavailable => _source?.Unavailable;
    public string OpenLabel => _source?.OpenLabel ?? "Open Media Player";

    /// What changed: "Track", "Playing", "Position", "Art", "Modes".
    public event Action<string>? Changed;

    public void Start(IMediaSource source)
    {
        _source = source;
        source.Start(r => Dispatcher.UIThread.Post(() => Apply(r)));
    }

    /// Position now, moving forward between reports so the scrubber glides.
    public double LivePosition(double now)
    {
        if (!IsPlaying) return Position;
        double p = Position + Math.Max(0, now - PositionAt);
        return Duration > 0 ? Math.Min(Duration, p) : p;
    }

    // ---- reports ----

    public void Apply(MediaReport? r)
    {
        if (r is null || r.Title.Length == 0) { Clear(); return; }
        double now = _clock.Now;
        bool track = !HasTrack || r.Title != Title || r.Artist != Artist || r.AppId != AppId;
        HasTrack = true;
        Title = r.Title; Artist = r.Artist; Album = r.Album; AppName = r.AppName; AppId = r.AppId;
        Duration = Math.Max(0, r.Duration);
        CanSeek = r.CanSeek && Duration > 0;
        bool modes = CanShuffle != r.CanShuffle || CanRepeat != r.CanRepeat || Shuffle != r.Shuffle || Repeat != r.Repeat;
        CanShuffle = r.CanShuffle; CanRepeat = r.CanRepeat;
        // right after a command, the player's own report can still be the old state: keep ours for a moment
        bool playingChanged = false;
        // (a new track always shows as it is: there's nothing of ours to keep)
        if (now >= _holdUntil || track)
        {
            Shuffle = r.Shuffle; Repeat = r.Repeat;
            playingChanged = IsPlaying != r.Playing;
            IsPlaying = r.Playing;
            // the position was measured at its stamp: carry it forward to now while playing
            double drift = r.Playing ? Math.Max(0, (DateTimeOffset.Now - r.PositionStamp).TotalSeconds) : 0;
            if (r.PositionStamp == default) drift = 0;
            double pos = r.Position + drift;
            Position = Duration > 0 ? Math.Clamp(pos, 0, Duration) : Math.Max(0, pos);
            PositionAt = now;
        }
        if (track) Changed?.Invoke("Track");
        if (playingChanged) Changed?.Invoke("Playing");
        if (modes) Changed?.Invoke("Modes");
        Changed?.Invoke("Position");

        if (r.ArtKey != _artKey)
        {
            _artKey = r.ArtKey;
            int request = ++_artRequest;
            if (r.LoadArt is null) SetArt(null);
            else _ = LoadArt(r.LoadArt, request);
        }
    }

    private async Task LoadArt(Func<Task<byte[]?>> load, int request)
    {
        Decoded? d = null;
        try
        {
            var bytes = await load().ConfigureAwait(false);
            if (bytes is { Length: > 0 }) d = await Task.Run(() => Decode(bytes)).ConfigureAwait(false);
        }
        catch { d = null; }
        Dispatcher.UIThread.Post(() => { if (request == _artRequest) SetArt(d); });
    }

    private void Clear()
    {
        if (!HasTrack) return;
        HasTrack = false; IsPlaying = false;
        Title = Artist = Album = AppName = AppId = "";
        Duration = Position = 0; CanSeek = CanShuffle = CanRepeat = false;
        _artKey = ""; _artRequest++;
        SetArt(null, notify: false);
        Changed?.Invoke("Track");
        Changed?.Invoke("Playing");
        Changed?.Invoke("Art");
    }

    private sealed record Decoded(SKImage Image, Avalonia.Media.Imaging.Bitmap Bitmap, Color Tint, Color[] Palette);

    private void SetArt(Decoded? d, bool notify = true)
    {
        ArtImage = d?.Image; Art = d?.Bitmap;
        Tint = d?.Tint ?? LT.AccentColor;
        Palette = d?.Palette ?? Array.Empty<Color>();
        if (notify) Changed?.Invoke("Art");
    }

    /// Decodes artwork and samples its colours (off the UI thread).
    private static Decoded? Decode(byte[] bytes)
    {
        using var bmp = SKBitmap.Decode(bytes);
        if (bmp is null || bmp.Width < 1 || bmp.Height < 1) return null;
        // keep it small: it's drawn at most ~160 pt, so 512 px covers 3x displays
        SKBitmap use = bmp;
        SKBitmap? scaled = null;
        int max = Math.Max(bmp.Width, bmp.Height);
        if (max > 512)
        {
            double k = 512.0 / max;
            scaled = bmp.Resize(new SKImageInfo(Math.Max(1, (int)(bmp.Width * k)), Math.Max(1, (int)(bmp.Height * k))),
                                new SKSamplingOptions(SKCubicResampler.Mitchell));
            use = scaled ?? bmp;
        }
        var image = SKImage.FromBitmap(use);
        using var ms = new MemoryStream();
        using (var data = image.Encode(SKEncodedImageFormat.Png, 100)) data.SaveTo(ms);
        ms.Position = 0;
        var bitmap = new Avalonia.Media.Imaging.Bitmap(ms);
        var (tint, palette) = Colours(use);
        scaled?.Dispose();
        return new Decoded(image, bitmap, tint, palette);
    }

    /// The Mac's averageColor and palette: box averages over the whole image and over a 3 × 3 grid of it.
    public static (Color Tint, Color[] Palette) Colours(SKBitmap b)
    {
        var sum = new double[9, 4];
        var all = new double[4];
        for (int y = 0; y < b.Height; y++)
        {
            int gy = Math.Min(2, y * 3 / b.Height);
            for (int x = 0; x < b.Width; x++)
            {
                var c = b.GetPixel(x, y);
                int g = gy * 3 + Math.Min(2, x * 3 / b.Width);
                sum[g, 0] += c.Red; sum[g, 1] += c.Green; sum[g, 2] += c.Blue; sum[g, 3]++;
                all[0] += c.Red; all[1] += c.Green; all[2] += c.Blue; all[3]++;
            }
        }
        var palette = new Color[9];
        for (int i = 0; i < 9; i++)
        {
            double n = Math.Max(1, sum[i, 3]);
            var (h, s, v) = Hsv(sum[i, 0] / n / 255, sum[i, 1] / n / 255, sum[i, 2] / n / 255);
            palette[i] = FromHsv(h, Math.Min(1, s * 1.3 + 0.05), Math.Min(1, Math.Max(0.35, v * 1.15)));
        }
        double an = Math.Max(1, all[3]);
        var (th, ts, tv) = Hsv(all[0] / an / 255, all[1] / an / 255, all[2] / an / 255);
        return (FromHsv(th, Math.Min(1, ts * 1.25 + 0.1), Math.Max(0.75, tv)), palette);
    }

    private static (double H, double S, double V) Hsv(double r, double g, double b)
    {
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = d == 0 ? 0 : max == r ? (g - b) / d % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        h /= 6; if (h < 0) h += 1;
        return (h, max == 0 ? 0 : d / max, max);
    }

    public static Color FromHsv(double h, double s, double v)
    {
        double i = Math.Floor(h * 6), f = h * 6 - i, p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
        var (r, g, b) = ((int)i % 6) switch { 0 => (v, t, p), 1 => (q, v, p), 2 => (p, v, t), 3 => (p, q, v), 4 => (t, p, v), _ => (v, p, q) };
        return Color.FromRgb((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
    }

    // ---- commands ----

    private void Hold() => _holdUntil = _clock.Now + 0.8;

    public void PlayPause()
    {
        if (!HasTrack) return;
        double now = _clock.Now;
        Position = LivePosition(now); PositionAt = now;   // freeze where it is *before* flipping the state
        IsPlaying = !IsPlaying;
        Hold();
        Changed?.Invoke("Playing");
        Run(_source?.TogglePlay());
    }

    public void Next() { Hold(); Run(_source?.Next()); }
    public void Previous() { Hold(); Run(_source?.Previous()); }

    public void Seek(double seconds)
    {
        if (!CanSeek) return;
        Position = Math.Clamp(seconds, 0, Duration); PositionAt = _clock.Now;
        Hold();
        Changed?.Invoke("Position");
        Run(_source?.Seek(Position));
    }

    public void ToggleShuffle()
    {
        if (!CanShuffle) return;
        Shuffle = !Shuffle; Hold();
        Changed?.Invoke("Modes");
        Run(_source?.SetShuffle(Shuffle));
    }

    public void CycleRepeat()
    {
        if (!CanRepeat) return;
        Repeat = Repeat switch { RepeatMode.Off => RepeatMode.All, RepeatMode.All => RepeatMode.One, _ => RepeatMode.Off };
        Hold();
        Changed?.Invoke("Modes");
        Run(_source?.SetRepeat(Repeat));
    }

    public void Open() => _source?.Open();

    private static void Run(Task? t) => t?.ContinueWith(x => _ = x.Exception, TaskContinuationOptions.OnlyOnFaulted);
}

/// <summary>
/// `--demo` (and the Mac preview's only source): a fake track with generated artwork, to see the player without
/// anything playing. Its controls work: play/pause, next/previous through three tracks, seek, shuffle and repeat.
/// </summary>
public sealed class DemoMediaSource : IMediaSource
{
    private sealed record Track(string Title, string Artist, string App, double Duration, SKColor A, SKColor B);
    private static readonly Track[] Tracks =
    {
        new("The Best Hide & Seek Mod with 100x Structures in One Night", "BLOX", "Google Chrome", 2017, new SKColor(242, 115, 64), new SKColor(140, 38, 115)),
        new("Weightless", "Marconi Union", "Spotify", 488, new SKColor(64, 150, 220), new SKColor(20, 40, 110)),
        new("Sunflower", "Rex Orange County", "Spotify", 252, new SKColor(250, 205, 70), new SKColor(60, 160, 90)),
    };

    private readonly bool _playing;
    private Action<MediaReport?>? _report;
    private int _i;
    private bool _isPlaying;
    private double _pos = 82;
    private DateTimeOffset _stamp = DateTimeOffset.Now;
    private bool _shuffle;
    private RepeatMode _repeat;

    public DemoMediaSource(bool playing = true) { _playing = playing; _isPlaying = playing; }

    public string OpenLabel => "Open Media Player";
    public string? Unavailable => null;

    public void Start(Action<MediaReport?> report) { _report = report; Report(); }

    private double Now => _isPlaying ? _pos + (DateTimeOffset.Now - _stamp).TotalSeconds : _pos;

    private void Report()
    {
        var t = Tracks[_i];
        _report?.Invoke(new MediaReport(t.Title, t.Artist, "", t.App, t.App.Replace(" ", ""), _isPlaying, t.Duration,
            Math.Min(t.Duration, Now), DateTimeOffset.Now, "demo" + _i, () => Task.FromResult<byte[]?>(Art(t)),
            true, true, true, _shuffle, _repeat));
        _pos = Math.Min(t.Duration, Now); _stamp = DateTimeOffset.Now;
    }

    /// A diagonal gradient, like the Mac's demo artwork.
    private static byte[] Art(Track t)
    {
        using var surface = SKSurface.Create(new SKImageInfo(320, 320));
        using var paint = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(new SKPoint(0, 320), new SKPoint(320, 0), new[] { t.A, t.B }, SKShaderTileMode.Clamp),
        };
        surface.Canvas.DrawRect(0, 0, 320, 320, paint);
        using var img = surface.Snapshot();
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public Task TogglePlay() { _pos = Now; _stamp = DateTimeOffset.Now; _isPlaying = !_isPlaying; Report(); return Task.CompletedTask; }
    public Task Next() { _i = (_i + 1) % Tracks.Length; _pos = 0; _stamp = DateTimeOffset.Now; Report(); return Task.CompletedTask; }
    public Task Previous()
    {
        if (Now > 3) _pos = 0; else { _i = (_i + Tracks.Length - 1) % Tracks.Length; _pos = 0; }
        _stamp = DateTimeOffset.Now; Report(); return Task.CompletedTask;
    }
    public Task Seek(double s) { _pos = s; _stamp = DateTimeOffset.Now; Report(); return Task.CompletedTask; }
    public Task SetShuffle(bool on) { _shuffle = on; Report(); return Task.CompletedTask; }
    public Task SetRepeat(RepeatMode m) { _repeat = m; Report(); return Task.CompletedTask; }
    public void Open() { }
}

/// <summary>Nothing to play from (the Mac preview without `--demo`): the empty state.</summary>
public sealed class NoMediaSource : IMediaSource
{
    public void Start(Action<MediaReport?> report) => report(null);
    public Task TogglePlay() => Task.CompletedTask;
    public Task Next() => Task.CompletedTask;
    public Task Previous() => Task.CompletedTask;
    public Task Seek(double seconds) => Task.CompletedTask;
    public Task SetShuffle(bool on) => Task.CompletedTask;
    public Task SetRepeat(RepeatMode mode) => Task.CompletedTask;
    public void Open() { }
    public string OpenLabel => "Open Media Player";
    public string? Unavailable => null;
}
