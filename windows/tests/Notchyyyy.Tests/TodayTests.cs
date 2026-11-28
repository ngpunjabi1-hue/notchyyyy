using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Notchyyyy.Motion;
using Notchyyyy.Notch;
using Notchyyyy.Theme;
using Notchyyyy.Today;
using Xunit;
using Px = Notchyyyy.Tests.MediaTests.Px;

namespace Notchyyyy.Tests;

/// Step 9: Today (tasks, times, reminders) and the Stopwatch (laps, keys, the time in the closed pill).
/// The wall clock is frozen at Saturday 28 November 2026, 13:24 (TestSetup).
public class TodayTests
{
    static string Out(string name)
    {
        var d = Path.Combine(Environment.GetEnvironmentVariable("NOTCHYYYY_FRAMES") ?? Path.GetTempPath(), name);
        Directory.CreateDirectory(d);
        return d;
    }

    // ---- times in titles ----

    [Theory]
    [InlineData("Call mom at 5pm", "Call mom", 17, 0)]
    [InlineData("Call mom at 5:30 pm", "Call mom", 17, 30)]
    [InlineData("Stand-up 9:30", "Stand-up", 9, 30)]
    [InlineData("Lunch at noon", "Lunch", 12, 0)]
    [InlineData("Gym at 7", "Gym", 7, 0)]
    [InlineData("Call Sam at 3", "Call Sam", 15, 0)]
    [InlineData("Meeting 5.30pm", "Meeting", 17, 30)]
    [InlineData("11am review the deck", "review the deck", 11, 0)]
    [InlineData("Pay rent", "Pay rent", -1, 0)]
    [InlineData("Ship v2.1", "Ship v2.1", -1, 0)]
    public void TimesAreFoundInTitles(string input, string title, int hour, int minute)
    {
        var tasks = new Tasks(readOnly: true, samples: false);
        var t = tasks.Add(input)!;
        Assert.Equal(title, t.Title);
        if (hour < 0) Assert.Null(t.Time);
        else Assert.Equal(new DateTime(2026, 11, 28, hour, minute, 0), t.Time);
    }

    // ---- the day ----

    [Fact]
    public void TheDayIsOrderedAndCarriesOver()
    {
        var tasks = new Tasks(readOnly: true, samples: true);
        // timed first by time, untimed after; yesterday's unfinished invoice carried over
        Assert.Equal(new[] { "Stand-up with the design team", "Review the onboarding flow", "Lunch with Priya", "Call mom", "Book flights for the offsite" }, tasks.Today.Select(t => t.Title));
        Assert.Equal(new[] { "Send the invoice" }, tasks.Carried.Select(t => t.Title));
        Assert.Equal(2, tasks.DoneCount);
        Assert.Equal("Lunch with Priya", tasks.NextUp!.Title);
        Assert.Equal("in 6 min", Tasks.Countdown(tasks.NextUp.Time!.Value));
        // finishing a carried task counts it for today
        var invoice = tasks.Carried[0];
        tasks.Toggle(invoice);
        Assert.Empty(tasks.Carried);
        Assert.Contains(invoice, tasks.Today);
        Assert.Equal(3, tasks.DoneCount);
        tasks.ClearDone();
        Assert.Equal(3, tasks.Today.Count);
    }

    [Fact]
    public void RemindersComeOnceWhenDue()
    {
        var clock = new DateTime(2026, 11, 28, 13, 24, 0);
        var tasks = new Tasks(readOnly: true, samples: true) { Clock = () => clock };
        var r = new MemoryReminders();
        tasks.Start(r);
        tasks.CheckDue();
        Assert.Empty(r.Shown);                              // nothing due at 13:24 (the morning's are past and done)
        clock = new DateTime(2026, 11, 28, 13, 30, 5);
        tasks.CheckDue();
        Assert.Equal(new[] { "Lunch with Priya" }, r.Shown.Select(t => t.Title));
        tasks.CheckDue();
        Assert.Single(r.Shown);                             // once
        var call = tasks.Today.First(t => t.Title == "Call mom");
        tasks.Toggle(call);                                 // done before it's due: no reminder
        clock = new DateTime(2026, 11, 28, 17, 0, 30);
        tasks.CheckDue();
        Assert.Single(r.Shown);
    }

