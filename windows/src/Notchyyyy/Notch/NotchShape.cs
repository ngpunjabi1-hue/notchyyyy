using Avalonia;
using Avalonia.Media;

namespace Notchyyyy.Notch;

/// <summary>
/// The notch silhouette: the top corners flare outwards into the screen edge, the bottom corners round in.
/// Same path as the Mac app's NotchShape (quadratic curves with radii <c>top</c> and <c>bottom</c>).
/// </summary>
public static class NotchShape
{
    public static StreamGeometry Build(Rect r, double top, double bottom, bool closed = true)
    {
        double t = Math.Max(0, Math.Min(top, r.Width / 2));
        double b = Math.Max(0, Math.Min(bottom, (r.Height - t) * 0.9));
        b = Math.Min(b, Math.Max(0, r.Width / 2 - t));
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(r.Left, r.Top), true);
            c.QuadraticBezierTo(new Point(r.Left + t, r.Top), new Point(r.Left + t, r.Top + t));
            c.LineTo(new Point(r.Left + t, r.Bottom - b));
            c.QuadraticBezierTo(new Point(r.Left + t, r.Bottom), new Point(r.Left + t + b, r.Bottom));
            c.LineTo(new Point(r.Right - t - b, r.Bottom));
            c.QuadraticBezierTo(new Point(r.Right - t, r.Bottom), new Point(r.Right - t, r.Bottom - b));
            c.LineTo(new Point(r.Right - t, r.Top + t));
            c.QuadraticBezierTo(new Point(r.Right - t, r.Top), new Point(r.Right, r.Top));
            c.EndFigure(closed);
        }
        return g;
    }
}
