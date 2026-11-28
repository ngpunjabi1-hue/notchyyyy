using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Notchyyyy.Motion;
using Notchyyyy.Theme;

namespace Notchyyyy.Notch;

/// <summary>
/// What the closed pill shows: a live activity either side of its centre (for now the music one: art on the
/// left, bars in the artwork's colour on the right). With Sprout on, Sprout takes the left slot (it wears headphones while music plays)
/// and the art isn't shown, as on the Mac.
/// </summary>
public sealed class CollapsedBar : Control
{
    private readonly NotchModel _m;
    private readonly IClock _clock;
    private bool _live, _ticking;

    public CollapsedBar(NotchModel m, IClock clock) { _m = m; _clock = clock; }

    /// Set by the stage while the bar can be seen; the bars only animate then.
    public bool Live
    {
        get => _live;
        set { if (_live == value) return; _live = value; if (value) Tick(); }
    }

    /// Music started or stopped (or Sprout was switched): redraw now and (re)start the bars.
    public void Refresh() { InvalidateVisual(); Tick(); }

    private void Tick()
    {
        if (_ticking || !_live || !(_m.MusicPlaying || _m.Timing)) return;
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        _ticking = true;
        top.RequestAnimationFrame(_ =>
        {
            _ticking = false;
            InvalidateVisual();
            Tick();
        });
    }

    public override void Render(DrawingContext ctx)
    {
        if (!_m.LiveActivity)
        {
            // only in the minute before a water reminder, as a heads-up (with Sprout on the left)
            if (Prefs.Shared.Buddy && _m.Water.DueSoon)
            {
                var c = new Point(Bounds.Width - (6 + 12) - 12, Bounds.Height / 2);
                double f = Math.Min(1, _m.Water.Count / (double)Math.Max(1, _m.Water.Goal)), r = 7;
                ctx.DrawEllipse(null, new Pen(new SolidColorBrush(Colors.White.WithOpacity(0.14)), 2), c, r, r);
                if (f > 0.001)
                {
                    var arc = new StreamGeometry();
                    using (var g = arc.Open())
                    {
                        double end = Math.Min(f, 0.9999) * 2 * Math.PI - Math.PI / 2;
                        g.BeginFigure(new Point(c.X, c.Y - r), false);
                        g.ArcTo(new Point(c.X + r * Math.Cos(end), c.Y + r * Math.Sin(end)), new Size(r, r), 0, f > 0.5, SweepDirection.Clockwise);
                        g.EndFigure(false);
                    }
                    ctx.DrawGeometry(null, new Pen(new SolidColorBrush(PeekBubble.WaterBlue), 2, lineCap: PenLineCap.Round), arc);
                }
                Icons.Draw(ctx, IconKind.Drop, c, 8, PeekBubble.WaterBlue);
            }
            return;
        }
        double h = Bounds.Height, inset = 6 + 12;   // the bar's padding, then inset from the rounded ends
        double now = _clock.Now;

        if (_m.Timing)
        {
            // the stopwatch: its icon on the left, the time ("2:05") on the right
            Icons.Draw(ctx, IconKind.Stopwatch, new Point(inset + 12, h / 2), 13, LT.AccentOf(Prefs.Shared.Accent));
            var t = new FormattedText(Today.StopwatchModel.Format(_m.Stopwatch.Elapsed(now), centis: false), System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface(LT.Ui, FontStyle.Normal, FontWeight.SemiBold), 11.5, new SolidColorBrush(Color.FromRgb(235, 235, 235)));
            ctx.DrawText(t, new Point(Bounds.Width - inset - t.Width, h / 2 - t.Height / 2));
            Tick();
            return;
        }
        var media = _m.Media;
        var a = media.HasTrack ? media.Tint : LT.AccentOf(Prefs.Shared.Accent);
        // leading: the artwork (or a note without one), unless Sprout sits there
        if (!Prefs.Shared.Buddy)
        {
            var box = new Rect(inset + 1, (h - 22) / 2, 22, 22);
            if (media.Art is { } art)
            {
                // aspect fill, clipped to a rounded square
                var src = art.Size;
                double fit = Math.Max(22 / src.Width, 22 / src.Height);
                var dst = new Rect(box.Center.X - src.Width * fit / 2, box.Center.Y - src.Height * fit / 2, src.Width * fit, src.Height * fit);
                using (ctx.PushClip(new RoundedRect(box, 6)))
                    ctx.DrawImage(art, new Rect(src), dst);
            }
            else Icons.Draw(ctx, IconKind.Note, box.Center, 13, Color.FromRgb(191, 191, 191));
        }

        // trailing: four bars, each on its own beat (MusicBars, scaled 0.85)
        double cx = Bounds.Width - inset - 12, cy = h / 2, k = 0.85;
        double total = 4 * 2.5 + 3 * 2;
        for (int i = 0; i < 4; i++)
        {
            double v = Math.Abs(Math.Sin(now * (2.6 + i * 0.85) + i * 1.3));
            double bh = (4 + 9 * v) * k, bw = 2.5 * k;
            double x = cx - total * k / 2 + i * (2.5 + 2) * k;
            ctx.DrawRectangle(new SolidColorBrush(a), null, new Rect(x, cy - bh / 2, bw, bh), bw / 2, bw / 2);
        }
        Tick();
    }

}
