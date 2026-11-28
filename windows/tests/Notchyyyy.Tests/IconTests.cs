using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Notchyyyy.Theme;
using Xunit;

namespace Notchyyyy.Tests;

public class IconTests
{
    sealed class Sheet : Control
    {
        public double Size = 96, Pad = 24;
        public Color Ink = Colors.White;
        public override void Render(DrawingContext ctx)
        {
            ctx.FillRectangle(Brushes.Black, new Rect(Bounds.Size));
            var kinds = Enum.GetValues<IconKind>();
            for (int i = 0; i < kinds.Length; i++)
                Icons.Draw(ctx, kinds[i], new Point(Pad + Size / 2 + i * (Size + Pad), Pad + Size / 2), Size, Ink);
        }
    }

    static WriteableBitmap Render(double size, double pad)
    {
        int n = Enum.GetValues<IconKind>().Length;
        var w = new Window { Width = pad + n * (size + pad), Height = size + 2 * pad, Content = new Sheet { Size = size, Pad = pad } };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        var bmp = w.CaptureRenderedFrame()!;
        w.Close();
        return bmp;
    }

    [AvaloniaFact]
    public void EveryIconDrawsInsideItsBox()
    {
        double size = 96, pad = 24;
        var bmp = Render(size, pad);
        var dir = Path.Combine(Environment.GetEnvironmentVariable("NOTCHYYYY_FRAMES") ?? Path.GetTempPath(), "icons");
        Directory.CreateDirectory(dir);
        bmp.Save(Path.Combine(dir, "icons-96.png"), new PngBitmapEncoderOptions());
        Render(16, 12).Save(Path.Combine(dir, "icons-16.png"), new PngBitmapEncoderOptions());

        using var fb = bmp.Lock();
        var px = new byte[fb.RowBytes * fb.Size.Height];
        System.Runtime.InteropServices.Marshal.Copy(fb.Address, px, 0, px.Length);
        var kinds = Enum.GetValues<IconKind>();
        for (int i = 0; i < kinds.Length; i++)
        {
            int x0 = (int)(pad + i * (size + pad)), lit = 0, outside = 0;
            for (int y = 0; y < fb.Size.Height; y++)
            for (int x = x0 - (int)pad / 2; x < x0 + size + pad / 2; x++)
            {
                if (px[y * fb.RowBytes + x * 4 + 1] < 40) continue;
                bool inBox = x >= x0 - 1 && x <= x0 + size + 1 && y >= pad - 1 && y <= pad + size + 1;
                if (inBox) lit++; else outside++;
            }
            Assert.True(lit > size * size * 0.04, $"{kinds[i]} draws almost nothing ({lit} px)");
            Assert.True(outside == 0, $"{kinds[i]} spills {outside} px outside its box");
        }
    }
}

/// The app's own icon (the exe, the Start-menu shortcut, the installer): Sprout, happy, on the accent.
public class AppIconTests
{
    [Fact]
    public void AppIconRenders()
    {
        var dir = Path.Combine(Environment.GetEnvironmentVariable("NOTCHYYYY_FRAMES") ?? Path.GetTempPath(), "app-icon");
        Directory.CreateDirectory(dir);
        using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(512, 512));
        surface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
        Notchyyyy.Sprout.SproutPainter.Draw(surface.Canvas, new Avalonia.Rect(-70, -40, 652, 652),
            Notchyyyy.Sprout.SproutPose.For(Notchyyyy.Sprout.SproutMood.Happy, 0), 0, default, 0, drawShadow: false, deviceScale: 1);
        using var img = surface.Snapshot();
        using var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(dir, "app-icon-512.png"), data.ToArray());
        using var bmp = SkiaSharp.SKBitmap.FromImage(img);
        Assert.True(bmp.GetPixel(256, 300).Alpha > 200, "Sprout not drawn");
        Assert.True(bmp.GetPixel(256, 40).Alpha < 255 && bmp.GetPixel(256, 470).Alpha > 200, "Sprout should fill the icon, top to bottom");
    }
}
