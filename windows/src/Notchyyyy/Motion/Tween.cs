namespace Notchyyyy.Motion;

/// A CSS / SwiftUI cubic-bezier timing curve.
public sealed class Ease
{
    private readonly double _x1, _y1, _x2, _y2;
    public Ease(double x1, double y1, double x2, double y2) { _x1 = x1; _y1 = y1; _x2 = x2; _y2 = y2; }

    // SwiftUI's easeIn / easeOut / easeInOut use the same control points as CSS.
    public static readonly Ease Linear = new(0, 0, 1, 1);
    public static readonly Ease In = new(0.42, 0, 1, 1);
    public static readonly Ease Out = new(0, 0, 0.58, 1);
    public static readonly Ease InOut = new(0.42, 0, 0.58, 1);

    public double At(double p)
    {
        if (p <= 0) return 0;
        if (p >= 1) return 1;
        // solve x(u) = p for u with Newton, falling back to bisection
        double u = p;
        for (int i = 0; i < 8; i++)
        {
            double x = Bez(u, _x1, _x2) - p, d = BezD(u, _x1, _x2);
            if (Math.Abs(x) < 1e-6) break;
            if (Math.Abs(d) < 1e-6) { u = Bisect(p); break; }
            u = Math.Clamp(u - x / d, 0, 1);
        }
        return Bez(u, _y1, _y2);
    }

    private double Bisect(double p)
    {
        double lo = 0, hi = 1, u = p;
        for (int i = 0; i < 30; i++)
        {
            u = (lo + hi) / 2;
            if (Bez(u, _x1, _x2) < p) lo = u; else hi = u;
        }
        return u;
    }

    private static double Bez(double u, double p1, double p2) => 3 * (1 - u) * (1 - u) * u * p1 + 3 * (1 - u) * u * u * p2 + u * u * u;
    private static double BezD(double u, double p1, double p2) => 3 * (1 - u) * (1 - u) * p1 + 6 * (1 - u) * u * (p2 - p1) + 3 * u * u * (1 - p2);
}

/// A timed, eased value (fades and the content's "pour"): from wherever it is now to a target.
public sealed class TweenValue
{
    private double _from, _to, _start, _duration = 0.0001;
    private Ease _ease = Ease.Linear;

    public TweenValue(double value) { _from = _to = value; }

    public double Target => _to;

    public void Snap(double v) { _from = _to = v; _start = double.NegativeInfinity; }

    public void AnimateTo(double target, double duration, Ease ease, double now, double delay = 0)
    {
        if (target == _to) return;
        _from = Value(now);
        _to = target;
        _start = now + delay;
        _duration = Math.Max(duration, 0.0001);
        _ease = ease;
    }

    public double Value(double now)
    {
        double p = (now - _start) / _duration;
        if (p <= 0) return _from;
        if (p >= 1) return _to;
        return _from + (_to - _from) * _ease.At(p);
    }

    public bool IsSettled(double now) => now >= _start + _duration;
}

public interface IClock { double Now { get; } }

public sealed class SystemClock : IClock
{
    private readonly System.Diagnostics.Stopwatch _sw = System.Diagnostics.Stopwatch.StartNew();
    public double Now => _sw.Elapsed.TotalSeconds;
}

/// For tests and frame-by-frame renders: time only moves when told to.
public sealed class ManualClock : IClock
{
    public double Now { get; set; }
}
