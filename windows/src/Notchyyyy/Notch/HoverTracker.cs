using Avalonia;
using Notchyyyy.Motion;
using Notchyyyy.Theme;

namespace Notchyyyy.Notch;

/// <summary>
/// Opens the notch when the pointer rests on the pill and closes it soon after the pointer leaves,
/// a port of the Mac app's <c>track()</c> / <c>expand()</c> / <c>collapse()</c>.
/// Pointer positions are in DIPs relative to the top centre of the notch, y pointing down.
/// Time comes from an <see cref="IClock"/>, so tests can step it exactly.
/// </summary>
public sealed class HoverTracker
{
    private readonly NotchModel _m;
    private readonly IClock _clock;
    private double? _expandAt, _collapseAt;
    /// After a collapse the pointer must leave the pill before it can open again (no bounce-back).
    private bool _needsExit;
    private bool _wasPeeking, _bubbleInteractive;

    /// Told when the window should start or stop taking clicks.
    public Action<bool>? SetInteractive { get; set; }
    public Func<double> HoverDelay { get; set; } = () => Prefs.Shared.HoverDelay;
    public const double CollapseDelay = 0.35;

    public HoverTracker(NotchModel model, IClock clock) { _m = model; _clock = clock; }

    /// Called about 60 times a second with the pointer (null when it can't be read).
    public void Tick(Point? pointer)
    {
        double now = _clock.Now;
        var p = pointer ?? new Point(double.MaxValue / 4, double.MaxValue / 4);

        // a bubble is up: it takes clicks only inside itself, and the notch doesn't open under it
        if (_m.PeekShown)
        {
            var ps = _m.PeekSize;
            bool inBubble = Math.Abs(p.X) <= ps.Width / 2 && p.Y >= -2 && p.Y <= ps.Height;
            if (inBubble != _bubbleInteractive) { _bubbleInteractive = inBubble; SetInteractive?.Invoke(inBubble); }
            _expandAt = null;
            if (_m.Hovering) _m.Hovering = false;
            _wasPeeking = true;
            return;
        }
        if (_wasPeeking)
        {
            // the bubble just went: don't spring open under a pointer that was clicking it
            _wasPeeking = false;
            if (_bubbleInteractive && !_m.Expanded) { _bubbleInteractive = false; SetInteractive?.Invoke(false); }
            _needsExit = true;
        }

        if (_m.Expanded)
        {
            var b = _m.BodySize;
            var body = new Rect(-b.Width / 2, 0, b.Width, b.Height).Inflate(16);
            var pill = new Rect(-270, b.Height, 540, _m.PillGap + _m.PillHeight).Inflate(12);
            bool inside = body.Contains(p) || pill.Contains(p);
            if (inside || _m.MenuOpen) _collapseAt = null;
            else if (_collapseAt is null && !_m.Pinned) _collapseAt = now + CollapseDelay;
            if (_collapseAt is { } c && now >= c) Collapse();
        }
        else
        {
            double w = _m.NotchSize.Width + 112, h = _m.NotchSize.Height + 6;
            bool inside = Math.Abs(p.X) <= w / 2 && p.Y >= -2 && p.Y <= h;
            if (!inside) _needsExit = false;
            bool hot = inside && !_needsExit;
            if (hot != _m.Hovering) _m.Hovering = hot;
            if (hot) _expandAt ??= now + HoverDelay();
            else _expandAt = null;
            if (_expandAt is { } e && now >= e) Expand();
        }
    }

    public void Expand()
    {
        _expandAt = null;
        if (_m.Expanded) return;
        _m.EndPeek();
        _wasPeeking = false; _bubbleInteractive = false;
        SetInteractive?.Invoke(true);
        // Expanded first: once open, hovering no longer changes the size, so clearing it can't
        // start a hover spring that the delayed open-height spring would then have to undo.
        _m.Expanded = true;     // the stage's paired springs animate it
        _m.Hovering = false;
    }

    public void Collapse()
    {
        _collapseAt = null; _expandAt = null;
        if (!_m.Expanded) return;
        _needsExit = true;
        _m.Expanded = false;    // the stage's paired springs animate it
        _m.Hovering = false;
        SetInteractive?.Invoke(false);
    }
}
