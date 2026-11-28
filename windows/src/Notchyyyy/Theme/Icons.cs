using Avalonia;
using Avalonia.Media;

namespace Notchyyyy.Theme;

public enum IconKind { Home, Media, Today, Apps, Clipboard, Stopwatch, System, Settings, Close, More, Screenshot, Record, Drop, Chevron, Note,
    Brush, Pointer, Grid, Info, Minus, Plus, Power, Reset, Sparkle, Download, AppSquare }

/// <summary>
/// Notchyyyy's own icon set (the Mac app uses SF Symbols, which can't ship on Windows): filled, rounded shapes
/// drawn on a 24-unit grid. Corners are softened by stroking each shape in its own colour with round joins
/// (1 unit wide, so every cut-out is drawn 1 unit wider than it should look).
/// </summary>
public static class Icons
{
    /// Draws <paramref name="kind"/> filling a <paramref name="size"/>-pt square centred on <paramref name="c"/>.
    /// No PushTransform: each shape carries its own transform and pens are scaled by hand. (Nested pushed
    /// transforms were mis-composed by Avalonia's off-screen renderer at 2x scale, dropping every icon after the first.)
    public static void Draw(DrawingContext ctx, IconKind kind, Point c, double size, Color color)
    {
        double k = size / 24;
        var m = Matrix.CreateTranslation(-12, -12) * Matrix.CreateScale(k, k) * Matrix.CreateTranslation(c.X, c.Y);
        var fill = new SolidColorBrush(color);
        var soft = new Pen(fill, 1.0 * k, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var line = new Pen(fill, 2.3 * k, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        Geometry T(Geometry g) { g.Transform = new MatrixTransform(m); return g; }
        void Shape(Geometry g) => ctx.DrawGeometry(fill, soft, T(g));
        void Fill(Geometry g) => ctx.DrawGeometry(fill, null, T(g));
        void Stroke(Geometry g) => ctx.DrawGeometry(null, line, T(g));
        void Dot(Point p, double r) => ctx.DrawEllipse(fill, null, m.Transform(p), r * k, r * k);

        switch (kind)
        {
            case IconKind.Home:
            {
                var house = Poly((12, 3.4), (21, 11), (19.2, 11), (19.2, 19.6), (4.8, 19.6), (4.8, 11), (3, 11));
                var door = new RectangleGeometry(new Rect(9.5, 13.6, 5, 7), 1.4, 1.4);
                Shape(new CombinedGeometry(GeometryCombineMode.Exclude, house, door));
                break;
            }
            case IconKind.Media:
            {
                var front = new RectangleGeometry(new Rect(3.6, 8.2, 16.8, 13), 3, 3);
                var play = Poly((9.6, 10.6), (9.6, 18.8), (16.4, 14.7));
                Shape(new CombinedGeometry(GeometryCombineMode.Exclude, front, play));
                Fill(new RectangleGeometry(new Rect(5.6, 4.8, 12.8, 1.7), 0.85, 0.85));
                Fill(new RectangleGeometry(new Rect(7.8, 1.9, 8.4, 1.6), 0.8, 0.8));
                break;
            }
            case IconKind.Today:
            {
                foreach (var y in new[] { 5.5, 12.0, 18.5 })
                {
                    Stroke(Line((2.8, y), (4.7, y + 1.8), (7.9, y - 1.8)));
                    Stroke(Line((11.2, y), (21, y)));
                }
                break;
            }
            case IconKind.Apps:
                foreach (var (x, y) in new[] { (3.2, 3.2), (13, 3.2), (3.2, 13), (13, 13) })
                    Shape(new RectangleGeometry(new Rect(x, y, 7.8, 7.8), 2.2, 2.2));
                break;
            case IconKind.Clipboard:
            {
                var board = new RectangleGeometry(new Rect(4.4, 3.8, 15.2, 18), 2.6, 2.6);
                var gap = new RectangleGeometry(new Rect(7.1, 0.9, 9.8, 6.3), 2.2, 2.2);
                Geometry g = new CombinedGeometry(GeometryCombineMode.Exclude, board, gap);
                g = new CombinedGeometry(GeometryCombineMode.Exclude, g, new RectangleGeometry(new Rect(7.6, 10.6, 8.8, 2.6), 1.3, 1.3));
                g = new CombinedGeometry(GeometryCombineMode.Exclude, g, new RectangleGeometry(new Rect(7.6, 15, 6.4, 2.6), 1.3, 1.3));
                Shape(g);
                Shape(new RectangleGeometry(new Rect(8.8, 2.2, 6.4, 3.6), 1.4, 1.4));
                break;
            }
            case IconKind.Stopwatch:
            {
                var dial = new EllipseGeometry(new Rect(3.6, 5.6, 16.8, 16.8));
                var hand = new RectangleGeometry(new Rect(10.7, 8.4, 2.6, 7.4), 1.3, 1.3);
                Shape(new CombinedGeometry(GeometryCombineMode.Exclude, dial, hand));
                Shape(new RectangleGeometry(new Rect(9.6, 1.6, 4.8, 1.9), 0.95, 0.95));
                Fill(new RectangleGeometry(new Rect(11.2, 3, 1.6, 2.8)));
                // the side button, turned 45° about its centre (19, 6.4)
                ctx.DrawGeometry(fill, null, new RectangleGeometry(new Rect(17.9, 5, 2.2, 2.8), 0.8, 0.8)
                {
                    Transform = new MatrixTransform(Matrix.CreateTranslation(-19, -6.4) * Matrix.CreateRotation(Math.PI / 4)
                                                    * Matrix.CreateTranslation(19, 6.4) * m),
                });
                break;
            }
            case IconKind.System:
            {
                // a dial open at the bottom, dots along it and a needle pointing straight up
                var arc = new StreamGeometry();
                using (var a = arc.Open())
                {
                    var from = Polar(12, 13, 9.2, 225); var to = Polar(12, 13, 9.2, -45);
                    a.BeginFigure(from, false);
                    a.ArcTo(to, new Size(9.2, 9.2), 0, true, SweepDirection.Clockwise);
                    a.EndFigure(false);
                }
                Stroke(arc);
                foreach (var deg in new[] { 200.0, 155, 25, -20 })
                {
                    Dot(Polar(12, 13, 5.6, deg), 1.05);
                }
                Stroke(Line((12, 13), (12, 7)));
                Dot(new Point(12, 13), 2.3);
                break;
            }
            case IconKind.Settings:
            {
                // eight rounded teeth around a ring
                var gear = new StreamGeometry();
                using (var g = gear.Open())
                {
                    const int teeth = 8;
                    double ro = 10.4, ri = 8.0;
                    for (int i = 0; i < teeth; i++)
                    {
                        double a0 = i * 360.0 / teeth;
                        var pts = new[] { Polar(12, 12, ri, a0 - 22.5), Polar(12, 12, ri, a0 - 10), Polar(12, 12, ro, a0 - 7.5), Polar(12, 12, ro, a0 + 7.5), Polar(12, 12, ri, a0 + 10) };
                        if (i == 0) g.BeginFigure(pts[0], true); else g.LineTo(pts[0]);
                        for (int j = 1; j < pts.Length; j++) g.LineTo(pts[j]);
                    }
                    g.EndFigure(true);
                }
                Shape(new CombinedGeometry(GeometryCombineMode.Exclude, gear, new EllipseGeometry(new Rect(7.9, 7.9, 8.2, 8.2))));
                break;
            }
            case IconKind.Close:
                Stroke(Line((6.5, 6.5), (17.5, 17.5)));
                Stroke(Line((17.5, 6.5), (6.5, 17.5)));
                break;
            case IconKind.Screenshot:   // a viewfinder: four corner brackets around a lens
                foreach (var (x, y, dx, dy) in new[] { (3.5, 3.5, 1, 1), (20.5, 3.5, -1, 1), (3.5, 20.5, 1, -1), (20.5, 20.5, -1, -1) })
                    Stroke(Line((x, y + dy * 5.2), (x, y), (x + dx * 5.2, y)));
                Dot(new Point(12, 12), 3.6);
                break;
            case IconKind.Record:       // a ring with a dot in it
                ctx.DrawEllipse(null, line, m.Transform(new Point(12, 12)), 8.6 * k, 8.6 * k);
                Dot(new Point(12, 12), 4.4);
                break;
            case IconKind.Drop:
            {
                var d = new StreamGeometry();
                using (var g = d.Open())
                {
                    g.BeginFigure(new Point(12, 2.6), true);
                    g.CubicBezierTo(new Point(15.2, 7.4), new Point(19, 11.4), new Point(19, 15));
                    g.CubicBezierTo(new Point(19, 18.9), new Point(15.9, 21.6), new Point(12, 21.6));
                    g.CubicBezierTo(new Point(8.1, 21.6), new Point(5, 18.9), new Point(5, 15));
                    g.CubicBezierTo(new Point(5, 11.4), new Point(8.8, 7.4), new Point(12, 2.6));
                    g.EndFigure(true);
                }
                Shape(d);
                break;
            }
            case IconKind.Chevron:
                Stroke(Line((9, 5), (16, 12), (9, 19)));
                break;
            case IconKind.Note:         // a quaver: a tilted oval head, a stem and a flag
            {
                var head = new EllipseGeometry(new Rect(4, 14.4, 8, 6.2))
                {
                    Transform = new MatrixTransform(Matrix.CreateTranslation(-8, -17.5) * Matrix.CreateRotation(-0.35) * Matrix.CreateTranslation(8, 17.5) * m),
                };
                ctx.DrawGeometry(fill, null, head);
                Stroke(Line((11.4, 17), (11.4, 3.6)));
                var flag = new StreamGeometry();
                using (var g = flag.Open())
                {
                    g.BeginFigure(new Point(11.4, 3.6), false);
                    g.QuadraticBezierTo(new Point(19.5, 5.5), new Point(18, 11.5));
                    g.EndFigure(false);
                }
                Stroke(flag);
                break;
            }
            case IconKind.Brush:        // a paintbrush: a tilted handle and a rounded tip
            {
                var handle = new RectangleGeometry(new Rect(10.6, 2.5, 2.8, 11.5), 1.4, 1.4)
                    { Transform = new MatrixTransform(Matrix.CreateTranslation(-12, -12) * Matrix.CreateRotation(Math.PI / 5) * Matrix.CreateTranslation(13.5, 10.5) * m) };
                ctx.DrawGeometry(fill, soft, handle);
                var tip = new StreamGeometry();
                using (var g = tip.Open())
                {
                    g.BeginFigure(new Point(8.4, 15.4), true);
                    g.CubicBezierTo(new Point(10.6, 15.2), new Point(12.2, 17.2), new Point(11.4, 19.2));
                    g.CubicBezierTo(new Point(10.4, 21.6), new Point(6.2, 22.2), new Point(3.6, 21.2));
                    g.CubicBezierTo(new Point(5.4, 20), new Point(4.8, 17.2), new Point(6.2, 16.2));
                    g.CubicBezierTo(new Point(6.8, 15.7), new Point(7.6, 15.4), new Point(8.4, 15.4));
                    g.EndFigure(true);
                }
                Shape(tip);
                break;
            }
            case IconKind.Pointer:      // an arrow pointer with two motion lines
                Shape(Poly((9.5, 4), (19.5, 13.2), (14.6, 13.8), (17.4, 19.6), (15, 20.8), (12.2, 15), (9.5, 18.4)));
                Stroke(Line((3.2, 9.5), (6.4, 9.5)));
                Stroke(Line((2.6, 14.5), (6.2, 14.5)));
                break;
            case IconKind.Grid:         // three small tiles over a wide bar
                foreach (var x in new[] { 3.0, 9.6, 16.2 }) Shape(new RectangleGeometry(new Rect(x, 4, 4.8, 5.6), 1.4, 1.4));
                Shape(new RectangleGeometry(new Rect(3, 13.4, 18, 6.6), 1.8, 1.8));
                break;
            case IconKind.Info:
                ctx.DrawEllipse(null, line, m.Transform(new Point(12, 12)), 8.8 * k, 8.8 * k);
                Dot(new Point(12, 7.8), 1.4);
                Stroke(Line((12, 11), (12, 16.6)));
                break;
            case IconKind.Minus:
                Stroke(Line((6, 12), (18, 12)));
                break;
            case IconKind.Plus:
                Stroke(Line((6, 12), (18, 12)));
                Stroke(Line((12, 6), (12, 18)));
                break;
            case IconKind.Power:
            {
                var arc = new StreamGeometry();
                using (var g = arc.Open())
                {
                    g.BeginFigure(Polar(12, 12.8, 7.6, 125), false);
                    g.ArcTo(Polar(12, 12.8, 7.6, 55), new Size(7.6, 7.6), 0, true, SweepDirection.CounterClockwise);
                    g.EndFigure(false);
                }
                Stroke(arc);
                Stroke(Line((12, 3.4), (12, 11.4)));
                break;
            }
            case IconKind.Reset:        // an arrow going round anticlockwise
            {
                var arc = new StreamGeometry();
                using (var g = arc.Open())
                {
                    g.BeginFigure(Polar(12, 12, 7.6, 150), false);
                    g.ArcTo(Polar(12, 12, 7.6, 200), new Size(7.6, 7.6), 0, true, SweepDirection.Clockwise);
                    g.EndFigure(false);
                }
                Stroke(arc);
                var head = Polar(12, 12, 7.6, 150);
                Stroke(Line((head.X - 0.6, head.Y - 4.4), (head.X, head.Y), (head.X + 4.2, head.Y - 1.4)));
                break;
            }
            case IconKind.Sparkle:      // a four-pointed star and a little one
            {
                StreamGeometry Star(double cx, double cy, double r)
                {
                    var g = new StreamGeometry();
                    using var c = g.Open();
                    c.BeginFigure(new Point(cx, cy - r), true);
                    c.QuadraticBezierTo(new Point(cx, cy), new Point(cx + r, cy));
                    c.QuadraticBezierTo(new Point(cx, cy), new Point(cx, cy + r));
                    c.QuadraticBezierTo(new Point(cx, cy), new Point(cx - r, cy));
                    c.QuadraticBezierTo(new Point(cx, cy), new Point(cx, cy - r));
                    c.EndFigure(true);
                    return g;
                }
                Shape(Star(10.5, 13, 8));
                Shape(Star(18.6, 5.6, 3.4));
                break;
            }
            case IconKind.Download:     // an arrow into a ring
                ctx.DrawEllipse(null, line, m.Transform(new Point(12, 12)), 8.8 * k, 8.8 * k);
                Stroke(Line((12, 7), (12, 16)));
                Stroke(Line((8.4, 12.6), (12, 16.2), (15.6, 12.6)));
                break;
            case IconKind.AppSquare:
                Shape(new RectangleGeometry(new Rect(4, 4, 16, 16), 4.4, 4.4));
                break;
            case IconKind.More:
                foreach (var x in new[] { 5.5, 12, 18.5 }) Dot(new Point(x, 12), 2);
                break;
        }
    }

    private static Point Polar(double cx, double cy, double r, double deg) =>
        new(cx + r * Math.Cos(deg * Math.PI / 180), cy - r * Math.Sin(deg * Math.PI / 180));

    private static Geometry Poly(params (double x, double y)[] pts)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        c.BeginFigure(new Point(pts[0].x, pts[0].y), true);
        foreach (var p in pts.Skip(1)) c.LineTo(new Point(p.x, p.y));
        c.EndFigure(true);
        return g;
    }

    private static Geometry Line(params (double x, double y)[] pts)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        c.BeginFigure(new Point(pts[0].x, pts[0].y), false);
        foreach (var p in pts.Skip(1)) c.LineTo(new Point(p.x, p.y));
        c.EndFigure(false);
        return g;
    }
}
