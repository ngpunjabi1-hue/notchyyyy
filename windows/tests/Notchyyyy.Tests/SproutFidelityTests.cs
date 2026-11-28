using Avalonia;
using Notchyyyy.Sprout;
using Notchyyyy.Theme;
using SkiaSharp;
using Xunit;

namespace Notchyyyy.Tests;

/// The optimised painter (no per-frame blurs) against the original with live Gaussian blurs, pixel by pixel.
public class SproutFidelityTests
{
    static readonly SproutMood[] All = Enum.GetValues<SproutMood>();

    public static (double Mean, int P99, int Max, SKBitmap Ref, SKBitmap New) Compare(SproutMood mood, bool hp, bool light, double t, int px = 300)
    {
        SKBitmap Draw(bool reference)
        {
            var bmp = new SKBitmap(px, px, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var c = new SKCanvas(bmp);
            c.Clear(light ? new SKColor(249, 249, 250) : SKColors.Black);
            var rect = new Rect(px * 0.115, px * 0.115, px * 0.77, px * 0.77);
            var pose = SproutPose.For(mood, t);
            var gaze = new Point(0.3, -0.2);
            if (reference) SproutPainterReference.Draw(c, rect, pose, t, gaze, 0, true, hp ? 1 : 0, null, Accent.Indigo, light);
            else SproutPainter.Draw(c, rect, pose, t, gaze, 0, true, hp ? 1 : 0, null, Accent.Indigo, light, 1);
            return bmp;
        }
        var a = Draw(true); var b = Draw(false);
        var diffs = new List<int>();
        long sum = 0;
        for (int y = 0; y < px; y++)
        for (int x = 0; x < px; x++)
        {
            var p = a.GetPixel(x, y); var q = b.GetPixel(x, y);
            int d = Math.Max(Math.Abs(p.Red - q.Red), Math.Max(Math.Abs(p.Green - q.Green), Math.Abs(p.Blue - q.Blue)));
            diffs.Add(d); sum += d;
        }
        diffs.Sort();
        return (sum / (double)diffs.Count, diffs[(int)(diffs.Count * 0.99)], diffs[^1], a, b);
    }

    /// Every mood, with and without headphones, dark and light: on average under 1 level of 255 apart, 99 % of pixels
    /// within 4 levels, none more than 10 (measured: 0.5, 3 and 7).
    [Fact]
    public void LooksTheSameAsTheLiveBlurOriginal()
    {
        foreach (var light in new[] { false, true })
        foreach (var hp in new[] { false, true })
        foreach (var m in All)
        foreach (var t in new[] { 1.37, 2.9 })
        {
            var (mean, p99, max, _, _) = Compare(m, hp, light, t);
            Assert.True(mean <= 1 && p99 <= 4 && max <= 10, $"{m} (light {light}, headphones {hp}, t {t}): mean {mean:0.00}, p99 {p99}, max {max}");
        }
    }

    [Fact]
    public void Report()
    {
        var lines = new List<string>();
        foreach (var light in new[] { false, true })
        foreach (var hp in new[] { false, true })
        foreach (var m in All)
        {
            var (mean, p99, max, _, _) = Compare(m, hp, light, 1.37);
            lines.Add($"{(light ? "light" : "dark "),-5} {(hp ? "hp" : "  ")} {m,-10} mean {mean:0.00}  p99 {p99,3}  max {max,3}");
        }
        File.WriteAllLines(Path.Combine(Path.GetTempPath(), "sprout-fidelity.txt"), lines);
    }
}
