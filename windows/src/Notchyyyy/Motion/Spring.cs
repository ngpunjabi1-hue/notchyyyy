namespace Notchyyyy.Motion;

/// <summary>
/// A spring in SwiftUI's terms: <c>spring(duration:bounce:)</c>, optionally delayed.
/// SwiftUI maps it to a unit-mass spring with stiffness (2π/duration)² and damping ratio 1 − bounce,
/// so the same numbers give the same curve as the Mac app.
/// </summary>
public readonly record struct SpringSpec(double Duration, double Bounce, double Delay = 0)
{
    /// SwiftUI's older form: <c>spring(response:dampingFraction:)</c>.
    public static SpringSpec Response(double response, double dampingFraction, double delay = 0) =>
        new(response, 1 - dampingFraction, delay);

    public double Omega => 2 * Math.PI / Duration;
    public double Zeta => Bounce >= 0 ? 1 - Bounce : 1 / (1 + Bounce);
    public SpringSpec WithDelay(double delay) => this with { Delay = delay };
}

/// <summary>
/// One animated number driven by a damped spring, solved exactly (not stepped), so the curve is the
/// same at any frame rate. Retargeting keeps the current value and velocity, like SwiftUI, so an
/// interrupted open turns into a close without a jump. A delayed retarget lets the running motion
/// carry on until its start time.
/// </summary>
public sealed class SpringValue
{
    private double _t0;          // start of the current segment
    private double _x0;          // displacement from the target at _t0
    private double _v0;          // velocity at _t0
    private double _target;
    private SpringSpec _spec = new(0.4, 0);
    private (double At, double Target, SpringSpec Spec)? _pending;
    private readonly double _epsilon;

    public SpringValue(double value, double epsilon = 0.01)
    {
        _target = value;
        _epsilon = epsilon;
    }

    /// The value it is heading to (including a delayed retarget that hasn't started yet).
    public double Target => _pending?.Target ?? _target;

    public void Snap(double value)
    {
        _pending = null;
        _target = value; _x0 = 0; _v0 = 0;
    }

    public void AnimateTo(double target, SpringSpec spec, double now)
    {
        ApplyPending(now);
        if (spec.Delay <= 0) { Retarget(target, spec, now); _pending = null; }
        else _pending = (now + spec.Delay, target, spec);
    }

    public double Value(double now)
    {
        ApplyPending(now);
        return _target + Displacement(now - _t0);
    }

    public double Velocity(double now)
    {
        ApplyPending(now);
        return Speed(now - _t0);
    }

    public bool IsSettled(double now)
    {
        if (_pending is not null) return false;
        double t = now - _t0;
        return Math.Abs(Displacement(t)) < _epsilon && Math.Abs(Speed(t)) < _epsilon * 10;
    }

    private void ApplyPending(double now)
    {
        if (_pending is { } p && now >= p.At)
        {
            _pending = null;
            Retarget(p.Target, p.Spec, p.At);
        }
    }

    private void Retarget(double target, SpringSpec spec, double at)
    {
        double t = at - _t0;
        double x = _target + Displacement(t), v = Speed(t);
        _t0 = at; _target = target; _spec = spec;
        _x0 = x - target; _v0 = v;
    }

    private double Displacement(double t) => Solve(t).x;
    private double Speed(double t) => Solve(t).v;

    /// Exact solution of x'' + 2ζω x' + ω² x = 0 from (x0, v0).
    private (double x, double v) Solve(double t)
    {
        if (t <= 0) return (_x0, _v0);
        if (_x0 == 0 && _v0 == 0) return (0, 0);
        double w = _spec.Omega, z = _spec.Zeta, x0 = _x0, v0 = _v0;
        if (z < 1)
        {
            double wd = w * Math.Sqrt(1 - z * z);
            double a = x0, b = (v0 + z * w * x0) / wd;
            double e = Math.Exp(-z * w * t), c = Math.Cos(wd * t), s = Math.Sin(wd * t);
            double x = e * (a * c + b * s);
            double v = e * ((-z * w) * (a * c + b * s) + (-a * wd * s + b * wd * c));
            return (x, v);
        }
        if (z == 1)
        {
            double b = v0 + w * x0, e = Math.Exp(-w * t);
            return (e * (x0 + b * t), e * (b - w * (x0 + b * t)));
        }
        {
            double r = w * Math.Sqrt(z * z - 1);
            double r1 = -z * w + r, r2 = -z * w - r;
            double c2 = (v0 - r1 * x0) / (r2 - r1), c1 = x0 - c2;
            double e1 = Math.Exp(r1 * t), e2 = Math.Exp(r2 * t);
            return (c1 * e1 + c2 * e2, c1 * r1 * e1 + c2 * r2 * e2);
        }
    }
}
