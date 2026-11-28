using Avalonia;
using Avalonia.Media;

namespace Notchyyyy.Theme;

public enum IconKind { Home, Media, Today, Apps, Clipboard, Stopwatch, System, Settings, Close, More }

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