    [Fact]
    public void TasksAreKeptOnDisk()
    {
        var file = Path.Combine(Directory.CreateTempSubdirectory("tasks").FullName, "tasks.json");
        var a = new Tasks(file, readOnly: false, samples: false);
        a.Add("Call mom at 5pm");
        a.Add("Book flights");
        a.Toggle(a.Today.First(t => t.Title == "Book flights"));
        var b = new Tasks(file, readOnly: false, samples: false);
        Assert.Equal(a.All.Select(t => (t.Id, t.Title, t.Time, t.Done)), b.All.Select(t => (t.Id, t.Title, t.Time, t.Done)));
        Assert.False(File.Exists(file + ".tmp"));
    }

    [Fact]
    public void StopwatchCountsLapsAndFormats()
    {
        var clock = new ManualClock { Now = 10 };
        var sw = new StopwatchModel(clock);
        sw.Toggle();
        clock.Now = 13.21;
        Assert.Equal("00:03.21", StopwatchModel.Format(sw.Elapsed()));
        sw.Lap();
        clock.Now = 15.0;
        sw.Lap();
        Assert.Equal(new[] { 1.79, 3.21 }, sw.Laps.Select(l => Math.Round(l, 2)));
        sw.Toggle();
        clock.Now = 99;
        Assert.Equal(5.0, sw.Elapsed(), 3);                    // stopped: time doesn't move
        Assert.Equal("1:02:05", StopwatchModel.Format(3725));
        Assert.Equal("2:05", StopwatchModel.Format(125.4, centis: false));
        sw.Reset();
        Assert.False(sw.HasTime);
        Assert.Empty(sw.Laps);
    }

    // ---- the tabs ----

    sealed class Rig : IDisposable
    {
        public readonly NotchModel M = new();
        public readonly ManualClock Clock = new() { Now = 1 };
        public readonly NotchStage Stage;
        public readonly Window Win;
        public Rig(Tab tab, bool light = false, bool open = true)
        {
            Prefs.Shared.Appearance = light ? Appearance.Light : Appearance.Dark;
            M.Buddy.Hour = () => 13;
            Stage = new NotchStage(M, Clock) { Width = 820, Height = 430 };
            Win = new Window { Width = 820, Height = 430, Content = new Grid { Background = new SolidColorBrush(Color.FromRgb(140, 160, 190)), Children = { Stage } } };
            Win.Show();
            Win.SetRenderScaling(2);
            M.Tab = tab;
            // the shell's key handler (Esc, Ctrl+1…9, the Stopwatch's keys)
            Win.KeyDown += (_, e) => { if (NotchKeys.Handle(M, () => M.Expanded = false, e.Key, e.KeyModifiers)) e.Handled = true; };
            if (open) { Clock.Now = 1.1; M.Expanded = true; }
            Run(2.2);
        }
        public WriteableBitmap Run(double until)
        {
            // the animation is stepped frame by frame; only the last frame is drawn and captured (drawing every
            // step made the suite five times slower on the Windows runner, for frames nothing looked at)
            for (double t = Clock.Now; t <= until + 1e-9; t += 1 / 30.0) { Clock.Now = t; Stage.Update(t); Dispatcher.UIThread.RunJobs(); }
            return Frame();
        }
        public WriteableBitmap Frame() { Dispatcher.UIThread.RunJobs(); return Win.CaptureRenderedFrame()!; }
        public TodayView Today => Stage.Body.CurrentToday!;
        public StopwatchView Watch => Stage.Body.CurrentStopwatch!;
        public Point Centre(Control c, double dx = 0, double dy = 0) => c.TranslatePoint(new Point(c.Bounds.Width / 2 + dx, c.Bounds.Height / 2 + dy), Win)!.Value;
        public void Click(Point p) { Win.MouseMove(p, RawInputModifiers.None); Win.MouseDown(p, MouseButton.Left); Win.MouseUp(p, MouseButton.Left); Dispatcher.UIThread.RunJobs(); }
        public void Dispose() { Win.Close(); Prefs.Shared.Appearance = Appearance.Dark; }
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public void TodayLooksRight(bool light)
    {
        using var rig = new Rig(Tab.Today, light);
        var bmp = rig.Run(2.6);
        bmp.Save(Path.Combine(Out("today"), $"today-{(light ? "light" : "dark")}.png"), new PngBitmapEncoderOptions());
        Assert.Equal("2 of 5 done", rig.Today.Progress);
        Assert.True(rig.Today.ShowingNext);
        Assert.Equal("Lunch with Priya | in 6 min", rig.Today.NextText);
        var rows = rig.Today.List.Rows.ToList();
        Assert.Equal("Send the invoice", rows[0].Task.Title);
        Assert.True(rows[0].Carried);
        Assert.Equal(6, rows.Count);
        // the done rows' checks are filled with the accent
        var done = rows.First(r => r.Task.Done);
        var probe = done.TranslatePoint(new Point(20 - 3.5, 18 - 3), rig.Win)!.Value;   // inside the fill, clear of the tick
        var c = new Px(bmp, 2).At(probe.X, probe.Y);
        Assert.True(Px.Dist(c, LT.AccentColor) < 60, $"done check not accent: {c}");
    }

