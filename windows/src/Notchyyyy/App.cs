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
        // what's playing: Windows' media sessions; the demo track with `--demo` (and nothing on the Mac preview otherwise)
        Media.IMediaSource source = Theme.Debug.Has("--demo") ? new Media.DemoMediaSource()
            : Theme.Debug.Has("--no-media") ? new Media.NoMediaSource()
#if WINDOWS
            : new Platform.SmtcSource();
#else
            : new Media.NoMediaSource();
#endif
        Stage = new NotchStage(Model, Clock) { Width = StageWidth, Height = StageHeight };
        Model.Media.Start(source);
        // Grayscale text smoothing, as on the Mac: Windows' default sub-pixel (ClearType) smoothing left red/blue
        // fringes on the text (seen on the Windows runner), and fringes turn into halos when text fades over a transparent window.
        TextOptions.SetTextRenderingMode(Stage, TextRenderingMode.Antialias);
        Tracker = new HoverTracker(Model, Clock);
        Window = _preview ? MakePreview() : MakeOverlay();
        Tracker.SetInteractive = on =>
        {
            if (_preview) return;
            Overlay.SetClickThrough(Window, !on);
            if (on) Overlay.RememberForeground(Window);
            // once the close has finished (re-activating mid-animation caused ghosting on the Mac)
            else DispatcherTimer.RunOnce(() => { if (!Model.Expanded) Overlay.ReturnForeground(Window); }, TimeSpan.FromSeconds(0.45));
        };

        Stage.Pill.Close = Tracker.Collapse;
        Prefs.Shared.Changed += name =>
        {
            if (name == nameof(Prefs.Appearance) && Application.Current is { } app)
                app.RequestedThemeVariant = LT.Light ? ThemeVariant.Light : ThemeVariant.Dark;
            if (name == nameof(Prefs.UserName)) Model.UserName = Prefs.Shared.UserName;
            Stage.ApplySetting(name);
        };
        // Screenshot / Record on Home: close the notch so it isn't in the shot, then open Windows' own Snipping Tool
        HomeView.Capture = video =>
        {
            Tracker.Collapse();
            DispatcherTimer.RunOnce(() => Platform.Capture.Start(video), TimeSpan.FromSeconds(0.4));
        };
        Window.KeyDown += (_, e) => { if (NotchKeys.Handle(Model, Tracker.Collapse, e.Key, e.KeyModifiers)) e.Handled = true; };
        Window.Opened += (_, _) =>
        {
            DispatcherTimer.Run(() =>
            {
                if (UseFakePointer && TickTimes.Count < 4000) TickTimes.Add(Clock.Now);
                var p = UseFakePointer ? FakePointer : _pointer.Read(Stage);
                Tracker.Tick(p);
                LookAt(p);
                return true;
            }, TimeSpan.FromMilliseconds(16), DispatcherPriority.Input);
            DebugHarness.Start(this);
        };
    }

    /// The little Sprout's eyes follow the pointer when it's near the top of the screen (the Mac's track()):
    /// a direction from Sprout, stronger up to 160 pt away, in steps of 1/6 so it doesn't redraw for every pixel.
    private void LookAt(Point? pointer)
    {
        // the big Sprout on Home follows the pointer anywhere on screen: a direction from between its eyes,
        // stronger the farther away (up to 0.9 × its size)
        if (Model.Expanded && pointer is { } q && Stage.Body.CurrentHome is { } home)
        {
            var e = home.EyesInBody();
            double ex = q.X + Model.BodySize.Width / 2, ey = q.Y;     // the pointer in open-body coordinates
            double gx = ex - e.X, gy = ey - e.Y, l = Math.Max(1, Math.Sqrt(gx * gx + gy * gy)), r = Math.Min(1, l / (HomeView.SproutSize * 0.9));
            HomeView.HomeGaze = new Point(gx / l * r, gy / l * r);
        }
        if (Model.Expanded || !Prefs.Shared.Buddy || pointer is not { } p) return;
        double bx = -(Model.NotchSize.Width + 12 + Model.Extra) / 2 + 18 + 15, by = Model.NotchSize.Height / 2;
        double dx = p.X - bx, dy = p.Y - by, len = Math.Max(1, Math.Sqrt(dx * dx + dy * dy)), reach = Math.Min(1, len / 160);
        var g = new Point(Math.Round(dx / len * reach * 6) / 6, Math.Round(dy / len * reach * 6) / 6);
        if (g != Model.Buddy.Gaze) { Model.Buddy.Gaze = g; Stage.Kick(); }
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
