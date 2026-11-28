using Avalonia;

namespace Notchyyyy.Sprout;

/// Sprout's feelings (the Mac app's SproutMood).
public enum SproutMood { Idle, Happy, Sleepy, Thirsty, Cheer, Running, Hurt, Surprised, Love, Dizzy, Music }

/// <summary>
/// One eye as a round-capped line through three points (design units, relative to the eye's centre).
/// A pill is a straight vertical line, ^ is the same line bent, – lies flat, ● has no length, so any eye can
/// morph into any other.
/// </summary>
public readonly record struct EyeLine(Point A, Point M, Point B, double W)
{
    public static EyeLine Mix(EyeLine x, EyeLine y, double p)
    {
        static Point L(Point u, Point v, double p) => new(u.X + (v.X - u.X) * p, u.Y + (v.Y - u.Y) * p);
        return new EyeLine(L(x.A, y.A, p), L(x.M, y.M, p), L(x.B, y.B, p), x.W + (y.W - x.W) * p);
    }

    /// A straight line of total length <paramref name="h"/> (caps included), turned by <paramref name="deg"/>, moved by <paramref name="off"/>.
    public static EyeLine Pill(double w, double h, double deg = 0, Point off = default)
    {
        double half = Math.Max(0.01, (h - w) / 2), r = deg * Math.PI / 180;
        double dx = Math.Sin(r) * half, dy = -Math.Cos(r) * half;
        return new EyeLine(new Point(off.X + dx, off.Y + dy), off, new Point(off.X - dx, off.Y - dy), w);
    }
}

/// How much of each motion is playing (they blend while moods change).
public record struct SproutMotion(double Idle, double Happy, double Cheer, double Love, double Sleepy, double Surprised, double Dizzy, double Running, double Music);

/// <summary>Everything about how Sprout holds itself that can blend from one mood to the next.</summary>
public sealed class SproutPose
{
    public EyeLine[] Eyes { get; private init; } = Array.Empty<EyeLine>();
    public double Blush { get; private init; }
    public double Hurt { get; private init; }
    public SproutMotion Motion { get; private init; }
    public (SproutMood Mood, double Amount)[] Signs { get; private init; } = Array.Empty<(SproutMood, double)>();

    private SproutPose() { }

    public static SproutPose For(SproutMood mood, double t)
    {
        double cyc = t % 4.2;
        if (cyc < 0) cyc += 4.2;
        double blink = cyc < 0.18 ? Math.Sin(cyc / 0.18 * Math.PI) : 0;
        EyeLine Eye(double side) => mood switch
        {
            SproutMood.Idle => EyeLine.Pill(6.4, Math.Max(6.4, 16 * (1 - blink * 0.75))),
            SproutMood.Running => EyeLine.Pill(6.4, 15, 14, new Point(1.5, 0)),
            SproutMood.Thirsty => EyeLine.Pill(6.4, 11, 0, new Point(1.5, -3)),
            SproutMood.Sleepy => EyeLine.Pill(4, 10, 90 + side * 9, new Point(0, 3)),
            SproutMood.Happy or SproutMood.Cheer or SproutMood.Love => new EyeLine(new(-4.4, 2.6), new(0, -2.6), new(4.4, 2.6), 3.6),
            SproutMood.Hurt => new EyeLine(new(side * 3.6, -4.2), new(-side * 3.6, 0), new(side * 3.6, 4.2), 3.6),
            SproutMood.Surprised => EyeLine.Pill(11.2, 11.2),
            SproutMood.Dizzy => EyeLine.Pill(2.2, 2.2),                                   // shrinks to the centre of the spiral
            _ => new EyeLine(new(-4, 2), new(0, -2), new(4, 2), 3.4),                     // music: eyes closed, enjoying it
        };
        var m = new SproutMotion();
        switch (mood)
        {
            case SproutMood.Idle or SproutMood.Thirsty or SproutMood.Hurt: m.Idle = 1; break;
            case SproutMood.Running: m.Running = 1; break;
            case SproutMood.Music: m.Music = 1; break;
            case SproutMood.Happy: m.Happy = 1; break;
            case SproutMood.Cheer: m.Cheer = 1; break;
            case SproutMood.Love: m.Love = 1; break;
            case SproutMood.Sleepy: m.Sleepy = 1; break;
            case SproutMood.Surprised: m.Surprised = 1; break;
            case SproutMood.Dizzy: m.Dizzy = 1; break;
        }
        bool hasSign = mood is SproutMood.Sleepy or SproutMood.Thirsty or SproutMood.Love or SproutMood.Surprised
                            or SproutMood.Cheer or SproutMood.Dizzy or SproutMood.Music;
        return new SproutPose
        {
            Eyes = new[] { Eye(-1), Eye(1) },
            Blush = mood is SproutMood.Happy or SproutMood.Cheer or SproutMood.Love ? 0.7 : mood == SproutMood.Music ? 0.4 : 0,
            Hurt = mood == SproutMood.Hurt ? 1 : 0,
            Motion = m,
            Signs = hasSign ? new[] { (mood, 1.0) } : Array.Empty<(SproutMood, double)>(),
        };
    }

