using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Notchyyyy.Motion;
using Notchyyyy.Theme;

namespace Notchyyyy.Notch;

/// <summary>
/// What the closed pill shows: a live activity either side of its centre (for now the music one: art on the
/// left, bars on the right). With Sprout on, Sprout takes the left slot (it wears headphones while music plays)
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
        if (_ticking || !_live || !_m.MusicPlaying) return;
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
        if (!_m.LiveActivity) return;
        double h = Bounds.Height, inset = 6 + 12;   // the bar's padding, then inset from the rounded ends
        double now = _clock.Now;

        var a = LT.AccentOf(Prefs.Shared.Accent);
        // leading: the artwork (a stand-in until real media arrives), unless Sprout sits there
        var art = new Rect(inset + 1, (h - 22) / 2, 22, 22);
        var grad = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Shade(a, 1.15), 0), new GradientStop(Shade(a, 0.55), 1) },
        };
        if (!Prefs.Shared.Buddy) ctx.DrawRectangle(grad, null, art, 6, 6);

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

    private static Color Shade(Color c, double f) =>
        Color.FromRgb((byte)Math.Clamp(c.R * f, 0, 255), (byte)Math.Clamp(c.G * f, 0, 255), (byte)Math.Clamp(c.B * f, 0, 255));
}
