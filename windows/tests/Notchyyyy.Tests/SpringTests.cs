using Notchyyyy.Motion;
using Notchyyyy.Theme;
using Xunit;

namespace Notchyyyy.Tests;

/// The springs must match SwiftUI's: checked against an independent RK4 integration of
/// x'' = -k (x - target) - c x' with k = (2π/duration)², c = 4π(1 - bounce)/duration (Apple's documented mapping).
public class SpringTests
{
    static (double x, double v) Rk4(double x, double v, double target, double duration, double bounce, double t, double dt = 1e-5)
    {
        double k = Math.Pow(2 * Math.PI / duration, 2), c = 4 * Math.PI * (1 - bounce) / duration;
        (double, double) F(double xx, double vv) => (vv, -k * (xx - target) - c * vv);
        for (double s = 0; s < t - 1e-12; s += dt)
        {
            double h = Math.Min(dt, t - s);
            var (a1, b1) = F(x, v);
            var (a2, b2) = F(x + h / 2 * a1, v + h / 2 * b1);
            var (a3, b3) = F(x + h / 2 * a2, v + h / 2 * b2);
            var (a4, b4) = F(x + h * a3, v + h * b3);
            x += h / 6 * (a1 + 2 * a2 + 2 * a3 + a4);
            v += h / 6 * (b1 + 2 * b2 + 2 * b3 + b4);
        }
        return (x, v);
    }

    [Theory]
    [InlineData(0.42, 0.18)]   // open width
    [InlineData(0.52, 0.22)]   // open height
    [InlineData(0.34, 0.0)]    // close height (critically damped)
    [InlineData(0.40, 0.0)]    // close width
    [InlineData(0.30, 0.22)]   // hover
    public void MatchesSwiftUISpringOverTime(double duration, double bounce)
    {
        var s = new SpringValue(192);
        s.AnimateTo(700, new SpringSpec(duration, bounce), 0);
        for (double t = 0; t <= 1.2; t += 1.0 / 120)
        {
            var (x, v) = Rk4(192, 0, 700, duration, bounce, t);
            Assert.InRange(s.Value(t) - x, -1e-3, 1e-3);
            Assert.InRange(s.Velocity(t) - v, -1e-2, 1e-2);
        }
    }

    [Fact]
    public void OpenHeightOvershootsALittleThenSettles()
    {
        var s = new SpringValue(32);
        s.AnimateTo(300, LT.OpenTall with { Delay = 0 }, 0);
        double peak = 0;
        for (double t = 0; t < 2; t += 0.001) peak = Math.Max(peak, s.Value(t));
        // bounce 0.22: a visible but small overshoot (a few percent of the 268 pt travel)
        Assert.InRange(peak - 300, 3, 25);
        Assert.True(s.IsSettled(1.6));
    }

    [Fact]
    public void ClosingNeverUndershoots()
    {
        foreach (var spec in new[] { LT.CloseTall, LT.CloseWide })
        {
            var s = new SpringValue(300);
            s.AnimateTo(32, spec, 0);
            for (double t = 0; t < 2; t += 0.001) Assert.True(s.Value(t) >= 32 - 1e-9, $"undershoot at {t}");
        }
    }

    [Fact]
    public void DelayHoldsTheCurrentMotionThenStarts()
    {
        var s = new SpringValue(32);
        s.AnimateTo(300, LT.OpenTall, 0);    // 0.03 s delay
        Assert.Equal(32, s.Value(0.0299), 9);
        Assert.Equal(32, s.Value(0.03), 9);
        Assert.True(s.Value(0.05) > 32);
        // and it's the same curve, just shifted
        var plain = new SpringValue(32);
        plain.AnimateTo(300, LT.OpenTall with { Delay = 0 }, 0);
        Assert.Equal(plain.Value(0.2), s.Value(0.23), 9);
    }

    [Fact]
    public void InterruptingKeepsPositionAndVelocity()
    {
        // open, then close half way through: no jump in position or speed at the switch
        var s = new SpringValue(192);
        s.AnimateTo(700, LT.OpenWide, 0);
        double tSwitch = 0.15;
        double x = s.Value(tSwitch), v = s.Velocity(tSwitch);
        s.AnimateTo(192, LT.CloseWide with { Delay = 0 }, tSwitch);
        Assert.Equal(x, s.Value(tSwitch), 9);
        Assert.Equal(v, s.Velocity(tSwitch), 6);
        Assert.Equal(x + v * 1e-6, s.Value(tSwitch + 1e-6), 6);   // carries on at the same speed
    }

    [Fact]
    public void DelayedRetargetLetsTheOldMotionRunUntilItStarts()
    {
        var s = new SpringValue(192);
        s.AnimateTo(700, LT.OpenWide, 0);
        var reference = new SpringValue(192);
        reference.AnimateTo(700, LT.OpenWide, 0);
        s.AnimateTo(192, LT.CloseWide, 0.2);       // 0.04 s delay
        Assert.Equal(reference.Value(0.22), s.Value(0.22), 9);   // still opening during the delay
        Assert.Equal(reference.Value(0.24), s.Value(0.24), 9);
        Assert.True(s.Velocity(0.40) < 0);                       // then heading back
    }

    [Fact]
    public void EaseCurvesMatchCss()
    {
        // reference values of cubic-bezier(0.42,0,1,1) and (0,0,0.58,1) at p = 0.5
        Assert.InRange(Ease.In.At(0.5), 0.3150, 0.3160);
        Assert.InRange(Ease.Out.At(0.5), 0.6840, 0.6850);
        Assert.Equal(0, Ease.Out.At(0));
        Assert.Equal(1, Ease.Out.At(1));
        double prev = 0;
        for (double p = 0; p <= 1; p += 0.01) { var y = Ease.Out.At(p); Assert.True(y >= prev - 1e-12); prev = y; }
    }
}
