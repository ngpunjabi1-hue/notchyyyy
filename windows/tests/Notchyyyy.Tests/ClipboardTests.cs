using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Notchyyyy.Clipboard;
using Notchyyyy.Motion;
using Notchyyyy.Notch;
using Notchyyyy.Theme;
using SkiaSharp;
using Xunit;
using Px = Notchyyyy.Tests.MediaTests.Px;

namespace Notchyyyy.Tests;

/// Step 7: the Clipboard tab, its rules for what a copy is, and history kept on disk.
public class ClipboardTests
{
    static string Out(string name)
    {
        var d = Path.Combine(Environment.GetEnvironmentVariable("NOTCHYYYY_FRAMES") ?? Path.GetTempPath(), name);
        Directory.CreateDirectory(d);
        return d;
    }

    // ---- colours ----

    [Theory]
    [InlineData("#5E6AD2", "#5E6AD2", "rgb(94, 106, 210)", "hsl(234, 56%, 60%)")]
    [InlineData("#fff", "#FFFFFF", "rgb(255, 255, 255)", "hsl(0, 0%, 100%)")]
    [InlineData("rgb(242, 201, 76)", "#F2C94C", "rgb(242, 201, 76)", "hsl(45, 86%, 62%)")]
    [InlineData("hsl(0, 100%, 50%)", "#FF0000", "rgb(255, 0, 0)", "hsl(0, 100%, 50%)")]
    [InlineData("rgba(0, 0, 0, 0.5)", "#00000080", "rgba(0, 0, 0, 0.5)", "hsla(0, 0%, 0%, 0.5)")]
    [InlineData("FF5733", "#FF5733", "rgb(255, 87, 51)", "hsl(11, 100%, 60%)")]
    public void ColoursParseAndConvert(string input, string hex, string rgb, string hsl)
    {
        var c = ColorValue.Parse(input);
        Assert.NotNull(c);
        Assert.Equal(hex, c!.Hex);
        Assert.Equal(rgb, c.Rgb);
        Assert.Equal(hsl, c.Hsl);
    }

    [Theory]
    [InlineData("decade")] [InlineData("abc")] [InlineData("hello world")] [InlineData("#12345")] [InlineData("rgb(1,2)")] [InlineData("")]
    public void WordsAreNotColours(string input) => Assert.Null(ColorValue.Parse(input));

    // ---- what a copy is (the Mac's order) ----

    const string Star = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\"><path d=\"M12 2l3 6 7 1-5 5 1 7-6-3-6 3 1-7-5-5 7-1z\"/></svg>";

    static byte[] Png(int w = 40, int h = 30, SKColor? c = null)
    {
        using var s = SKSurface.Create(new SKImageInfo(w, h));
        s.Canvas.Clear(c ?? SKColors.OrangeRed);
        using var img = s.Snapshot();
        using var d = img.Encode(SKEncodedImageFormat.Png, 100);
        return d.ToArray();
    }

    [Fact]
    public void SnapshotsBecomeTheRightKindOfCard()
    {
        Assert.Null(new ClipSnapshot(Text: "hunter2", Private: true).ToItem());                    // password managers
        Assert.Equal(ClipKind.Svg, new ClipSnapshot(Text: Star).ToItem()!.Kind);
        Assert.Equal(ClipKind.Svg, new ClipSnapshot(Text: "plain", Svg: Star).ToItem()!.Kind);
        Assert.Equal(ClipKind.Color, new ClipSnapshot(Text: " #5E6AD2 ").ToItem()!.Kind);
        Assert.Equal(ClipKind.Text, new ClipSnapshot(Text: "A1\tB1\nA2\tB2", Png: Png()).ToItem()!.Kind);   // a spreadsheet: text wins
        Assert.Equal(ClipKind.Image, new ClipSnapshot(Text: "https://example.com/cat.png", Png: Png()).ToItem()!.Kind);   // a bare link: the image
        Assert.Equal(ClipKind.Image, new ClipSnapshot(Png: Png()).ToItem()!.Kind);
        Assert.Null(new ClipSnapshot(Text: "   ").ToItem());
        var dir = Directory.CreateTempSubdirectory("clipfiles").FullName;
        File.WriteAllText(Path.Combine(dir, "icon.svg"), Star);
        File.WriteAllBytes(Path.Combine(dir, "photo.png"), Png());
        Assert.Equal(ClipKind.Svg, new ClipSnapshot(Files: new[] { Path.Combine(dir, "icon.svg") }).ToItem()!.Kind);
        Assert.Equal(ClipKind.Image, new ClipSnapshot(Files: new[] { Path.Combine(dir, "photo.png") }).ToItem()!.Kind);
        var doc = new ClipSnapshot(Files: new[] { Path.Combine(dir, "notes.txt") }).ToItem()!;
        Assert.Equal(ClipKind.Text, doc.Kind);
        Assert.EndsWith("notes.txt", doc.Text);
    }

