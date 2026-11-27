using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Notchyyyy.Motion;
using Notchyyyy.Notch;
using Notchyyyy.Platform;
using Notchyyyy.Theme;

namespace Notchyyyy;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Theme.Debug.Args = args;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}

public sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = LT.Light ? ThemeVariant.Light : ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d)
            d.MainWindow = new Shell().Window;
        base.OnFrameworkInitializationCompleted();
    }
}

/// <summary>Builds the notch and its window, runs the hover tracker, and handles the debug flags.</summary>
public sealed class Shell
{
    public const double StageWidth = 820, StageHeight = 430;

    public NotchModel Model { get; } = new();
    public IClock Clock { get; } = new SystemClock();
    public NotchStage Stage { get; }
    public HoverTracker Tracker { get; }
    public Window Window { get; }
    /// When each pointer tick ran (self-tests read it to report timer health).
    public List<double> TickTimes { get; } = new();
    /// Set by self-tests: stands in for the real pointer.
    public Point? FakePointer { get; set; }
    public bool UseFakePointer { get; set; }

    private readonly IPointerSource _pointer = PointerSources.ForThisOS();
    /// On this Mac the top of the screen is under the hardware notch, so the app shows itself in a framed
    /// stand-in for a Windows desktop instead (`--preview`, the default on macOS; `--overlay` forces the real thing).
    private readonly bool _preview = Theme.Debug.Has("--preview") || (!OperatingSystem.IsWindows() && !Theme.Debug.Has("--overlay"));

    public Shell()
    {
        if (Theme.Debug.Has("--demo")) Model.MusicPlaying = true;
        Stage = new NotchStage(Model, Clock) { Width = StageWidth, Height = StageHeight };
        Tracker = new HoverTracker(Model, Clock);
        Window = _preview ? MakePreview() : MakeOverlay();
        Tracker.SetInteractive = on => { if (!_preview) Overlay.SetClickThrough(Window, !on); };

        Window.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Escape && Model.Expanded) Tracker.Collapse(); };
        Window.Opened += (_, _) =>
        {
            DispatcherTimer.Run(() =>
            {
                if (UseFakePointer && TickTimes.Count < 4000) TickTimes.Add(Clock.Now);
                Tracker.Tick(UseFakePointer ? FakePointer : _pointer.Read(Stage));
                return true;
            }, TimeSpan.FromMilliseconds(16), DispatcherPriority.Input);
            DebugHarness.Start(this);
        };
    }

    private Window MakeOverlay()
    {
        var w = new Window
        {
            Width = StageWidth, Height = StageHeight, CanResize = false, ShowInTaskbar = false, Topmost = true,
            WindowDecorations = WindowDecorations.None, ShowActivated = false,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
            Background = Brushes.Transparent, Title = "Notchyyyy",
            WindowStartupLocation = WindowStartupLocation.Manual,
            Content = Stage,
        };
        w.Opened += (_, _) =>
        {
            Overlay.Prepare(w);
            Overlay.SetClickThrough(w, true);
            if (w.Screens.Primary is { } s)
                w.Position = new PixelPoint(s.Bounds.X + (int)Math.Round((s.Bounds.Width - StageWidth * s.Scaling) / 2), s.Bounds.Y);
        };
        return w;
    }

    private Window MakePreview()
    {
        bool grey = Theme.Debug.Value("--backdrop") == "grey";
        Panel backdrop = new Panel
        {
            Background = grey
                ? new SolidColorBrush(Color.FromRgb(128, 128, 128))
                : new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Color.FromRgb(0x9c, 0xb8, 0xd8), 0),
                        new GradientStop(Color.FromRgb(0x5a, 0x7f, 0xb4), 0.55),
                        new GradientStop(Color.FromRgb(0x2e, 0x4a, 0x7c), 1),
                    },
                },
        };
        Stage.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        Stage.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        var root = new Grid { Children = { backdrop, Stage } };
        return new Window
        {
            Width = 1100, Height = 560, CanResize = false, Title = "Notchyyyy — Windows preview",
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = Brushes.Black, Content = root,
        };
    }
}
