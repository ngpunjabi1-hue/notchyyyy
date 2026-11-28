using Avalonia;
using Notchyyyy.Theme;
using SkiaSharp;

namespace Notchyyyy.Tests;
using Notchyyyy.Sprout;

/// <summary>
/// Paints Sprout, a soft glowing gradient sphere resting on the bottom edge of its rectangle, in a 100×100 design
/// space. A line-by-line port of the Mac app's SproutPainter, drawn with Skia so the glow, shadow, highlight and
/// blush get the same Gaussian blurs (SwiftUI's blur radius is the Gaussian's standard deviation).
/// </summary>
/// The original painter with live Gaussian blurs, kept as the reference the optimised one must match.
public static class SproutPainterReference
{
    public static readonly SKColor Blush = new(255, 122, 128);
    public static readonly SKColor Water = new(107, 189, 255);

    public const double FloorY = 92, Radius = 36;
    public static readonly Point Center = new(50, 92 - 36);

        
    

    private static SproutPainter.Palette PaletteFor(Accent a) => SproutPainter.PaletteFor(a);

    /// A 2-D affine map, applied like CGAffineTransform: x' = a·x + c·y + tx, y' = b·x + d·y + ty.
    private readonly record struct Affine(double A, double B, double C, double D, double Tx, double Ty)
    {
        public static Affine Translate(double x, double y) => new(1, 0, 0, 1, x, y);
        public static Affine Scale(double x, double y) => new(x, 0, 0, y, 0, 0);
        public static Affine Rotate(double r) => new(Math.Cos(r), Math.Sin(r), -Math.Sin(r), Math.Cos(r), 0, 0);
        /// this, then <paramref name="n"/> (CGAffineTransform.concatenating)
        public Affine Then(Affine n) => new(A * n.A + B * n.C, A * n.B + B * n.D, C * n.A + D * n.C, C * n.B + D * n.D,
                                            Tx * n.A + Ty * n.C + n.Tx, Tx * n.B + Ty * n.D + n.Ty);
        public SKPoint Map(double x, double y) => new((float)(A * x + C * y + Tx), (float)(B * x + D * y + Ty));
        public SKMatrix Sk => new((float)A, (float)C, (float)Tx, (float)B, (float)D, (float)Ty, 0, 0, 1);
    }