    [Fact]
    public void SvgRendersAndCopiesAsACrispPng()
    {
        var (img, w, h) = Clips.RenderSvg(Star, 96);
        Assert.NotNull(img);
        Assert.Equal((24, 24), (w, h));
        var png = Clips.SvgPng(Star)!;
        using var bmp = SKBitmap.Decode(png);
        Assert.True(bmp.Width >= 256, $"PNG only {bmp.Width} px");
        // the star is drawn in black in the middle, transparent in the corner
        Assert.True(bmp.GetPixel(bmp.Width / 2, bmp.Height / 2).Alpha > 200);
        Assert.Equal(0, bmp.GetPixel(2, 2).Alpha);
    }

    // ---- the list ----

    static Clips Store(string? folder = null) => new(folder ?? Directory.CreateTempSubdirectory("clips").FullName, readOnly: false, samples: false);

    [AvaloniaFact]
    public void CopyingTheSameThingAgainMovesItToTheFront()
    {
        var s = Store();
        s.Start(new MemoryClipboard());
        int before = s.Items.Count;
        s.Add(new ClipItem { Kind = ClipKind.Text, Text = "one" });
        s.Add(new ClipItem { Kind = ClipKind.Text, Text = "two" });
        s.Add(new ClipItem { Kind = ClipKind.Text, Text = "one" });
        Assert.Equal(before + 2, s.Items.Count);
        Assert.Equal("one", s.Items[0].Text);
        for (int i = 0; i < 60; i++) s.Add(new ClipItem { Kind = ClipKind.Text, Text = $"n{i}" });
        Assert.Equal(Clips.Max, s.Items.Count);
        Assert.Equal("n59", s.Items[0].Text);
    }

    /// History survives a restart: text, colours, SVG and images (a PNG beside the list), in order; removing an image
    /// removes its file.
    [AvaloniaFact]
    public async Task HistoryIsKeptOnDisk()
    {
        var dir = Directory.CreateTempSubdirectory("clips").FullName;
        var a = Store(dir);
        a.Start(new MemoryClipboard());
        a.Add(new ClipItem { Kind = ClipKind.Image, Png = Png(64, 48) });
        a.Add(new ClipItem { Kind = ClipKind.Svg, Text = Star });
        a.Add(new ClipItem { Kind = ClipKind.Color, Color = ColorValue.Parse("#F2C94C") });
        a.Add(new ClipItem { Kind = ClipKind.Text, Text = "npm install -g @openai/codex" });
        await a.SaveNow();
        Assert.Single(Directory.GetFiles(dir, "*.png"));

        var b = Store(dir);
        b.Start(new MemoryClipboard());
        Assert.Equal(new[] { ClipKind.Text, ClipKind.Color, ClipKind.Svg, ClipKind.Image }, b.Items.Select(i => i.Kind));
        Assert.Equal("#F2C94C", b.Items[1].Color!.Hex);
        Assert.NotNull(b.Items[3].Picture);
        Assert.Equal(a.Items.Select(i => i.Id), b.Items.Select(i => i.Id));

        b.Remove(b.Items[3]);
        await b.SaveNow();
        Assert.Empty(Directory.GetFiles(dir, "*.png"));
        Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
    }

