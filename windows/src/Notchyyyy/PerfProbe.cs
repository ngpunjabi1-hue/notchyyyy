using System.Diagnostics;
using System.Globalization;
using System.Text;
using Avalonia.Controls;
using Avalonia.Threading;
using Notchyyyy.Notch;

namespace Notchyyyy;

/// <summary>
/// `--perf DIR`: a scripted session with every frame timed, to measure smoothness on the machine it runs on.
/// Phases: opening, Home (big Sprout animating), a tab switch, back to Home, closing, closed with the little Sprout,
/// and another open/close. For each phase: frame count, frame interval (median, 95th percentile, worst), late frames
/// (more than 1.5 frames' time apart), and CPU used. Writes frames.csv and summary.txt, then quits.
/// </summary>
public static class PerfProbe
{
    private sealed record Phase(string Name, double Start, double End);

    public static void Run(Shell s, string dir)
    {
        Directory.CreateDirectory(dir);
        s.UseFakePointer = true; s.FakePointer = null;      // the real pointer must not interfere
        s.Model.Pinned = true;
        var clock = Stopwatch.StartNew();
        double Now() => clock.Elapsed.TotalSeconds;
        var proc = Process.GetCurrentProcess();
        var frames = new List<(double T, string Phase)>();
        var sprout = new List<(double T, string Phase)>();       // the big Sprout's own redraws
        var cpu = new Dictionary<string, double>();
        string phase = "warmup";
        var little = new List<(double T, string Phase)>();
        Sprout.SproutView.Rendered = v => { if (v.BoxSize >= 100) sprout.Add((Now(), phase)); else little.Add((Now(), phase)); };
        var paint = new List<(double Ms, double Size)>();
        Sprout.SproutView.Painted = (ms, size) => { lock (paint) paint.Add((ms, size)); };
        double phaseCpuStart = proc.TotalProcessorTime.TotalMilliseconds;
        var phases = new List<Phase>();
        double phaseStart = 0;

        const string Idle = "closed (little Sprout)";
        bool probing = true;
        TopLevel top = TopLevel.GetTopLevel(s.Stage)!;
        bool running = true;
        void Frame(TimeSpan _)
        {
            if (!running) return;
            frames.Add((Now(), phase));
            // while closed and idle the probe stops asking for frames, so it doesn't keep the app drawing:
            // that phase measures the idle CPU cost honestly (the little Sprout's own 30 fps)
            if (phase == Idle) { probing = false; return; }
            top.RequestAnimationFrame(Frame);
        }

        void Enter(string name)
        {
            proc.Refresh();
            double c = proc.TotalProcessorTime.TotalMilliseconds;
            cpu[phase] = c - phaseCpuStart;
            phases.Add(new Phase(phase, phaseStart, Now()));
            phase = name; phaseStart = Now(); phaseCpuStart = c;
            if (!probing && name != Idle) { probing = true; top.RequestAnimationFrame(Frame); }
        }

        top.RequestAnimationFrame(Frame);

        var steps = new (double At, Action Do)[]
        {
            (1.0, () => { Enter("opening"); s.Tracker.Expand(); }),
            (1.8, () => Enter("home (big Sprout)")),
            (4.0, () => { Enter("tab switch"); s.Model.Tab = Tab.Media; }),
            (4.6, () => Enter("media tab")),
            (5.4, () => { Enter("back to home"); s.Model.Tab = Tab.Home; }),
            (6.0, () => Enter("home again")),
            (7.0, () => { Enter("closing"); s.Tracker.Collapse(); }),
            (7.8, () => Enter("closed (little Sprout)")),
            (11.0, () => { Enter("open again"); s.Tracker.Expand(); }),
            (11.8, () => { Enter("close again"); s.Tracker.Collapse(); }),
            (12.6, () => Enter("end")),
        };
        foreach (var (at, act) in steps) DispatcherTimer.RunOnce(act, TimeSpan.FromSeconds(at));

        DispatcherTimer.RunOnce(() =>
        {
            running = false;
            var csv = new StringBuilder("t,phase\n");
            foreach (var f in frames) csv.Append(f.T.ToString("0.0000", CultureInfo.InvariantCulture)).Append(',').Append(f.Phase).Append('\n');
            File.WriteAllText(Path.Combine(dir, "frames.csv"), csv.ToString());

            var sb = new StringBuilder();
            double hz = Hz(frames.Select(f => f.T).ToList());
            sb.AppendLine($"render scaling {s.Window.RenderScaling}, display ~{hz:0} Hz, OS {Environment.OSVersion}");
            sb.AppendLine("display frames (the notch's own motion runs at the display rate):");
            sb.AppendLine("phase                    frames  median ms  p95 ms  worst ms  late  cpu %");
            foreach (var ph in phases.Where(p => p.Name != "warmup"))
            {
                var ts = frames.Where(f => f.Phase == ph.Name).Select(f => f.T).ToList();
                if (ts.Count < 3) { sb.AppendLine($"{ph.Name,-24} {ts.Count,6}"); continue; }
                var d = ts.Zip(ts.Skip(1), (a, b) => (b - a) * 1000).OrderBy(x => x).ToList();
                double frame = 1000 / hz;
                int late = d.Count(x => x > frame * 1.5);
                double secs = ph.End - ph.Start;
                double cpuPct = cpu.TryGetValue(ph.Name, out var c) ? c / (secs * 1000) * 100 : double.NaN;
                sb.AppendLine($"{ph.Name,-24} {ts.Count,6} {d[d.Count / 2],10:0.0} {d[(int)(d.Count * 0.95)],7:0.0} {d[^1],9:0.0} {late,5} {cpuPct,6:0}");
            }
            sb.AppendLine();
            sb.AppendLine("the big Sprout's own frames (it animates at 60 fps, like the Mac's; late = over 25 ms apart):");
            sb.AppendLine("phase                    frames  median ms  p95 ms  worst ms  late");
            foreach (var ph in phases.Where(p => p.Name != "warmup"))
            {
                var ts = sprout.Where(f => f.Phase == ph.Name).Select(f => f.T).ToList();
                if (ts.Count < 3) continue;
                var d = ts.Zip(ts.Skip(1), (a, b) => (b - a) * 1000).OrderBy(x => x).ToList();
                sb.AppendLine($"{ph.Name,-24} {ts.Count,6} {d[d.Count / 2],10:0.0} {d[(int)(d.Count * 0.95)],7:0.0} {d[^1],9:0.0} {d.Count(x => x > 25),5}");
            }
            {
                var ts = little.Where(f => f.Phase == Idle).Select(f => f.T).ToList();
                if (ts.Count > 3)
                {
                    var d = ts.Zip(ts.Skip(1), (a, b) => (b - a) * 1000).OrderBy(x => x).ToList();
                    sb.AppendLine();
                    sb.AppendLine($"little Sprout while closed: {ts.Count} frames, median {d[d.Count / 2]:0.0} ms, p95 {d[(int)(d.Count * 0.95)]:0.0} ms, worst {d[^1]:0.0} ms");
                }
            }
            sb.AppendLine();
            lock (paint)
                foreach (var g in paint.GroupBy(p => p.Size > 100 ? "big Sprout (Home)" : "little Sprout (pill)"))
                {
                    var d = g.Select(p => p.Ms).OrderBy(x => x).ToList();
                    sb.AppendLine($"Skia time per {g.Key} draw: median {d[d.Count / 2]:0.00} ms, p95 {d[(int)(d.Count * 0.95)]:0.00} ms, worst {d[^1]:0.00} ms ({d.Count} draws)");
                }
            File.WriteAllText(Path.Combine(dir, "summary.txt"), sb.ToString());
            Console.WriteLine(sb.ToString());
            Environment.Exit(0);
        }, TimeSpan.FromSeconds(13.0));
    }

    /// The display's refresh rate, from the typical gap between frames.
    private static double Hz(List<double> t)
    {
        var d = t.Zip(t.Skip(1), (a, b) => b - a).Where(x => x > 0.002).OrderBy(x => x).ToList();
        return d.Count == 0 ? 60 : Math.Round(1 / d[d.Count / 4]);
    }
}
