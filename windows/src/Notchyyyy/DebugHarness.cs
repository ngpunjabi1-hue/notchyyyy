using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Notchyyyy.Theme;

namespace Notchyyyy;

/// <summary>
/// Test-only launch flags, like the Mac app's:
///   --open                 open and pin the notch
///   --demo                 a stand-in song is playing (music live activity)
///   --anim-test            open at 2.5 s, close at 4.5 s, open at 6.5 s, close at 8.5 s
///   --frame-log FILE       one line per animation frame: time, width, height
///   --hover-test           drive a stand-in pointer through the hover rules; PASS/FAIL lines to $TMPDIR/notchyyyy-hovertest.txt
///   --snap FILE@SECONDS    render the window to a PNG at that time (repeatable, comma-separated)
///   --win-selftest --out DIR  the overlay checked on real Windows (cursor, click-through, transparency, screenshots)
///   --quit-after SECONDS
/// </summary>
public static class DebugHarness
{
    private static readonly string Inv = "F3";

    public static void Start(Shell s)
    {
        var d = Debug.Args;
        void At(double t, Action a) => DispatcherTimer.RunOnce(a, TimeSpan.FromSeconds(t));

        if (Debug.Has("--open"))
        {
            s.Model.Pinned = true;
            At(0.5, s.Tracker.Expand);
        }

        if (Debug.Has("--anim-test"))
        {
            s.UseFakePointer = true;   // the real pointer must not interfere
            s.Model.Pinned = true;     // opened by the test, not by hovering: don't let the tracker close it
            foreach (var (t, open) in new[] { (2.5, true), (4.5, false), (6.5, true), (8.5, false) })
                At(t, () => { if (open) s.Tracker.Expand(); else s.Tracker.Collapse(); });
        }

        if (Debug.Value("--frame-log") is { } logPath)
        {
            var sb = new StringBuilder("t,w,h,update_ms\n");
            s.Stage.FrameRendered = (t, w, h, ms) =>
                sb.Append(t.ToString(Inv, CultureInfo.InvariantCulture)).Append(',')
                  .Append(w.ToString(Inv, CultureInfo.InvariantCulture)).Append(',')
                  .Append(h.ToString(Inv, CultureInfo.InvariantCulture)).Append(',')
                  .Append(ms.ToString(Inv, CultureInfo.InvariantCulture)).Append('\n');
            AppDomain.CurrentDomain.ProcessExit += (_, _) => File.WriteAllText(logPath, sb.ToString());
            DispatcherTimer.Run(() => { File.WriteAllText(logPath, sb.ToString()); return true; }, TimeSpan.FromSeconds(1));
        }

        if (Debug.Value("--snap") is { } snaps)
        {
            foreach (var item in snaps.Split(','))
            {
                var at = item.LastIndexOf('@');
                if (at < 0) continue;
                var path = item[..at];
                var t = double.Parse(item[(at + 1)..], CultureInfo.InvariantCulture);
                At(t, () => Snap(s.Window, path));
            }
        }

        if (Debug.Has("--hover-test")) HoverTest(s);
        if (Debug.Has("--win-selftest")) Platform.WinSelfTest.Run(s);

        // --state-log FILE: every change of the notch's state, with the time, for checking real input from outside
        if (Debug.Value("--state-log") is { } statePath)
        {
            void Write(string what) => File.AppendAllText(statePath,
                $"{s.Clock.Now:0.000} {what} expanded={s.Model.Expanded} hovering={s.Model.Hovering} tab={s.Model.Tab}\n");
            File.WriteAllText(statePath, "");
            Write("start");
            s.Model.Changed += Write;
        }

        // --pointer-debug: where the app thinks the stage's top centre is on screen, and where the pointer is
        if (Debug.Has("--pointer-debug"))
            At(1.5, () =>
            {
                var o = s.Stage.PointToScreen(new Point(s.Stage.Bounds.Width / 2, 0));
                var cg = OperatingSystem.IsMacOS() ? Platform.MacPointerProbe.Global() : null;
                var rel = Platform.PointerSources.ForThisOS().Read(s.Stage);
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "notchyyyy-pointer.txt"),
                    $"stageTopCentreScreen {o.X} {o.Y}\nscaling {s.Window.RenderScaling}\nwindowPosition {s.Window.Position.X} {s.Window.Position.Y}\n" +
                    $"pointerGlobal {cg?.X} {cg?.Y}\npointerRelative {rel?.X} {rel?.Y}\n");
            });

        if (Debug.Value("--quit-after") is { } q && double.TryParse(q, CultureInfo.InvariantCulture, out var secs))
            At(secs, () => (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown());
    }

    /// Renders the window's content exactly as Avalonia draws it (no screen-capture permission needed).
    public static void Snap(Window w, string path)
    {
        if (w.Content is not Visual v) return;
        double scale = w.RenderScaling;
        var size = v.Bounds.Size;
        using var bmp = new RenderTargetBitmap(new PixelSize((int)(size.Width * scale), (int)(size.Height * scale)), new Vector(96 * scale, 96 * scale));
        bmp.Render(v);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        bmp.Save(path, new PngBitmapEncoderOptions());
    }

    /// The hover rules with a stand-in pointer, in real time through the real timer loop.
    private static void HoverTest(Shell s)
    {
        var m = s.Model;
        var outPath = Path.Combine(Path.GetTempPath(), "notchyyyy-hovertest.txt");
        var lines = new List<string>();
        void Log(string l) { lines.Add(l); File.WriteAllLines(outPath, lines); }
        void Check(string name, bool ok, string detail = "") => Log((ok ? "PASS " : "FAIL ") + name + (detail.Length > 0 ? $"  ({detail})" : ""));
        void At(double t, Action a) => DispatcherTimer.RunOnce(a, TimeSpan.FromSeconds(t));
        s.UseFakePointer = true;

        var far = new Point(0, 400);
        var pill = new Point(0, 12);
        s.FakePointer = far;
        At(0.5, () => Check("starts closed", !m.Expanded && !m.Hovering));
        At(1.0, () => s.FakePointer = pill);
        At(1.07, () => Check("pointer on the pill swells it before opening", m.Hovering && !m.Expanded));
        At(1.40, () => Check("opens after the hover delay", m.Expanded && !m.Hovering));
        At(1.6, () => s.FakePointer = new Point(250, 220));
        At(2.4, () => Check("stays open while the pointer is inside the body", m.Expanded));
        At(2.5, () => s.FakePointer = new Point(-340, 300));   // within the 16 px margin of the body
        At(3.2, () => Check("the 16 px margin around the body still counts as inside", m.Expanded));
        At(3.3, () => s.FakePointer = new Point(0, 330));      // the tab pill's band under the body
        At(4.0, () => Check("the tab-pill band under the body counts as inside", m.Expanded));
        At(4.1, () => s.FakePointer = new Point(420, 200));    // outside
        At(4.35, () => Check("doesn't close the moment the pointer leaves", m.Expanded));
        At(4.6, () => Check("closes 0.35 s after the pointer leaves", !m.Expanded));
        At(5.0, () => s.FakePointer = pill);
        At(5.4, () => Check("reopens when the pointer comes back", m.Expanded));
        At(5.6, () => s.Tracker.Collapse());                    // like the close button, pointer still on the pill
        At(6.3, () => Check("no bounce-back: stays closed while the pointer is still on the pill", !m.Expanded && !m.Hovering));
        At(6.4, () => s.FakePointer = far);
        At(6.7, () => s.FakePointer = pill);
        At(7.1, () => Check("opens again once the pointer has left and returned", m.Expanded));
        At(7.2, () => s.FakePointer = new Point(0, 380));       // below the tab-pill band, outside everything
        At(7.8, () => Check("closes from below the tab-pill band too", !m.Expanded));
        At(8.0, () => s.FakePointer = new Point(147, 12));      // just outside the hot zone (±146 = 56 px either side of the notch)
        At(8.5, () => Check("the hot zone ends 56 px either side of the notch", !m.Hovering && !m.Expanded));
        At(8.6, () =>
        {
            var gaps = s.TickTimes.Zip(s.TickTimes.Skip(1), (a, b) => (b - a) * 1000).OrderBy(x => x).ToList();
            if (gaps.Count > 0)
                Log($"INFO pointer ticks: {gaps.Count + 1}, interval median {gaps[gaps.Count / 2]:0.0} ms, p95 {gaps[(int)(gaps.Count * 0.95)]:0.0} ms, max {gaps[^1]:0.0} ms");
            Log(lines.All(l => !l.StartsWith("FAIL")) ? "ALL PASS" : "SOME FAILED");
        });
    }
}