    [Fact]
    public void BitmapsFromTheClipboardBecomePngs()
    {
        // a 2 × 2 bottom-up 32-bit DIB whose fourth bytes are 0 (as many apps write it): it must come out opaque
        var dib = new byte[40 + 16];
        BitConverter.GetBytes(40).CopyTo(dib, 0); BitConverter.GetBytes(2).CopyTo(dib, 4); BitConverter.GetBytes(2).CopyTo(dib, 8);
        BitConverter.GetBytes((short)1).CopyTo(dib, 12); BitConverter.GetBytes((short)32).CopyTo(dib, 14);
        for (int i = 0; i < 4; i++) { dib[40 + i * 4] = 255; dib[40 + i * 4 + 1] = 0; dib[40 + i * 4 + 2] = 0; dib[40 + i * 4 + 3] = 0; }   // blue
        var png = Platform.WinClipboard.DibToPng(dib)!;
        using var bmp = SKBitmap.Decode(png);
        var px = bmp.GetPixel(0, 0);
        Assert.Equal((255, 0, 0, 255), (px.Blue, px.Green, px.Red, px.Alpha));
        // and back: a PNG becomes a DIB the same size
        var back = Platform.WinClipboard.PngToDib(Png(10, 7))!;
        Assert.Equal((10, 7), (BitConverter.ToInt32(back, 4), BitConverter.ToInt32(back, 8)));
        Assert.Equal(40 + 10 * 7 * 4, back.Length);
    }

    // ---- the tab ----

    sealed class Rig : IDisposable
    {
        public readonly NotchModel M = new();
        public readonly ManualClock Clock = new() { Now = 1 };
        public readonly NotchStage Stage;
        public readonly Window Win;
        public readonly MemoryClipboard Board = new();
        public Rig(bool light = false, bool samples = true, Tab tab = Tab.Clipboard)
        {
            Prefs.Shared.Appearance = light ? Appearance.Light : Appearance.Dark;
            M.Buddy.Hour = () => 14;
            if (samples) M.Clips.Start(Board);   // --sample (set for every test): the Mac's samples
            Stage = new NotchStage(M, Clock) { Width = 820, Height = 430 };
            Win = new Window { Width = 820, Height = 430, Content = new Grid { Background = new SolidColorBrush(Color.FromRgb(140, 160, 190)), Children = { Stage } } };
            Win.Show();
            Win.SetRenderScaling(2);
            M.Tab = tab;
            Clock.Now = 1.1; M.Expanded = true;
        }
        public WriteableBitmap Run(double until)
        {
            WriteableBitmap? b = null;
            for (double t = Clock.Now; t <= until + 1e-9; t += 1 / 30.0) { Clock.Now = t; Stage.Update(t); b = Frame(); }
            return b!;
        }
        public WriteableBitmap Frame() { Dispatcher.UIThread.RunJobs(); return Win.CaptureRenderedFrame()!; }
        public ClipboardView View => Stage.Body.CurrentClipboard!;
        public Point At(ClipCard c, double x, double y) => c.TranslatePoint(new Point(x, y), Win)!.Value;
        public void Click(Point p) { Win.MouseMove(p, RawInputModifiers.None); Win.MouseDown(p, MouseButton.Left); Win.MouseUp(p, MouseButton.Left); Dispatcher.UIThread.RunJobs(); }
        public void Dispose() { Win.Close(); Prefs.Shared.Appearance = Appearance.Dark; }
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public void CardsLookRight(bool light)
    {
        using var rig = new Rig(light);
        var bmp = rig.Run(2.4);
        bmp.Save(Path.Combine(Out("clipboard"), $"clipboard-{(light ? "light" : "dark")}.png"), new PngBitmapEncoderOptions());
        var cards = rig.View.Strip.Cards.ToList();
        Assert.Equal(6, cards.Count);
        var px = new Px(bmp, 2);
        // the first card is the indigo swatch #5E6AD2
        var sw = px.At(rig.At(cards[0], 120, 30).X, rig.At(cards[0], 120, 30).Y);
        Assert.True(Px.Dist(sw, Color.FromRgb(0x5E, 0x6A, 0xD2)) < 12, $"swatch colour {sw}");
        // the second is the star SVG (an outline), drawn dark on a light checkerboard
        int dark = 0;
        for (double x = ClipCard.W / 2 - 29; x < ClipCard.W / 2 + 29; x += 1)
        for (double y = ClipCard.BodyH / 2 - 29; y < ClipCard.BodyH / 2 + 29; y += 1)
            if (px.At(rig.At(cards[1], x, y).X, rig.At(cards[1], x, y).Y).R < 90) dark++;
        var corner = px.At(rig.At(cards[1], 4, 4).X, rig.At(cards[1], 4, 4).Y);
        Assert.True(dark > 150 && corner.R > 200, $"star not drawn ({dark} dark points, corner {corner})");
        // the image card shows its gradient
        var im = px.At(rig.At(cards[3], 78, 70).X, rig.At(cards[3], 78, 70).Y);
        Assert.True(im.B > 150 && im.G < 130, $"image card {im}");
        Assert.Equal("6 recent", rig.View.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text!.EndsWith("recent")).Text);
    }