    private static SKPaint Fill(SKColor c, double blur = 0)
    {
        var p = new SKPaint { IsAntialias = true, Color = c, Style = SKPaintStyle.Fill };
        if (blur > 0) p.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, (float)blur);
        return p;
    }
    private static SKPaint Stroke(SKColor c, double w) => new()
    {
        IsAntialias = true, Color = c, Style = SKPaintStyle.Stroke, StrokeWidth = (float)w,
        StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round,
    };
    private static SKColor Op(SKColor c, double a) => c.WithAlpha((byte)Math.Round(Math.Clamp(a, 0, 1) * c.Alpha));

    /// A stadium centred on <paramref name="c"/>, turned by <paramref name="deg"/> degrees: its ends are exact semicircles.
    private static SKPath Capsule(SKPoint c, double w, double h, double deg = 0)
    {
        double r = Math.Min(w, h) / 2;
        var p = new SKPath();
        p.AddRoundRect(new SKRect((float)(-w / 2), (float)(-h / 2), (float)(w / 2), (float)(h / 2)), (float)r, (float)r);
        p.Transform(SKMatrix.CreateRotationDegrees((float)deg).PostConcat(SKMatrix.CreateTranslation(c.X, c.Y)));
        return p;
    }

    /// <param name="gaze">where Sprout looks, each axis −1…1, y down</param>
    /// <param name="squash">&gt; 0 flattens, &lt; 0 stretches</param>
    /// <param name="headphones">0…1 slides them on</param>
    /// <param name="runPhase">drives the running steps (faster with faster typing)</param>
    public static void Draw(SKCanvas c, Rect rect, SproutPose pose, double t, Point gaze = default, double squash = 0,
                            bool drawShadow = true, double headphones = 0, double? runPhase = null, Accent? accent = null, bool? light = null)
    {
        var pal = PaletteFor(accent ?? Prefs.Shared.Accent);
        bool isLight = light ?? LT.Light;
        double k = Math.Min(rect.Width, rect.Height) / 100;
        double ox = rect.Center.X - 50 * k, oy = rect.Bottom - 100 * k;

        // each motion runs on its own clock and is then weighted, so blending moods never jumps a phase
        var w = pose.Motion;
        double hop = Math.Abs(Math.Sin(t * 6)), jump = Math.Abs(Math.Sin(t * 7));
        // running: a quick step every half turn of the phase; music: a nod on the beat and a slow sway
        double phase = runPhase ?? t * 13;
        double step = Math.Abs(Math.Sin(phase)), beat = Math.Abs(Math.Sin(t * 6));
        double lift = w.Happy * hop * 4 + w.Cheer * jump * 10 + w.Love * jump * 4 + w.Running * step * 6 + w.Music * beat * 2.2;
        double sy = 1 + w.Idle * Math.Sin(t * 2.1) * 0.015 + w.Sleepy * Math.Sin(t * 1.1) * 0.02 + w.Surprised * 0.04
                    + (w.Cheer + w.Love) * 0.06 * (jump - 0.4) - w.Running * 0.07 * (1 - step) - w.Music * 0.025 * (1 - beat);
        sy -= squash;
        double sx = 1 / Math.Max(sy, 0.6);
        // leaning toward the pointer, swaying when dizzy or to music, leaning into a run; all pivot on the floor
        double calm = 1 - w.Dizzy, sway = Math.Sin(t * 4.6), groove = Math.Sin(t * 3);
        double angle = (gaze.X * 7 * calm * (1 - w.Running) + sway * 10 * w.Dizzy + groove * 6 * w.Music + 9 * w.Running) * Math.PI / 180;
        double shift = gaze.X * 3.5 * calm + sway * 3 * w.Dizzy + groove * 1.5 * w.Music + 2 * w.Running;
        double f = FloorY;
        var body = Affine.Translate(-50, -f).Then(Affine.Scale(sx, sy)).Then(Affine.Rotate(angle))
            .Then(Affine.Translate(50 + shift, f - lift)).Then(Affine.Scale(k, k)).Then(Affine.Translate(ox, oy));
        SKPoint P(double x, double y) => body.Map(x, y);
        // a design-space offset turned with the body (not scaled by squash)
        SKPoint Turn(Point v) => new((float)((v.X * Math.Cos(angle) - v.Y * Math.Sin(angle)) * k), (float)((v.X * Math.Sin(angle) + v.Y * Math.Cos(angle)) * k));
        var outline = new SKPath();
        outline.AddOval(new SKRect((float)(Center.X - Radius), (float)(Center.Y - Radius), (float)(Center.X + Radius), (float)(Center.Y + Radius)));
        outline.Transform(body.Sk);

        // speed lines streaming out behind a running Sprout
        if (w.Running > 0.01)
        {
            double[] ys = { 44, 58, 72 };
            for (int i = 0; i < 3; i++)
            {
                double u = (phase / (2 * Math.PI) * 0.9 + i / 3.0) % 1;
                if (u < 0) u += 1;
                double len = 8 + i % 2 * 5, x = 20 - u * 22;
                double a = (u < 0.15 ? u / 0.15 : 1 - (u - 0.15) / 0.85) * w.Running;
                using var paint = Fill(Op(pal.Rim, 0.85 * a));
                c.DrawPath(Capsule(new SKPoint((float)(ox + x * k), (float)(oy + (ys[i] - lift) * k)), len * k, 3 * k), paint);
            }
        }

        if (drawShadow)
        {
            double swd = 50 * sx * Math.Max(0.4, 1 - lift / 40);
            using var paint = Fill(Op(pal.Core, 0.32), 3.5 * k);
            c.DrawOval(new SKRect((float)(ox + (50 + shift - swd / 2) * k), (float)(oy + (f - 3) * k),
                                  (float)(ox + (50 + shift + swd / 2) * k), (float)(oy + (f + 3) * k)), paint);
        }

        using (var glow = Fill(Op(pal.Rim, (0.5 - 0.2 * w.Sleepy) * (isLight ? 0.7 : 1)), 7 * k))
            c.DrawPath(outline, glow);

        // a sphere: warm core a little low, pale rim
        var gc = P(50, 62);
        using (var sphere = new SKPaint { IsAntialias = true })
        {
            sphere.Shader = SKShader.CreateRadialGradient(gc, (float)(40 * k * sx), new[] { pal.Core, pal.Amber, pal.Rim },
                                                          new[] { 0f, 0.55f, 1f }, SKShaderTileMode.Clamp);
            c.DrawPath(outline, sphere);
        }
        if (pose.Hurt > 0.01)
        {
            using var hurt = new SKPaint { IsAntialias = true };
            var red = new SKColor(235, 77, 71);
            hurt.Shader = SKShader.CreateRadialGradient(gc, (float)(34 * k), new[] { Op(red, 0.75 * pose.Hurt), red.WithAlpha(0) },
                                                        null, SKShaderTileMode.Clamp);
            c.DrawPath(outline, hurt);
        }

        c.Save();
        c.ClipPath(outline, SKClipOperation.Intersect, true);
        using (var soft = Fill(new SKColor(255, 255, 255, 97), 6 * k))
        {
            var o = P(26, 24);
            c.DrawOval(new SKRect(o.X, o.Y, (float)(o.X + 30 * k * sx), (float)(o.Y + 16 * k * sy)), soft);
        }
        c.Restore();
        using (var spec = Fill(new SKColor(255, 255, 255, 153)))
            c.DrawPath(Capsule(P(31, 36), 4.4 * k, 11 * k, 32 + angle * 180 / Math.PI), spec);

        if (pose.Blush > 0.01)
        {
            using var paint = Fill(Op(Blush, pose.Blush), 2.2 * k);
            foreach (var x in new[] { 32.0, 68 })
            {
                var p = P(x + gaze.X * 3, 67 + gaze.Y * 1.5);
                c.DrawOval(new SKRect((float)(p.X - 5.5 * k), (float)(p.Y - 2.5 * k), (float)(p.X + 5.5 * k), (float)(p.Y + 2.5 * k)), paint);
            }
        }

        // Eyes: each is one round-capped line through three points, so a pill, a chevron, a dash and a dot
        // are all the same shape and any one can morph into another. They look where Sprout looks.
        var look = new Point(gaze.X * 5.5 * calm, gaze.Y * 4.5 * calm);
        for (int i = 0; i < 2; i++)
        {
            double side = i == 0 ? -1 : 1;
            var e = P(50 + side * 10.5 + look.X, 58 + look.Y);
            var l = pose.Eyes[i];
            var path = new SKPath();
            var pts = new[] { l.A, l.M, l.B };
            for (int j = 0; j < 3; j++)
            {
                var q = Turn(pts[j]);
                var pt = new SKPoint(e.X + q.X, e.Y + q.Y);
                if (j == 0) path.MoveTo(pt); else path.LineTo(pt);
            }
            using (var eye = Stroke(SKColors.White, l.W * k)) c.DrawPath(path, eye);

            // dizzy: a spinning spiral grows out of the shrunken eye
            if (w.Dizzy > 0.01)
            {
                var sp = new SKPath();
                double spin = t * 7;
                for (int s = 0; s <= 60; s++)
                {
                    double u = s / 60.0, a = spin + u * 2.6 * 2 * Math.PI, r = u * 7.4 * k * w.Dizzy;
                    var pt = new SKPoint((float)(e.X + Math.Cos(a) * r), (float)(e.Y + Math.Sin(a) * r));
                    if (s == 0) sp.MoveTo(pt); else sp.LineTo(pt);
                }
                using var paint = Stroke(Op(SKColors.White, w.Dizzy), 2.2 * k);
                c.DrawPath(sp, paint);
            }
        }

        // headphones: a band over the head and a cup on each side, in a deep shade of the accent
        if (headphones > 0.01)
        {
            using var layer = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Round(Math.Clamp(headphones, 0, 1) * 255)) };
            c.SaveLayer(layer);
            double down = (1 - headphones) * 14;     // they slide down onto the head
            SKPoint Q(double x, double y) { var p = P(x, y); return new SKPoint(p.X, (float)(p.Y - down * k)); }
            var ac = LT.AccentOf(accent ?? Prefs.Shared.Accent);
            var (ah, asat, ab) = Hsv.FromRgb(ac.R / 255.0, ac.G / 255.0, ac.B / 255.0);
            var deep = Hsv.ToSk(ah, Math.Min(1, asat * 1.05 + 0.1), ab * 0.42);
            var deeper = Hsv.ToSk(ah, Math.Min(1, asat * 1.1 + 0.1), ab * 0.28);
            double r = Radius + 4.5;
            var band = new SKPath();
            for (int i = 0; i <= 32; i++)
            {
                double th = Math.PI + i / 32.0 * Math.PI;
                var p = Q(Center.X + Math.Cos(th) * r, Center.Y + Math.Sin(th) * r);
                if (i == 0) band.MoveTo(p); else band.LineTo(p);
            }
            using (var bp = Stroke(deep, 4.6 * k)) c.DrawPath(band, bp);
            using (var hl = Stroke(new SKColor(255, 255, 255, 56), 1.2 * k)) c.DrawPath(band, hl);
            foreach (var side in new[] { -1.0, 1 })
            {
                var cc = Q(Center.X + side * (Radius + 0.5), Center.Y + 2);
                double tilt = angle * 180 / Math.PI;
                using (var cup = new SKPaint { IsAntialias = true })
                {
                    cup.Shader = SKShader.CreateLinearGradient(new SKPoint(cc.X, (float)(cc.Y - 10 * k)), new SKPoint(cc.X, (float)(cc.Y + 10 * k)),
                                                               new[] { deep, deeper }, null, SKShaderTileMode.Clamp);
                    c.DrawPath(Capsule(cc, 10 * k, 20 * k, tilt), cup);
                }
                using var shine = Fill(new SKColor(255, 255, 255, 77));
                c.DrawPath(Capsule(new SKPoint((float)(cc.X + side * 2.2 * k), (float)(cc.Y - 2.5 * k)), 2.4 * k, 8 * k, tilt), shine);
            }
            c.Restore();
        }

        // signs above the head fade in and out with their mood
        var top = P(50, Center.Y - Radius);
        foreach (var (mood, a) in pose.Signs)
        {
            if (a <= 0.01) continue;
            using var layer = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Round(Math.Clamp(a, 0, 1) * 255)) };
            c.SaveLayer(layer);
            Sign(c, mood, top, k, t, pal);
            c.Restore();
        }
    }

    private static void Sign(SKCanvas c, SproutMood mood, SKPoint top, double k, double t, SproutPainter.Palette pal)
    {
        float X(double v) => (float)v;
        switch (mood)
        {
            case SproutMood.Sleepy:      // three short lying pills drifting up
                for (int i = 0; i < 3; i++)
                {
                    double ph = (t * 0.35 + i / 3.0) % 1;
                    if (ph < 0) ph += 1;
                    double a = ph < 0.75 ? 1 - ph / 0.75 : 0;
                    var p = new SKPoint(X(top.X + (14 + ph * 10) * k), X(top.Y + (2 - ph * 20) * k));
                    using var paint = Fill(Op(pal.Rim, a));
                    c.DrawPath(Capsule(p, (5 + ph * 4) * k, 2.6 * k), paint);
                }
                break;
            case SproutMood.Thirsty:
                Drop(c, new SKPoint(X(top.X + 17 * k), X(top.Y - (7 + Math.Sin(t * 3) * 3) * k)), 2.6 * k);
                break;
            case SproutMood.Love:        // a heart built from two circles and a turned square, beating
            {
                double s = (1 + 0.12 * Math.Sin(t * 8)) * k;
                var hc = new SKPoint(X(top.X + 17 * k), X(top.Y - 6 * k));
                using var paint = Fill(Blush);
                c.DrawOval(new SKRect(X(hc.X - 4.6 * s), X(hc.Y - 4.6 * s), X(hc.X + 1.0 * s), X(hc.Y + 1.0 * s)), paint);
                c.DrawOval(new SKRect(X(hc.X - 1 * s), X(hc.Y - 4.6 * s), X(hc.X + 4.6 * s), X(hc.Y + 1.0 * s)), paint);
                var sq = new SKPath();
                sq.AddRect(new SKRect(X(-2.8 * s), X(-2.8 * s), X(2.8 * s), X(2.8 * s)));
                sq.Transform(SKMatrix.CreateRotation((float)(Math.PI / 4)).PostConcat(SKMatrix.CreateTranslation(hc.X, X(hc.Y + 0.4 * s))));
                c.DrawPath(sq, paint);
                break;
            }
            case SproutMood.Surprised:   // !
            {
                using var paint = Fill(pal.Rim);
                c.DrawPath(Capsule(new SKPoint(X(top.X + 17 * k), X(top.Y - 9 * k)), 3.6 * k, 10 * k), paint);
                c.DrawOval(new SKRect(X(top.X + 15.2 * k), X(top.Y - 1.8 * k), X(top.X + 18.8 * k), X(top.Y + 1.8 * k)), paint);
                break;
            }
            case SproutMood.Dizzy:       // little stars circling the head; the ones behind are smaller and fainter
                for (int i = 0; i < 3; i++)
                {
                    double a = t * 4 + i * 2 * Math.PI / 3, depth = (Math.Sin(a) + 1) / 2;
                    var p = new SKPoint(X(top.X + Math.Cos(a) * 20 * k), X(top.Y + (-2 + Math.Sin(a) * 4) * k));
                    double r = (1.4 + depth * 1.6) * k;
                    var star = new SKPath();
                    star.MoveTo(X(p.X - r * 1.6), p.Y); star.LineTo(X(p.X + r * 1.6), p.Y);
                    star.MoveTo(p.X, X(p.Y - r * 1.6)); star.LineTo(p.X, X(p.Y + r * 1.6));
                    using var paint = Stroke(Op(pal.Rim, 0.45 + 0.55 * depth), r * 0.9);
                    c.DrawPath(star, paint);
                }
                break;
            case SproutMood.Music:       // two little notes drifting up, one after the other
                for (int i = 0; i < 2; i++)
                {
                    double ph = (t * 0.45 + i * 0.5) % 1;
                    if (ph < 0) ph += 1;
                    double a = ph < 0.15 ? ph / 0.15 : ph < 0.7 ? 1 : 1 - (ph - 0.7) / 0.3;
                    var b = new SKPoint(X(top.X + (i == 0 ? 15 : -17) * k + Math.Sin(ph * 6) * 1.5 * k), X(top.Y + (-2 - ph * 16) * k));
                    Note(c, b, k * 0.9, Op(pal.Rim, a));
                }
                break;
            case SproutMood.Cheer:       // two little dots popping either side
            {
                double s = (0.5 + 0.5 * Math.Sin(t * 7)) * 2.4 * k;
                using var paint = Fill(pal.Rim);
                foreach (var p in new[] { new SKPoint(X(top.X - 20 * k), X(top.Y - 2 * k)), new SKPoint(X(top.X + 20 * k), X(top.Y - 5 * k)) })
                    c.DrawOval(new SKRect(X(p.X - s), X(p.Y - s), X(p.X + s), X(p.Y + s)), paint);
                break;
            }
        }
    }

    /// a quaver: a tilted oval head, a stem and a flag
    private static void Note(SKCanvas c, SKPoint p, double k, SKColor color)
    {
        var head = new SKPath();
        head.AddOval(new SKRect((float)(-3.2 * k), (float)(-2.3 * k), (float)(3.2 * k), (float)(2.3 * k)));
        head.Transform(SKMatrix.CreateRotation(-0.4f).PostConcat(SKMatrix.CreateTranslation(p.X, p.Y)));
        using (var fill = Fill(color)) c.DrawPath(head, fill);
        var stem = new SKPath();
        stem.MoveTo((float)(p.X + 2.6 * k), (float)(p.Y - 0.5 * k));
        stem.LineTo((float)(p.X + 2.6 * k), (float)(p.Y - 11 * k));
        stem.QuadTo((float)(p.X + 6.8 * k), (float)(p.Y - 9 * k), (float)(p.X + 6.4 * k), (float)(p.Y - 5 * k));
        using var paint = Stroke(color, 1.5 * k);
        c.DrawPath(stem, paint);
    }

    public static void Drop(SKCanvas c, SKPoint p, double s)
    {
        var d = new SKPath();
        float X(double v) => (float)v;
        d.MoveTo(p.X, X(p.Y - 2.6 * s));
        d.CubicTo(X(p.X + 2.3 * s), X(p.Y - 0.2 * s), X(p.X + 2 * s), X(p.Y + 1.9 * s), p.X, X(p.Y + 1.9 * s));
        d.CubicTo(X(p.X - 2 * s), X(p.Y + 1.9 * s), X(p.X - 2.3 * s), X(p.Y - 0.2 * s), p.X, X(p.Y - 2.6 * s));
        using var paint = Fill(Water);
        c.DrawPath(d, paint);
    }
}

