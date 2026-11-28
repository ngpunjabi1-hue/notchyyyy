using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Notchyyyy.Apps;
using Notchyyyy.Motion;
using Notchyyyy.Notch;
using Notchyyyy.Theme;
using Xunit;
using Px = Notchyyyy.Tests.MediaTests.Px;

namespace Notchyyyy.Tests;

/// Step 8: the Apps tab — pinned apps, opening one, editing (remove, reorder), the picker, and pins kept on disk.
public class AppsTests
{
    static string Out(string name)
    {
        var d = Path.Combine(Environment.GetEnvironmentVariable("NOTCHYYYY_FRAMES") ?? Path.GetTempPath(), name);
        Directory.CreateDirectory(d);
        return d;
    }

    sealed class Rig : IDisposable
    {
        public readonly NotchModel M = new();
        public readonly ManualClock Clock = new() { Now = 1 };
        public readonly NotchStage Stage;
        public readonly Window Win;
        public readonly SampleCatalog Catalog = new();
        public int Collapses;
        public Rig(bool light = false)
        {
            Prefs.Shared.Appearance = light ? Appearance.Light : Appearance.Dark;
            M.Buddy.Hour = () => 14;
            M.Apps.Start(Catalog);
            Pump(() => M.Apps.Scanned);
            AppsView.Collapse = () => Collapses++;
            Stage = new NotchStage(M, Clock) { Width = 820, Height = 430 };
            Win = new Window { Width = 820, Height = 430, Content = new Grid { Background = new SolidColorBrush(Color.FromRgb(140, 160, 190)), Children = { Stage } } };
            Win.Show();
            Win.SetRenderScaling(2);
            M.Tab = Tab.Apps;
            Clock.Now = 1.1; M.Expanded = true;
            Run(2.0);
            Pump(() => M.Apps.Ids.All(id => M.Apps.Icon(id) is not null));   // icons load in the background
        }
        public static void Pump(Func<bool> until)
        {
            for (int i = 0; i < 500 && !until(); i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
            Dispatcher.UIThread.RunJobs();
            Assert.True(until(), "timed out waiting");
        }
        public WriteableBitmap Run(double until)
        {
            // the animation is stepped frame by frame; only the last frame is drawn and captured (drawing every
            // step made the suite five times slower on the Windows runner, for frames nothing looked at)
            for (double t = Clock.Now; t <= until + 1e-9; t += 1 / 30.0) { Clock.Now = t; Stage.Update(t); Dispatcher.UIThread.RunJobs(); }
            return Frame();
        }
        public WriteableBitmap Frame() { Dispatcher.UIThread.RunJobs(); return Win.CaptureRenderedFrame()!; }
        public AppsView View => Stage.Body.CurrentApps!;
        public Point Centre(Control c, double dx = 0, double dy = 0) => c.TranslatePoint(new Point(c.Bounds.Width / 2 + dx, c.Bounds.Height / 2 + dy), Win)!.Value;
        public void Click(Point p) { Win.MouseMove(p, RawInputModifiers.None); Win.MouseDown(p, MouseButton.Left); Win.MouseUp(p, MouseButton.Left); Dispatcher.UIThread.RunJobs(); }
        public void Dispose() { Win.Close(); Prefs.Shared.Appearance = Appearance.Dark; AppsView.Collapse = null; }
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public void ShelfLooksRight(bool light)
    {
        using var rig = new Rig(light);
        var bmp = rig.Run(2.6);
        bmp.Save(Path.Combine(Out("apps"), $"apps-{(light ? "light" : "dark")}.png"), new PngBitmapEncoderOptions());
        // the Mac's six defaults, Windows' counterparts
        Assert.Equal(Favorites.Defaults, rig.M.Apps.Ids.Select(rig.M.Apps.Name));
        Assert.Equal("6 pinned", rig.View.Label);
        Assert.Equal(7, rig.View.Grid.Columns);
        // File Explorer's yellow tile is drawn in the first slot
        var px = new Px(bmp, 2);
        var probe = rig.Centre(rig.View.Grid.Tiles.First(), -17, -24);   // the icon's corner, clear of its letter
        var c = px.At(probe.X, probe.Y);
        Assert.True(c.R > 200 && c.G > 150 && c.B < 120, $"File Explorer's icon not drawn: {c}");
        Assert.True(rig.View.Grid.AddTile.IsHitTestVisible);
    }

    [AvaloniaFact]
    public void ClickingAnAppOpensItAndClosesTheNotch()
    {
        using var rig = new Rig();
        var notepad = rig.View.Grid.Tiles.First(t => rig.M.Apps.Name(t.Id!) == "Notepad");
        rig.Click(rig.Centre(notepad));
        Assert.Equal(1, notepad.Opens);
        rig.Run(rig.Clock.Now + 0.06);
        Assert.Empty(rig.Catalog.Launched);   // the press shows first
        rig.Run(rig.Clock.Now + 0.2);
        Assert.Equal(new[] { "sample:Notepad" }, rig.Catalog.Launched);
        Assert.Equal(1, rig.Collapses);
    }

    /// Edit: the label changes, Add hides, tiles wiggle and wear a ×; the × removes the app and the rest close up.
    [AvaloniaFact]
    public void EditModeRemovesApps()
    {
        using var rig = new Rig();
        rig.Click(rig.Centre(rig.View.EditButton));
        Assert.True(rig.View.Editing);
        Assert.Equal("Click × to remove · drag to reorder", rig.View.Label);
        Assert.False(rig.View.AddButton.IsVisible);
        Assert.Equal("Done", rig.View.EditButton.Title);
        rig.Run(rig.Clock.Now + 0.5);
        var tiles = rig.View.Grid.Tiles.ToList();
        // they wiggle: the transform changes over time without anything else happening
        var m0 = tiles[1].RenderTransform!.Value;
        rig.Run(rig.Clock.Now + 0.1);
        Assert.NotEqual(m0, tiles[1].RenderTransform!.Value);
        rig.Frame().Save(Path.Combine(Out("apps"), "editing.png"), new PngBitmapEncoderOptions());
        var victim = tiles[1]; var after = tiles[2];
        var slot1 = rig.View.Grid.Slot(1);
        double size = AppGrid.TileSize, icon = size * 0.71, top = (AppGrid.TileHeight - (icon + 18)) / 2;
        var badge = victim.TranslatePoint(new Point((size - icon) / 2 - 5 + 9, top - 5 + 9), rig.Win)!.Value;
        rig.Click(badge);
        Assert.DoesNotContain(victim.Id, rig.M.Apps.Ids);
        rig.Run(rig.Clock.Now + 0.8);
        Assert.Equal(slot1.X, after.Current(rig.Clock.Now).X, 1);
        Assert.Equal(slot1.Y, after.Current(rig.Clock.Now).Y, 1);
        rig.Click(rig.Centre(rig.View.EditButton));
        Assert.False(rig.View.Editing);
        Assert.Equal("5 pinned", rig.View.Label);
    }

    /// While editing, dragging a tile moves it; the others make room, and it settles into its new slot.
    [AvaloniaFact]
    public void DraggingReorders()
    {
        using var rig = new Rig();
        rig.View.SetEditing(true);
        rig.Run(rig.Clock.Now + 0.5);
        var first = rig.View.Grid.Tiles.First();
        string id = first.Id!;
        var from = rig.Centre(first);
        var to = rig.View.Grid.TranslatePoint(rig.View.Grid.Slot(3) + new Vector(AppGrid.TileSize / 2, AppGrid.TileHeight / 2), rig.Win)!.Value;
        rig.Win.MouseMove(from, RawInputModifiers.None);
        rig.Win.MouseDown(from, MouseButton.Left);
        for (int i = 1; i <= 10; i++)
        {
            rig.Win.MouseMove(from + (to - from) * (i / 10.0), RawInputModifiers.LeftMouseButton);
            rig.Clock.Now += 1 / 60.0;
            rig.Frame();
        }
        Assert.Equal(3, rig.M.Apps.Ids.ToList().IndexOf(id));
        rig.Win.MouseUp(to, MouseButton.Left);
        rig.Run(rig.Clock.Now + 0.8);
        Assert.False(rig.View.Grid.Dragging);
        var settled = first.Current(rig.Clock.Now);
        Assert.Equal(rig.View.Grid.Slot(3).X, settled.X, 1);
        Assert.Equal(rig.View.Grid.Slot(3).Y, settled.Y, 1);
    }

    /// The picker: every app, a search that narrows it, a click that pins (✓), Done back to the shelf where the new
    /// app has grown in.
    [AvaloniaFact]
    public void PickerSearchesAndPins()
    {
        using var rig = new Rig();
        rig.Click(rig.Centre(rig.View.AddButton));
        rig.Run(rig.Clock.Now + 0.6);
        Assert.True(rig.View.ShowingPicker);
        var list = rig.View.Picker.List;
        Assert.Equal(25, list.Visible);
        Rig.Pump(() => list.Shown.Where(r => r.InView).All(r => rig.M.Apps.Icon(r.App.Id) is not null));   // icons load in the background
        rig.Frame().Save(Path.Combine(Out("apps"), "picker.png"), new PngBitmapEncoderOptions());
        rig.View.Picker.Search.Text = "powe";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { "PowerPoint" }, list.Shown.Select(r => r.App.Name));
        rig.Run(rig.Clock.Now + 0.2);
        rig.Click(rig.Centre(list.Shown[0]));
        Assert.Contains("sample:PowerPoint", rig.M.Apps.Ids);
        rig.Frame().Save(Path.Combine(Out("apps"), "picker-pinned.png"), new PngBitmapEncoderOptions());
        rig.View.Picker.Search.Text = "zzz";
        Dispatcher.UIThread.RunJobs();
        Assert.True(rig.View.Picker.ShowingNoMatch);
        rig.View.ShowPicker(false);
        rig.Run(rig.Clock.Now + 0.8);
        Assert.False(rig.View.ShowingPicker);
        Assert.Equal("7 pinned", rig.View.Label);
        var tile = rig.View.Grid.TileFor("sample:PowerPoint")!;
        Assert.Equal(1, tile.Opacity, 2);
        Assert.Equal(rig.View.Grid.Slot(6).X, tile.Current(rig.Clock.Now).X, 1);
        Assert.Equal(rig.View.Grid.Slot(6).Y, tile.Current(rig.Clock.Now).Y, 1);
    }