    [AvaloniaFact]
    public void ClickingACardCopiesItAndMovesItToTheFront()
    {
        using var rig = new Rig();
        rig.Run(2.4);
        var cards = rig.View.Strip.Cards.ToList();
        var codex = cards.First(c => c.Item.Text.StartsWith("npm"));
        rig.View.Strip.ScrollTo(10000, animate: false);
        rig.Run(2.6);
        rig.Click(rig.At(codex, 60, 60));
        Assert.Equal("npm install -g @openai/codex", rig.Board.LastText);
        Assert.Equal(codex.Item, rig.M.Clips.Items[0]);
        Assert.Equal(codex.Item.Id.ToString(), rig.M.Clips.JustCopied);
        // it glides to the front (and the row glides back to the start), with no jumps between frames
        double prev = codex.X; double maxStep = 0;
        for (int i = 0; i < 60; i++)
        {
            rig.Clock.Now += 1 / 60.0;
            rig.Frame();
            maxStep = Math.Max(maxStep, Math.Abs(codex.X - prev));
            prev = codex.X;
        }
        Assert.Equal(0, codex.X, 1);
        Assert.True(maxStep < 120, $"jumped {maxStep:0} pt in one 60 Hz frame");
        Assert.Equal(0, rig.View.Strip.ScrollNow, 1);
        rig.Frame().Save(Path.Combine(Out("clipboard"), "copied.png"), new PngBitmapEncoderOptions());
    }

    [AvaloniaFact]
    public void ColourChipsCopyEachFormat()
    {
        using var rig = new Rig();
        rig.Run(2.4);
        var yellow = rig.View.Strip.Cards.First(c => c.Item.Color?.Hex == "#F2C94C");
        double cw = (ClipCard.W - 12 - 8) / 3, y = ClipCard.BodyH - 30 + 15;
        rig.Click(rig.At(yellow, 6 + cw / 2, y));
        Assert.Equal("#F2C94C", rig.Board.LastText);
        rig.Run(rig.Clock.Now + 1);
        yellow = rig.View.Strip.Cards.First(c => c.Item.Color?.Hex == "#F2C94C");
        rig.Click(rig.At(yellow, 6 + cw + 4 + cw / 2, y));
        Assert.Equal("rgb(242, 201, 76)", rig.Board.LastText);
        rig.Run(rig.Clock.Now + 1);
        yellow = rig.View.Strip.Cards.First(c => c.Item.Color?.Hex == "#F2C94C");
        rig.Click(rig.At(yellow, 6 + 2 * (cw + 4) + cw / 2, y));
        Assert.Equal("hsl(45, 86%, 62%)", rig.Board.LastText);
        Assert.EndsWith(":hsl", rig.M.Clips.JustCopied);
        rig.Frame().Save(Path.Combine(Out("clipboard"), "chip-copied.png"), new PngBitmapEncoderOptions());
    }

