using System.Reflection;
using Avalonia.Threading;
using Notchyyyy.Theme;
using Velopack;
using Velopack.Sources;

namespace Notchyyyy.Updates;

/// <summary>
/// Automatic updates (the Mac's Updates, with Velopack in place of Sparkle). Looks for a new version at launch, every
/// hour, and the moment you step away (if the last look was 20+ minutes ago); downloads it quietly; then installs
/// and restarts once the notch has been left alone for 15 seconds in a row (never mid-use). A chip in the open
/// notch's header offers to install at once; afterwards a small bubble says "Updated to …".
///
/// Where updates come from is fixed when a release is built (the NotchUpdateFeed build property: a GitHub repo's
/// address); until a release is made there is none, and nothing ever goes to the network. Settings, clipboard, tasks
/// and pins live in %APPDATA%\Notchyyyy, which updates never touch.
/// </summary>
public sealed class Updater
{
    /// The app's identity for notifications and its Start-menu shortcut (the same one the reminders use).
    public const string Aumid = "Notchyyyy.Notchyyyy";

    private readonly UpdateManager? _um;
    private readonly string? _log = Debug.Value("--update-log");
    private UpdateInfo? _ready;
    private DateTime _lastCheck = DateTime.MinValue;
    private int _quiet;
    private bool _busy;

    /// Asked before installing in the background: true when nobody is using the notch.
    public Func<bool> IsIdle { get; set; } = () => true;

    /// "Available" (a version is downloaded and waiting), "Checked" (with or without one), "Installing".
    public event Action<string>? Changed;

    public Updater()
    {
        // `--update-feed DIR` (tests: a local folder of releases) wins over the feed built into a release
        string? feed = Debug.Value("--update-feed") ?? BuiltInFeed;
        if (string.IsNullOrWhiteSpace(feed) || Debug.Sample || Debug.Has("--perf")) return;
        try
        {
            IUpdateSource source = feed.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)
                ? new GithubSource(feed, null, false)
                : new SimpleFileSource(new DirectoryInfo(feed));
            var um = new UpdateManager(source);
            if (um.IsInstalled) _um = um;
            else Log("not installed (running from a build folder): updates are off");
        }
        catch (Exception e) { Log($"updates unavailable: {e.Message}"); }
    }

    /// The feed a release was built with (empty until there's a release).
    public static string BuiltInFeed =>
        typeof(Updater).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "NotchUpdateFeed")?.Value ?? "";

    /// Updates can happen: installed, and a feed to look at.
    public bool Enabled => _um is not null;
    /// The version waiting to be installed (null: none).
    public string? Available => _ready?.TargetFullRelease.Version.ToString();
    public static string CurrentVersion => typeof(Updater).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";

    /// Starts the schedule: a look shortly after launch, then every hour; and every 5 s, whether it's quiet enough
    /// to install what's waiting.
    public void Start()
    {
        if (_um is null) return;
        Log($"running {CurrentVersion}; feed {Debug.Value("--update-feed") ?? BuiltInFeed}");
        double first = Debug.Has("--update-now") ? 2 : 30;
        DispatcherTimer.RunOnce(() => _ = CheckQuietly("launch", force: true), TimeSpan.FromSeconds(first));
        DispatcherTimer.Run(() => { _ = CheckQuietly("hourly", force: true); return true; }, TimeSpan.FromHours(1));
        DispatcherTimer.Run(() => { WaitForIdle(); return true; }, TimeSpan.FromSeconds(5));
    }

    /// You stepped away: look if it's been a while.
    public void SteppedAway() => _ = CheckQuietly("away", force: false);

    /// A background look (only with automatic updates on, nothing already waiting, and not within 20 minutes of the
    /// last unless <paramref name="force"/>).
    private async Task CheckQuietly(string reason, bool force)
    {
        if (_um is null || _busy || _ready is not null || !Prefs.Shared.AutoUpdate) return;
        if (!force && DateTime.UtcNow - _lastCheck < TimeSpan.FromMinutes(20)) return;
        await CheckAndDownload(reason);
    }

    /// Settings › Check for Updates: look now; a found update downloads, then installs right away.
    public async Task<string> CheckNow()
    {
        if (_um is null) return "Updates arrive once Notchyyyy is installed";
        if (_ready is not null) { InstallNow(); return $"Installing {Available}…"; }
        await CheckAndDownload("asked");
        if (_ready is not null) { InstallNow(); return $"Installing {Available}…"; }
        return "You're up to date";
    }

    private async Task CheckAndDownload(string reason)
    {
        if (_um is null || _busy) return;
        _busy = true;
        _lastCheck = DateTime.UtcNow;
        try
        {
            Log($"checking ({reason})");
            var info = await _um.CheckForUpdatesAsync().ConfigureAwait(false);
            if (info is null) { Log("up to date"); Dispatcher.UIThread.Post(() => Changed?.Invoke("Checked")); return; }
            Log($"found {info.TargetFullRelease.Version}; downloading");
            await _um.DownloadUpdatesAsync(info).ConfigureAwait(false);
            Log($"downloaded {info.TargetFullRelease.Version}; waiting for the notch to be idle");
            Dispatcher.UIThread.Post(() => { _ready = info; _quiet = 0; Changed?.Invoke("Available"); });
        }
        catch (Exception e) { Log($"update check failed: {e.Message}"); }
        finally { _busy = false; }
    }

    /// Installs once the notch has been left alone for 15 seconds in a row (checked every 5 s).
    private void WaitForIdle()
    {
        if (_ready is null) return;
        _quiet = IsIdle() ? _quiet + 5 : 0;
        if (_quiet >= 15) InstallNow();
    }

    /// The header chip, or Check for Updates: install what's waiting, and restart.
    public void InstallNow()
    {
        if (_um is null || _ready is not { } info) return;
        Log($"installing {info.TargetFullRelease.Version} and restarting");
        Changed?.Invoke("Installing");
        // the new version starts with the same test flags it needs to report back (none in normal use)
        var restart = _log is not null ? new[] { "--update-log", _log } : Array.Empty<string>();
        _um.ApplyUpdatesAndRestart(info.TargetFullRelease, restart);
    }

    private void Log(string line)
    {
        Debug.Log("update: " + line);
        if (_log is null) return;
        try { File.AppendAllText(_log, $"{DateTime.Now:HH:mm:ss} {line}{Environment.NewLine}"); } catch { }
    }

    // ---- install and uninstall hooks (run before anything else, in Program.Main) ----

    /// Velopack's hooks: on uninstall, take Notchyyyy off the startup list and drop its notification identity.
    /// Your data in %APPDATA%\Notchyyyy is left for you (as on the Mac).
    public static void RunHooks(string[] args)
    {
        try
        {
            var app = VelopackApp.Build().SetArgs(args);
            if (OperatingSystem.IsWindows())
                app = app.SetAppUserModelId(Aumid).OnBeforeUninstallFastCallback(_ =>
                {
                    try { Platform.Startup.Set(false); } catch { }
                    try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\AppUserModelId\{Aumid}", false); } catch { }
                });
            app.Run();
        }
        catch (Exception e) { Debug.Log($"install hooks: {e.Message}"); }
    }

    /// After an update: the version that ran before, if it was different (then the "Updated" bubble shows once).
    public static string? UpdatedFrom()
    {
        var last = Prefs.Shared.LastRunVersion;
        var now = CurrentVersion;
        if (last != now) Prefs.Shared.LastRunVersion = now;
        return !string.IsNullOrEmpty(last) && last != now ? last : null;
    }
}
