#if WINDOWS
using System.Diagnostics;
using System.Runtime.InteropServices;
using Notchyyyy.Media;
using Windows.Media.Control;
using WinRepeat = Windows.Media.MediaPlaybackAutoRepeatMode;

namespace Notchyyyy.Platform;

/// <summary>
/// Whatever is playing on Windows (Spotify, Media Player, YouTube in Edge or Chrome, …), read from the system's
/// media sessions: the same ones the volume flyout shows (GlobalSystemMediaTransportControlsSessionManager).
/// Follows the session Windows calls current. Reports on a thread-pool thread; the player moves them to the UI.
/// </summary>
public sealed class SmtcSource : IMediaSource
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private Action<MediaReport?>? _report;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _pending;

    public string? Unavailable { get; private set; }
    public string OpenLabel => "Open Media Player";

    public void Start(Action<MediaReport?> report)
    {
        _report = report;
        _ = Connect();
    }

    private async Task Connect()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += (_, _) => Attach(_manager.GetCurrentSession());
            _manager.SessionsChanged += (_, _) => Attach(_manager.GetCurrentSession());
            Unavailable = null;
            Attach(_manager.GetCurrentSession());
        }
        catch (Exception e)
        {
            Unavailable = "Windows media sessions aren't available here";
            Theme.Debug.Log($"SMTC unavailable: {e.Message}");
            _report?.Invoke(null);
        }
    }

    private void Attach(GlobalSystemMediaTransportControlsSession? s)
    {
        lock (this)
        {
            if (ReferenceEquals(s, _session)) { Push(); return; }
            if (_session is { } old)
            {
                old.MediaPropertiesChanged -= OnMedia;
                old.PlaybackInfoChanged -= OnPlayback;
                old.TimelinePropertiesChanged -= OnTimeline;
            }
            _session = s;
            if (s is not null)
            {
                s.MediaPropertiesChanged += OnMedia;
                s.PlaybackInfoChanged += OnPlayback;
                s.TimelinePropertiesChanged += OnTimeline;
            }
        }
        Push();
    }

    private void OnMedia(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) => Push();
    private void OnPlayback(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) => Push();
    private void OnTimeline(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs e) => Push();

    /// Reads the session and reports it. Bursts of events (a track change fires all three) collapse into one read.
    private void Push()
    {
        if (Interlocked.Exchange(ref _pending, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(40);
            await _gate.WaitAsync();
            try
            {
                Interlocked.Exchange(ref _pending, 0);
                _report?.Invoke(await Read());
            }
            catch (Exception e) { Theme.Debug.Log($"SMTC read failed: {e.Message}"); }
            finally { _gate.Release(); }
        });
    }

    private async Task<MediaReport?> Read()
    {
        var s = _session;
        if (s is null) return null;
        var props = await s.TryGetMediaPropertiesAsync();
        if (props is null || string.IsNullOrWhiteSpace(props.Title)) return null;
        var pb = s.GetPlaybackInfo();
        var tl = s.GetTimelineProperties();
        double duration = Math.Max(0, (tl.EndTime - tl.StartTime).TotalSeconds);
        double position = Math.Max(0, (tl.Position - tl.StartTime).TotalSeconds);
        var c = pb.Controls;
        string aumid = s.SourceAppUserModelId ?? "";
        var thumb = props.Thumbnail;
        string artKey = thumb is null ? "" : $"{aumid}|{props.Title}|{props.Artist}|{props.AlbumTitle}";
        return new MediaReport(
            props.Title, props.Artist ?? "", props.AlbumTitle ?? "", AppName(aumid), aumid,
            pb.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
            duration, position, tl.LastUpdatedTime,
            artKey, thumb is null ? null : () => ReadThumbnail(thumb),
            c.IsPlaybackPositionEnabled && duration > 0, c.IsShuffleEnabled, c.IsRepeatEnabled,
            pb.IsShuffleActive ?? false,
            pb.AutoRepeatMode switch { WinRepeat.List => RepeatMode.All, WinRepeat.Track => RepeatMode.One, _ => RepeatMode.Off });
    }

    private static async Task<byte[]?> ReadThumbnail(Windows.Storage.Streams.IRandomAccessStreamReference r)
    {
        using var stream = await r.OpenReadAsync();
        using var s = System.IO.WindowsRuntimeStreamExtensions.AsStreamForRead(stream);
        using var ms = new MemoryStream();
        await s.CopyToAsync(ms);
        return ms.ToArray();
    }

    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["spotify"] = "Spotify", ["chrome"] = "Google Chrome", ["msedge"] = "Microsoft Edge", ["firefox"] = "Firefox",
        ["brave"] = "Brave", ["opera"] = "Opera", ["vlc"] = "VLC", ["foobar2000"] = "foobar2000", ["itunes"] = "iTunes",
        ["AppleMusic"] = "Apple Music", ["Microsoft.ZuneMusic"] = "Media Player", ["Microsoft.ZuneVideo"] = "Films & TV",
        ["SpotifyAB.SpotifyMusic"] = "Spotify", ["308046B0AF4A39CB"] = "Firefox", ["MSEdge"] = "Microsoft Edge",
    };

    /// A person-friendly name for the app behind a session: "Spotify.exe" → Spotify, a Store app's id → its name.
    public static string AppName(string aumid)
    {
        if (aumid.Length == 0) return "";
        string bare = aumid.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? aumid[..^4] : aumid;
        if (Known.TryGetValue(bare, out var k)) return k;
        string family = bare.Split('!')[0].Split('_')[0];
        if (Known.TryGetValue(family, out k)) return k;
        try
        {
            if (Windows.Foundation.Metadata.ApiInformation.IsMethodPresent("Windows.ApplicationModel.AppInfo", "GetFromAppUserModelId"))
            {
                var name = Windows.ApplicationModel.AppInfo.GetFromAppUserModelId(aumid)?.DisplayInfo?.DisplayName;
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
        }
        catch { }
        // "Microsoft.ZuneMusic" → "ZuneMusic"; "foo.exe" → "Foo"
        string last = family.Split('.')[^1];
        return last.Length > 0 ? char.ToUpperInvariant(last[0]) + last[1..] : aumid;
    }

    public Task TogglePlay() => Do(s => s.TryTogglePlayPauseAsync().AsTask());
    public Task Next() => Do(s => s.TrySkipNextAsync().AsTask());
    public Task Previous() => Do(s => s.TrySkipPreviousAsync().AsTask());
    public Task Seek(double seconds) => Do(s =>
    {
        var start = s.GetTimelineProperties().StartTime;
        return s.TryChangePlaybackPositionAsync((start + TimeSpan.FromSeconds(seconds)).Ticks).AsTask();
    });
    public Task SetShuffle(bool on) => Do(s => s.TryChangeShuffleActiveAsync(on).AsTask());
    public Task SetRepeat(RepeatMode m) => Do(s => s.TryChangeAutoRepeatModeAsync(
        m switch { RepeatMode.All => WinRepeat.List, RepeatMode.One => WinRepeat.Track, _ => WinRepeat.None }).AsTask());

    private async Task Do(Func<GlobalSystemMediaTransportControlsSession, Task<bool>> act)
    {
        if (_session is not { } s) return;
        try { await act(s); } catch (Exception e) { Theme.Debug.Log($"SMTC command failed: {e.Message}"); }
        Push();
    }

    /// Brings the playing app forward; with nothing playing, opens Media Player.
    public void Open()
    {
        string aumid = _session?.SourceAppUserModelId ?? "";
        try
        {
            if (aumid.Length == 0) { Process.Start(new ProcessStartInfo("mswindowsmusic:") { UseShellExecute = true }); return; }
            if (aumid.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var p in Process.GetProcessesByName(aumid[..^4]))
                    if (p.MainWindowHandle != IntPtr.Zero)
                    {
                        if (IsIconic(p.MainWindowHandle)) ShowWindow(p.MainWindowHandle, 9);   // SW_RESTORE
                        SetForegroundWindow(p.MainWindowHandle);
                        return;
                    }
                return;
            }
            Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{aumid}") { UseShellExecute = false });
        }
        catch (Exception e) { Theme.Debug.Log($"Open failed: {e.Message}"); }
    }

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
}
#endif