    /// Typing in the composer and pressing Enter adds the task, with the time it understood, in its place.
    [AvaloniaFact]
    public void TypingATaskAddsItInOrder()
    {
        using var rig = new Rig(Tab.Today);
        rig.Click(rig.Centre(rig.Today.Input));
        Assert.True(rig.Today.Input.IsFocused);
        rig.Win.KeyTextInput("Call dad at 3pm");
        rig.Win.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("", rig.Today.Input.Text);
        var dad = rig.M.Tasks.Today.First(t => t.Title == "Call dad");
        Assert.Equal(15, dad.Time!.Value.Hour);
        rig.Run(rig.Clock.Now + 0.8);
        var order = rig.Today.List.Rows.Select(r => r.Task.Title).ToList();
        Assert.Equal(order.IndexOf("Lunch with Priya") + 1, order.IndexOf("Call dad"));
        var row = rig.Today.List.RowFor(dad)!;
        Assert.Equal(1, row.Opacity, 2);
        Assert.Equal(order.IndexOf("Call dad") * (TaskList.RowH + TaskList.Gap), row.Y, 1);
        Assert.Equal("2 of 6 done", rig.Today.Progress);
    }

    /// The clock button shows a time field (an hour from now); arrow keys change it; the task gets that time.
    [AvaloniaFact]
    public void TheTimeFieldSetsATime()
    {
        using var rig = new Rig(Tab.Today);
        rig.Click(rig.Centre(rig.Today.ClockButton));
        Assert.True(rig.Today.Time.IsVisible);
        Assert.Equal(new TimeSpan(14, 24, 0), rig.Today.Time.Value);
        rig.Run(rig.Clock.Now + 0.2);
        rig.Click(rig.Centre(rig.Today.Time, 12));          // the minutes
        rig.Win.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
        rig.Click(rig.Centre(rig.Today.Time, -12));         // the hours
        rig.Win.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new TimeSpan(15, 25, 0), rig.Today.Time.Value);
        rig.Frame().Save(Path.Combine(Out("today"), "time-field.png"), new PngBitmapEncoderOptions());
        rig.Today.Input.Text = "Dentist";
        rig.Today.AddFromInput();
        Assert.Equal(new DateTime(2026, 11, 28, 15, 25, 0), rig.M.Tasks.Today.First(t => t.Title == "Dentist").Time);
        Assert.False(rig.Today.Time.IsVisible);
    }