    public static SproutPose Mix(SproutPose x, SproutPose y, double p)
    {
        double L(double a, double b) => a + (b - a) * p;
        var a = x.Motion; var b = y.Motion;
        return new SproutPose
        {
            Eyes = x.Eyes.Zip(y.Eyes, (u, v) => EyeLine.Mix(u, v, p)).ToArray(),
            Blush = L(x.Blush, y.Blush),
            Hurt = L(x.Hurt, y.Hurt),
            Motion = new SproutMotion(L(a.Idle, b.Idle), L(a.Happy, b.Happy), L(a.Cheer, b.Cheer), L(a.Love, b.Love), L(a.Sleepy, b.Sleepy),
                                      L(a.Surprised, b.Surprised), L(a.Dizzy, b.Dizzy), L(a.Running, b.Running), L(a.Music, b.Music)),
            Signs = x.Signs.Select(s => (s.Mood, s.Amount * (1 - p))).Concat(y.Signs.Select(s => (s.Mood, s.Amount * p))).ToArray(),
        };
    }
}

/// <summary>
/// Carries Sprout smoothly from one mood to the next: a change starts from exactly what was on screen (even
/// mid-change) and eases into the new pose over 0.45 s; the gaze glides after the pointer instead of jumping;
/// headphones slide on and off; the running steps speed up with your typing.
/// </summary>
public sealed class SproutTween
{
    private SproutMood? _target;
    private SproutPose? _from, _shown;
    private double _start, _lastT = double.NaN, _lastA = double.NaN, _paceNow = 1;
    private Point _gazeNow;
    public double Duration { get; set; } = 0.45;
    public double Headphones { get; private set; }
    public double RunPhase { get; private set; }

    public (SproutPose Pose, Point Gaze) Pose(SproutMood mood, Point gaze, double t)
    {
        if (mood != _target)
        {
            _from = _target is null ? null : _shown;
            _target = mood;
            _start = t;
        }
        double raw = Math.Clamp((t - _start) / Duration, 0, 1);
        double p = raw < 0.5 ? 4 * raw * raw * raw : 1 - Math.Pow(-2 * raw + 2, 3) / 2;     // ease in-out (cubic)
        var pose = SproutPose.For(mood, t);
        if (_from is not null && raw < 1) pose = SproutPose.Mix(_from, pose, p); else _from = null;
        _shown = pose;
        double dt = double.IsNaN(_lastT) ? 0 : Math.Clamp(t - _lastT, 0, 0.1);
        _lastT = t;
        double a = 1 - Math.Exp(-dt * 8);
        _gazeNow = new Point(_gazeNow.X + (gaze.X - _gazeNow.X) * a, _gazeNow.Y + (gaze.Y - _gazeNow.Y) * a);
        return (pose, _gazeNow);
    }

    /// Call once a frame after <see cref="Pose"/>: eases the headphones and advances the running steps.
    public (double Headphones, double RunPhase) Accessories(bool headphones, double pace, double t)
    {
        double dt = double.IsNaN(_lastA) ? 0 : Math.Clamp(t - _lastA, 0, 0.1);
        _lastA = t;
        Headphones += ((headphones ? 1 : 0) - Headphones) * (1 - Math.Exp(-dt * 7));
        _paceNow += (pace - _paceNow) * (1 - Math.Exp(-dt * 4));
        RunPhase += dt * 11 * _paceNow;
        return (Headphones, RunPhase);
    }
}