    [AvaloniaFact]
    public void SvgCardCopiesSvgOrPng()
    {
        using var rig = new Rig();
        rig.Run(2.4);
        var svg = rig.View.Strip.Cards.First(c => c.Item.Kind == ClipKind.Svg);
        rig.Win.MouseMove(rig.At(svg, 78, 40), RawInputModifiers.None);
        rig.Run(rig.Clock.Now + 0.5);
        Assert.True(svg.Hovered);
        rig.Frame().Save(Path.Combine(Out("clipboard"), "svg-hover.png"), new PngBitmapEncoderOptions());
        double cw = (ClipCard.W - 12 - 4) / 2, y = ClipCard.BodyH - 6 - 11;
        rig.Click(rig.At(svg, 6 + cw + 4 + cw / 2, y));
        Assert.NotNull(rig.Board.LastPng);
        rig.Run(rig.Clock.Now + 0.5);
        svg = rig.View.Strip.Cards.First(c => c.Item.Kind == ClipKind.Svg);
        rig.Win.MouseMove(rig.At(svg, 78, 40), RawInputModifiers.None);
        rig.Run(rig.Clock.Now + 0.5);
        rig.Click(rig.At(svg, 6 + cw / 2, y));
        Assert.StartsWith("<svg", rig.Board.LastText);
        Assert.Equal(rig.Board.LastText, rig.Board.LastSvg);
    }

    /// Hover shows the remove badge; clicking it removes the card, which shrinks away while the rest slide left.
    [AvaloniaFact]
    public void RemovingACardSlidesTheRestOver()
    {
        using var rig = new Rig();
        rig.Run(2.4);
        var cards = rig.View.Strip.Cards.ToList();
        var victim = cards[1]; var next = cards[2];
        double nextX = next.X;
        rig.Win.MouseMove(rig.At(victim, 60, 60), RawInputModifiers.None);
        rig.Run(rig.Clock.Now + 0.4);
        var badge = rig.At(victim, ClipCard.W - 7 - 9, 7 + 9);
        rig.Frame().Save(Path.Combine(Out("clipboard"), "hover-badge.png"), new PngBitmapEncoderOptions());
        rig.Click(badge);
        Assert.DoesNotContain(victim.Item, rig.M.Clips.Items);
        Assert.Equal(5, rig.M.Clips.Items.Count);
        rig.Run(rig.Clock.Now + 0.8);
        Assert.Equal(nextX - CardStrip.CardW - CardStrip.Gap, next.X, 1);
        Assert.DoesNotContain(victim, rig.View.Strip.Children);   // gone once it has shrunk away
    }

    /// The wheel scrolls the row on a spring: no step bigger than a frame's worth, no overshoot past the end.
    [AvaloniaFact]
    public void WheelScrollsSmoothlyAndStopsAtTheEnd()
    {
        using var rig = new Rig();
        rig.Run(2.4);
        var strip = rig.View.Strip;
        var p = strip.TranslatePoint(new Point(200, 60), rig.Win)!.Value;
        rig.Win.MouseWheel(p, new Vector(0, -1));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(90, strip.ScrollTarget, 1);
        double prev = 0, maxStep = 0;
        for (int i = 0; i < 40; i++)
        {
            rig.Clock.Now += 1 / 60.0;
            rig.Frame();
            double s = strip.ScrollNow;
            maxStep = Math.Max(maxStep, s - prev);
            Assert.True(s <= 90.5, $"overshot to {s:0.0}");
            prev = s;
        }
        Assert.Equal(90, prev, 0);
        Assert.True(maxStep < 30, $"stepped {maxStep:0} pt in a frame");
        for (int i = 0; i < 20; i++) rig.Win.MouseWheel(p, new Vector(0, -1));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(strip.MaxScroll, strip.ScrollTarget, 1);
    }