    [AvaloniaFact]
    public void PinsAreKeptOnDisk()
    {
        var file = Path.Combine(Directory.CreateTempSubdirectory("apps").FullName, "apps.json");
        var a = new Favorites(file, readOnly: false);
        a.Start(new SampleCatalog());
        Rig.Pump(() => a.Scanned);
        a.Add("sample:Spotify");
        a.Move("sample:Spotify", 0);
        a.Remove("sample:Notepad");
        var b = new Favorites(file, readOnly: false);
        b.Start(new SampleCatalog());
        Rig.Pump(() => b.Scanned);
        Assert.Equal(a.Ids, b.Ids);
        Assert.Equal("sample:Spotify", b.Ids[0]);
        // an app that's been uninstalled drops off once the Start menu has been read
        File.WriteAllText(file, "[\"sample:Spotify\",\"sample:Gone Forever\"]");
        var c = new Favorites(file, readOnly: false);
        c.Start(new SampleCatalog());
        Rig.Pump(() => c.Scanned);
        Assert.Equal(new[] { "sample:Spotify" }, c.Ids);
    }

    /// Frame by frame at 120 Hz: pinning (a tile grows in), removing (the rest close up), editing on. No pops.
    [AvaloniaTheory]
    [InlineData("pin")] [InlineData("remove")] [InlineData("edit")]
    public void MotionHasNoPops(string what)
    {
        using var rig = new Rig();
        switch (what)
        {
            case "pin": rig.M.Apps.Add("sample:Spotify"); break;
            case "remove": rig.M.Apps.Remove(rig.M.Apps.Ids[0]); break;
            case "edit": rig.View.SetEditing(true); break;
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
                for (double y = 84; y < 276; y += 3)
                for (double x = 98; x < 722; x += 3) { n++; if (Px.Dist(px.At(x, y), prev.At(x, y)) > 40) changed++; }
                jumps.Add((double)changed / n);
            }
            prev = px;
        }
        File.WriteAllText(Path.Combine(Out("apps-motion"), what + ".txt"), string.Join("\n", jumps.Select(j => j.ToString("0.0000"))));
        Assert.True(jumps.Take(30).Sum() > 0.01, "nothing moved");
        for (int i = 0; i < jumps.Count; i++)
        {
            double around = Math.Max(i > 0 ? jumps[i - 1] : 0, i + 1 < jumps.Count ? jumps[i + 1] : 0);
            Assert.False(jumps[i] > 0.02 && jumps[i] > 2.5 * around, $"a pop at frame {i + 2}: {jumps[i]:P1} changed (around {around:P1})");
        }
    }
}
