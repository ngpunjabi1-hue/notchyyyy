using Avalonia;
using Avalonia.Media;

namespace Notchyyyy.Theme;

public enum IconKind { Home, Media, Today, Apps, Clipboard, Stopwatch, System, Settings, Close, More, Screenshot, Record, Drop, Chevron, Note,
    Brush, Pointer, Grid, Info, Minus, Plus, Power, Reset, Sparkle, Download, AppSquare,
    Play, Pause, Next, Previous, Shuffle, Repeat, RepeatOne, Trash, TextLines, Photo, Palette, Shapes, Check,
    Pencil, Search, PlusCircle, CheckCircle, Cpu, Gpu, Memory, Drive }

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
            case IconKind.Play:         // a rounded triangle, its weight a touch right of centre like SF's play.fill
                Shape(Poly((7, 4.6), (7, 19.4), (19.6, 12)));
                break;
            case IconKind.Pause:
                Shape(new RectangleGeometry(new Rect(6, 4.8, 4.2, 14.4), 1.4, 1.4));
                Shape(new RectangleGeometry(new Rect(13.8, 4.8, 4.2, 14.4), 1.4, 1.4));
                break;
            case IconKind.Next:         // two triangles pointing right (forward.fill)
                Shape(Poly((2.6, 6), (2.6, 18), (12, 12)));
                Shape(Poly((12, 6), (12, 18), (21.4, 12)));
                break;
            case IconKind.Previous:
                Shape(Poly((21.4, 6), (21.4, 18), (12, 12)));
                Shape(Poly((12, 6), (12, 18), (2.6, 12)));
                break;
            case IconKind.Shuffle:      // two crossing paths, each ending in an arrowhead
            {
                var a = new StreamGeometry();
                using (var g = a.Open())
                {
                    g.BeginFigure(new Point(3, 7), false);
                    g.LineTo(new Point(6.5, 7));
                    g.CubicBezierTo(new Point(11.5, 7), new Point(12.5, 17), new Point(17.5, 17));
                    g.LineTo(new Point(19.5, 17));
                    g.EndFigure(false);
                    g.BeginFigure(new Point(3, 17), false);
                    g.LineTo(new Point(6.5, 17));
                    g.CubicBezierTo(new Point(11.5, 17), new Point(12.5, 7), new Point(17.5, 7));
                    g.LineTo(new Point(19.5, 7));
                    g.EndFigure(false);
                }
                Stroke(a);
                Shape(Poly((18.4, 3.6), (22.2, 7), (18.4, 10.4)));
                Shape(Poly((18.4, 13.6), (22.2, 17), (18.4, 20.4)));
                break;
            }
            case IconKind.Repeat:
            case IconKind.RepeatOne:    // a loop: two rounded runs, each ending in an arrowhead
            {
                var a = new StreamGeometry();
                using (var g = a.Open())
                {
                    g.BeginFigure(new Point(4, 12.5), false);
                    g.LineTo(new Point(4, 10.5));
                    g.ArcTo(new Point(8, 6.5), new Size(4, 4), 0, false, SweepDirection.Clockwise);
                    g.LineTo(new Point(18.5, 6.5));
                    g.EndFigure(false);
                    g.BeginFigure(new Point(20, 11.5), false);
                    g.LineTo(new Point(20, 13.5));
                    g.ArcTo(new Point(16, 17.5), new Size(4, 4), 0, false, SweepDirection.Clockwise);
                    g.LineTo(new Point(5.5, 17.5));
                    g.EndFigure(false);
                }
                Stroke(a);
                Shape(Poly((17.4, 3.2), (21.2, 6.5), (17.4, 9.8)));
                Shape(Poly((6.6, 14.2), (2.8, 17.5), (6.6, 20.8)));
                if (kind == IconKind.RepeatOne)
                    Stroke(Line((10.6, 10.6), (12.2, 9.6), (12.2, 14.4)));
                break;
            }
            case IconKind.Trash:        // a bin with a lid and a handle
                Shape(new CombinedGeometry(GeometryCombineMode.Exclude,
                    Poly((5.4, 7.6), (18.6, 7.6), (17.4, 20.4), (6.6, 20.4)),
                    new CombinedGeometry(GeometryCombineMode.Union,
                        new RectangleGeometry(new Rect(9.1, 10.2, 1.8, 7.6), 0.9, 0.9),
                        new RectangleGeometry(new Rect(13.1, 10.2, 1.8, 7.6), 0.9, 0.9))));
                Fill(new RectangleGeometry(new Rect(3.6, 4.6, 16.8, 2.2), 1.1, 1.1));
                Fill(new RectangleGeometry(new Rect(9.2, 2.4, 5.6, 2.6), 1.1, 1.1));
                break;
            case IconKind.TextLines:    // left-aligned lines of text
                Stroke(Line((4, 6), (20, 6)));
                Stroke(Line((4, 10.7), (15, 10.7)));
                Stroke(Line((4, 15.4), (20, 15.4)));
                Stroke(Line((4, 20), (12.5, 20)));
                break;
            case IconKind.Photo:        // a framed picture: a sun and a hill cut out of it
            {
                Geometry g = new RectangleGeometry(new Rect(2.8, 4.4, 18.4, 15.2), 3, 3);
                g = new CombinedGeometry(GeometryCombineMode.Exclude, g, new EllipseGeometry(new Rect(6.6, 7.6, 4, 4)));
                g = new CombinedGeometry(GeometryCombineMode.Exclude, g, Poly((5.2, 17.6), (10.4, 12.2), (13.4, 15.2), (15.6, 13), (19.2, 17.6)));
                Shape(g);
                break;
            }
            case IconKind.Palette:      // a painter's palette with paint wells
            {
                Geometry g = new EllipseGeometry(new Rect(2.6, 3.4, 18.8, 17.2));
                g = new CombinedGeometry(GeometryCombineMode.Exclude, g, new EllipseGeometry(new Rect(14.2, 13.4, 5.8, 5.8)));
                foreach (var (x, y) in new[] { (6.4, 9.6), (9.6, 6.4), (14, 6.6) })
                    g = new CombinedGeometry(GeometryCombineMode.Exclude, g, new EllipseGeometry(new Rect(x, y, 3.2, 3.2)));
                g = new CombinedGeometry(GeometryCombineMode.Exclude, g, new EllipseGeometry(new Rect(5.8, 14, 3.2, 3.2)));
                Shape(g);
                break;
            }
            case IconKind.Shapes:       // a square overlapping a circle (vector shapes)
                Shape(new RectangleGeometry(new Rect(3.2, 3.2, 10.6, 10.6), 2, 2));
                Shape(new CombinedGeometry(GeometryCombineMode.Exclude,
                    new EllipseGeometry(new Rect(9.6, 9.6, 11.4, 11.4)), new RectangleGeometry(new Rect(2, 2, 13.4, 13.4), 3, 3)));
                break;
            case IconKind.Check:
                Stroke(Line((4.6, 12.6), (9.6, 17.4), (19.6, 6.6)));
                break;
            case IconKind.Pencil:       // a pencil at 45°: the body with its point at the bottom left, the eraser apart
                Shape(Poly((14.4, 5.6), (18.4, 9.6), (8.6, 19.4), (3.8, 20.2), (4.6, 15.4)));
                Shape(Poly((16, 4), (17.6, 2.4), (21.6, 6.4), (20, 8)));
                break;
            case IconKind.Search:       // a magnifying glass
                ctx.DrawEllipse(null, line, m.Transform(new Point(10.4, 10.4)), 6.4 * k, 6.4 * k);
                Stroke(Line((15.2, 15.2), (20.4, 20.4)));
                break;
            case IconKind.PlusCircle:
                ctx.DrawEllipse(null, new Pen(fill, 1.9 * k), m.Transform(new Point(12, 12)), 9.2 * k, 9.2 * k);
                Stroke(Line((12, 7.6), (12, 16.4)));
                Stroke(Line((7.6, 12), (16.4, 12)));
                break;
            case IconKind.CheckCircle:
            {
                var disc = new EllipseGeometry(new Rect(2.4, 2.4, 19.2, 19.2));
                var tick = new StreamGeometry();
                using (var g = tick.Open())
                {
                    // the tick, cut out of the disc as a 2.4-unit-wide band
                    g.BeginFigure(new Point(6.8, 12.4), true);
                    g.LineTo(new Point(8.5, 10.7)); g.LineTo(new Point(10.7, 12.9)); g.LineTo(new Point(15.5, 8.1));
                    g.LineTo(new Point(17.2, 9.8)); g.LineTo(new Point(10.7, 16.3));
                    g.EndFigure(true);
                }
                Fill(new CombinedGeometry(GeometryCombineMode.Exclude, disc, tick));
                break;
            }
            case IconKind.Cpu:          // a chip: a square with a die cut out and pins on every side
            {
                Shape(new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(5, 5, 14, 14), 2.6, 2.6), new RectangleGeometry(new Rect(9, 9, 6, 6), 1, 1)));
                foreach (var p in new[] { 9.0, 12, 15 })
                {
                    Fill(new RectangleGeometry(new Rect(p - 0.8, 1.6, 1.6, 2.6), 0.8, 0.8));
                    Fill(new RectangleGeometry(new Rect(p - 0.8, 19.8, 1.6, 2.6), 0.8, 0.8));
                    Fill(new RectangleGeometry(new Rect(1.6, p - 0.8, 2.6, 1.6), 0.8, 0.8));
                    Fill(new RectangleGeometry(new Rect(19.8, p - 0.8, 2.6, 1.6), 0.8, 0.8));
                }
                break;
            }
            case IconKind.Gpu:          // a graphics card: a board with a fan, and its bracket
            {
                Shape(new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(3, 6, 18, 11), 2.4, 2.4), new EllipseGeometry(new Rect(10.2, 8.2, 6.6, 6.6))));
                Fill(new RectangleGeometry(new Rect(5.5, 17.6, 9, 2), 0.8, 0.8));
                Dot(new Point(13.5, 11.5), 1.3);
                break;
            }
            case IconKind.Memory:       // a memory stick: a long board with chips cut out and a row of contacts
            {
                Geometry g = new RectangleGeometry(new Rect(2.4, 6.4, 19.2, 9.6), 2, 2);
                foreach (var x in new[] { 5.2, 10.2, 15.2 }) g = new CombinedGeometry(GeometryCombineMode.Exclude, g, new RectangleGeometry(new Rect(x, 8.6, 3.6, 4.6), 0.8, 0.8));
                Shape(g);
                foreach (var x in new[] { 4.6, 7.6, 10.6, 13.6, 16.6 }) Fill(new RectangleGeometry(new Rect(x, 16.8, 1.8, 2.6), 0.6, 0.6));
                break;
            }
            case IconKind.Drive:        // a drive: a rounded slab with a light
                Shape(new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(2.6, 7, 18.8, 10), 3, 3), new EllipseGeometry(new Rect(15.6, 10.6, 2.8, 2.8))));
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