    [AvaloniaFact]
    public void ANewCopyGrowsInAtTheFront()
    {
        using var rig = new Rig();
        rig.Run(2.4);
        var first = rig.View.Strip.Cards.First();
        rig.M.Clips.Add(new ClipItem { Kind = ClipKind.Text, Text = "fresh" });
        var card = rig.View.Strip.Cards.First();
        Assert.Equal("fresh", card.Item.Text);
        Assert.True(card.Opacity < 0.05, "a new card starts invisible and grows in");
        rig.Run(rig.Clock.Now + 0.8);
        Assert.Equal(1, card.Opacity, 2);
        Assert.Equal(CardStrip.CardW + CardStrip.Gap, first.X, 1);
    }

    [AvaloniaFact]
    public void ClearShowsTheEmptyState()
    {
        using var rig = new Rig();
        rig.Run(2.4);
        rig.Click(rig.View.ClearButton.TranslatePoint(new Point(20, 13), rig.Win)!.Value);
        Assert.Empty(rig.M.Clips.Items);
        rig.Run(rig.Clock.Now + 0.8);
        Assert.True(rig.View.ShowingEmpty);
        Assert.False(rig.View.ClearButton.IsVisible);
        rig.Frame().Save(Path.Combine(Out("clipboard"), "empty.png"), new PngBitmapEncoderOptions());
    }

    [AvaloniaFact]
    public void HomeRowShowsTheNewestCopy()
    {
        using var rig = new Rig(tab: Tab.Home);
        rig.Run(2.4);
        var home = rig.Stage.Body.CurrentHome!;
        Assert.Equal("#5E6AD2", home.ClipRow.Title);
        Assert.Equal("Colour · just now", home.ClipRow.Detail);
        rig.M.Clips.Add(new ClipItem { Kind = ClipKind.Text, Text = "hello\nworld" });
        Assert.Equal("hello world", home.ClipRow.Title);
    }

    /// Frame by frame at 120 Hz: a copy arriving, a card removed, a card copied to the front. No frame changes much
    /// more of the picture than the frames either side of it (the same check that caught the Home Sprout ghost).
    [AvaloniaTheory]
    [InlineData("arrive")] [InlineData("remove")] [InlineData("front")]
    public void CardMotionHasNoPops(string what)
    {
        using var rig = new Rig();
        rig.Run(2.4);
        var cards = rig.View.Strip.Cards.ToList();
        switch (what)
        {
            case "arrive": rig.M.Clips.Add(new ClipItem { Kind = ClipKind.Color, Color = ColorValue.Parse("#E5484D") }); break;
            case "remove": rig.M.Clips.Remove(cards[1].Item); break;
            case "front": cards[2].Click("card"); break;
        }
        MediaTests.Px? prev = null;
        var jumps = new List<double>();
        for (int i = 1; i <= 72; i++)
        {
            rig.Clock.Now += 1 / 120.0;
            var px = new MediaTests.Px(rig.Frame(), 2);
            if (prev is not null)
            {
                int changed = 0, n = 0;
                for (double y = 84; y < 260; y += 3)
                for (double x = 98; x < 722; x += 3) { n++; if (MediaTests.Px.Dist(px.At(x, y), prev.At(x, y)) > 40) changed++; }
                jumps.Add((double)changed / n);
            }
            prev = px;
        }
        File.WriteAllText(Path.Combine(Out("clipboard-motion"), what + ".txt"), string.Join("\n", jumps.Select(j => j.ToString("0.0000"))));
        Assert.True(jumps.Take(30).Sum() > 0.05, "nothing moved");
        for (int i = 0; i < jumps.Count; i++)
        {
            double around = Math.Max(i > 0 ? jumps[i - 1] : 0, i + 1 < jumps.Count ? jumps[i + 1] : 0);
            Assert.False(jumps[i] > 0.02 && jumps[i] > 2.5 * around, $"a pop at frame {i + 2}: {jumps[i]:P1} changed (around {around:P1})");
        }
        Assert.True(jumps.Skip(60).All(j => j < 0.002), "still moving after half a second");
    }
}
