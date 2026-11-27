using Avalonia;
using Notchyyyy.Motion;
using Notchyyyy.Notch;
using Xunit;

namespace Notchyyyy.Tests;

/// The Mac app's hover rules, on a stepped clock (ticks every 16 ms like the app's timer).
public class HoverTrackerTests
{
    private readonly NotchModel m = new();
    private readonly ManualClock clock = new();
    private readonly HoverTracker tr;
    private readonly List<bool> interactive = new();
    private static readonly Point Far = new(0, 400), Pill = new(0, 12);

    public HoverTrackerTests()
    {
        tr = new HoverTracker(m, clock) { HoverDelay = () => 0.12, SetInteractive = on => interactive.Add(on) };
    }

    void Run(Point? p, double seconds)
    {
        double end = clock.Now + seconds;
        while (clock.Now < end - 1e-9) { clock.Now += 0.016; tr.Tick(p); }
    }

    [Fact]
    public void OpensOnlyAfterTheHoverDelay()
    {
        Run(Far, 0.2);
        Run(Pill, 0.016);
        Assert.True(m.Hovering); Assert.False(m.Expanded);
        Run(Pill, 0.08);
        Assert.False(m.Expanded);
        Run(Pill, 0.06);
        Assert.True(m.Expanded); Assert.False(m.Hovering);
        Assert.Equal(new[] { true }, interactive);
    }

    [Fact]
    public void BrushingPastDoesNotOpen()
    {
        Run(Pill, 0.08);           // shorter than the delay
        Run(Far, 0.5);
        Assert.False(m.Expanded); Assert.False(m.Hovering);
    }

    [Fact]
    public void ClosesThreeHundredFiftyMsAfterLeaving()
    {
        Run(Pill, 0.3);
        Assert.True(m.Expanded);
        Run(new Point(420, 200), 0.32);
        Assert.True(m.Expanded);
        Run(new Point(420, 200), 0.06);
        Assert.False(m.Expanded);
        Assert.Equal(new[] { true, false }, interactive);
    }

    [Fact]
    public void ComingBackInsideCancelsTheClose()
    {
        Run(Pill, 0.3);
        Run(new Point(420, 200), 0.2);
        Run(new Point(0, 150), 0.6);
        Assert.True(m.Expanded);
    }

    [Theory]
    [InlineData(-365, 150, true)]   // 16 pt margin left of the body
    [InlineData(365, 315, true)]    // bottom-right margin
    [InlineData(-367, 150, false)]
    [InlineData(0, 330, true)]      // the tab pill's band
    [InlineData(281, 360, true)]
    [InlineData(283, 340, false)]
    [InlineData(0, 365, false)]     // below the pill band
    public void InsideTheOpenNotch(double x, double y, bool inside)
    {
        Run(Pill, 0.3);
        Run(new Point(x, y), 0.6);
        Assert.Equal(inside, m.Expanded);
    }

    [Fact]
    public void NoBounceBackAfterClosingUnderThePointer()
    {
        Run(Pill, 0.3);
        tr.Collapse();
        Run(Pill, 1.0);
        Assert.False(m.Expanded); Assert.False(m.Hovering);
        Run(Far, 0.05);
        Run(Pill, 0.2);
        Assert.True(m.Expanded);
    }

    [Fact]
    public void PinnedStaysOpen()
    {
        m.Pinned = true;
        tr.Expand();
        Run(Far, 2);
        Assert.True(m.Expanded);
    }

    [Theory]
    [InlineData(146, 12, true)]
    [InlineData(147, 12, false)]
    [InlineData(0, 38, true)]
    [InlineData(0, 38.5, false)]
    public void HotZoneIs56PtEitherSideAnd6PtBelow(double x, double y, bool hot)
    {
        Run(new Point(x, y), 0.032);
        Assert.Equal(hot, m.Hovering);
    }
}