    /// Clicking a check ticks the task (the progress follows); the hover trash deletes; Clear completed clears.
    [AvaloniaFact]
    public void TickingDeletingAndClearing()
    {
        using var rig = new Rig(Tab.Today);
        var call = rig.Today.List.Rows.First(r => r.Task.Title == "Call mom");
        rig.Click(call.TranslatePoint(new Point(20, 18), rig.Win)!.Value);
        Assert.True(call.Task.Done);
        Assert.Equal("3 of 5 done", rig.Today.Progress);
        rig.Run(rig.Clock.Now + 0.6);
        rig.Frame().Save(Path.Combine(Out("today"), "ticked.png"), new PngBitmapEncoderOptions());
        var lunch = rig.Today.List.Rows.First(r => r.Task.Title.StartsWith("Lunch"));
        rig.Win.MouseMove(lunch.TranslatePoint(new Point(100, 18), rig.Win)!.Value, RawInputModifiers.None);
        rig.Run(rig.Clock.Now + 0.4);
        rig.Click(lunch.TranslatePoint(new Point(lunch.Bounds.Width - 12 - 11, 18), rig.Win)!.Value);
        Assert.DoesNotContain(rig.M.Tasks.All, t => t.Title.StartsWith("Lunch"));
        rig.Run(rig.Clock.Now + 0.6);
        // it sits at the end of the list: scroll down to it, as a person would
        rig.Win.MouseWheel(rig.Centre(rig.Today.List), new Vector(0, -5));
        rig.Run(rig.Clock.Now + 0.6);
        rig.Click(rig.Centre(rig.Today.List.ClearButton));
        Assert.Equal(0, rig.M.Tasks.DoneCount);
        rig.Run(rig.Clock.Now + 0.8);
        Assert.Equal(new[] { "Send the invoice", "Book flights for the offsite" }, rig.Today.List.Rows.Select(r => r.Task.Title));
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public void StopwatchRunsWithButtonsAndKeys(bool light)
    {
        using var rig = new Rig(Tab.Stopwatch, light);
        var w = rig.Watch;
        Assert.Equal("Start", w.RightButton.Title);
        Assert.False(w.LeftButton.Enabled);
        rig.Click(rig.Centre(w.RightButton));
        Assert.True(rig.M.Stopwatch.Running);
        Assert.Equal("Stop", w.RightButton.Title);
        Assert.Equal("Lap", w.LeftButton.Title);
        rig.Run(rig.Clock.Now + 3.0);
        rig.Win.KeyPress(Key.L, RawInputModifiers.None, PhysicalKey.L, "l");
        rig.Run(rig.Clock.Now + 1.5);
        rig.Win.KeyPress(Key.L, RawInputModifiers.None, PhysicalKey.L, "l");
        rig.Run(rig.Clock.Now + 0.5);
        rig.Win.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Dispatcher.UIThread.RunJobs();
        Assert.False(rig.M.Stopwatch.Running);
        var laps = w.LapRows;
        Assert.Equal(new[] { "Lap 2", "Lap 1" }, laps.Select(l => l.Name));
        Assert.Equal(LT.Green, laps[0].Colour);   // the faster lap
        Assert.Equal(LT.Red, laps[1].Colour);
        var bmp = rig.Frame();
        bmp.Save(Path.Combine(Out("today"), $"stopwatch-{(light ? "light" : "dark")}.png"), new PngBitmapEncoderOptions());
        Assert.Matches(@"^00:0[45]\.\d\d$", w.Time.Shown);
        rig.Win.KeyPress(Key.R, RawInputModifiers.None, PhysicalKey.R, "r");
        Dispatcher.UIThread.RunJobs();
        Assert.False(rig.M.Stopwatch.HasTime);
        Assert.Empty(w.LapRows);
    }

    /// While the stopwatch runs, the closed pill shows its icon and the time (Sprout steps aside); stopping it
    /// gives the pill back.
    [AvaloniaFact]
    public void StopwatchShowsInTheClosedPill()
    {
        using var rig = new Rig(Tab.Stopwatch, open: false);
        double before = rig.M.ClosedSize.Width;
        rig.M.Stopwatch.Toggle();
        Assert.True(rig.M.Timing);
        Assert.False(rig.M.ShowsBuddy);
        Assert.Equal(before + 124 - 92, rig.M.ClosedSize.Width, 1);
        var bmp = rig.Run(rig.Clock.Now + 65.4);
        bmp.Save(Path.Combine(Out("today"), "pill-stopwatch.png"), new PngBitmapEncoderOptions());
        var px = new Px(bmp, 2);
        double left = 410 - rig.M.ClosedSize.Width / 2, right = 410 + rig.M.ClosedSize.Width / 2;
        int accent = 0, white = 0;
        for (double x = left + 18; x < left + 36; x += 0.5) for (double y = 8; y < 26; y += 0.5) if (Px.Dist(px.At(x, y), LT.AccentColor) < 70) accent++;
        for (double x = right - 60; x < right - 16; x += 0.5) for (double y = 8; y < 26; y += 0.5) if (px.At(x, y).R > 200) white++;
        Assert.True(accent > 10, $"no stopwatch icon ({accent})");
        Assert.True(white > 20, $"no time text ({white})");
        rig.M.Stopwatch.Toggle();
        Assert.True(rig.M.ShowsBuddy);
        Assert.Equal(before, rig.M.ClosedSize.Width, 1);
    }

    [AvaloniaFact]
    public void HomeRowShowsWhatsNext()
    {
        using var rig = new Rig(Tab.Home);
        var home = rig.Stage.Body.CurrentHome!;
        Assert.Equal("Lunch with Priya", home.TodayRow.Title);
        Assert.Equal($"2 of 5 done · {Tasks.ShortTime(new DateTime(2026, 11, 28, 13, 30, 0))}", home.TodayRow.Detail);
    }

    /// Frame by frame at 120 Hz: a task added (rows make room), ticked, deleted. No pops.
    [AvaloniaTheory]
    [InlineData("add")] [InlineData("tick")] [InlineData("delete")]
    public void TaskMotionHasNoPops(string what)
    {
        using var rig = new Rig(Tab.Today);
        switch (what)
        {
            case "add": rig.M.Tasks.Add("Water the plants at 13:00"); break;
            case "tick": rig.M.Tasks.Toggle(rig.M.Tasks.Today.First(t => t.Title == "Call mom")); break;
            case "delete": rig.M.Tasks.Delete(rig.M.Tasks.Today.First(t => t.Title == "Review the onboarding flow")); break;
        }
        Px? prev = null;
        var jumps = new List<double>();
        for (int i = 1; i <= 72; i++)
        {
            rig.Clock.Now += 1 / 120.0;
            var px = new Px(rig.Frame(), 2);
            if (prev is not null)
            {
                int changed = 0, n = 0;
                for (double y = 46; y < 276; y += 3)
                for (double x = 98; x < 722; x += 3) { n++; if (Px.Dist(px.At(x, y), prev.At(x, y)) > 40) changed++; }
                jumps.Add((double)changed / n);
            }
            prev = px;
        }
        File.WriteAllText(Path.Combine(Out("today-motion"), what + ".txt"), string.Join("\n", jumps.Select(j => j.ToString("0.0000"))));
        Assert.True(jumps.Take(30).Sum() > 0.002, "nothing moved");
        for (int i = 0; i < jumps.Count; i++)
        {
            double around = Math.Max(i > 0 ? jumps[i - 1] : 0, i + 1 < jumps.Count ? jumps[i + 1] : 0);
            Assert.False(jumps[i] > 0.02 && jumps[i] > 2.5 * around, $"a pop at frame {i + 2}: {jumps[i]:P1} changed (around {around:P1})");
        }
    }
}
