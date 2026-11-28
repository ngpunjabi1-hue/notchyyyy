using Notchyyyy.Theme;
using Xunit;

namespace Notchyyyy.Tests;

/// Settings survive restarts and updates: saved the moment they change, loaded exactly, never corrupted.
public class PrefsTests
{
    static string TempFile() { var d = Path.Combine(Path.GetTempPath(), "notchyyyy-prefs-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d); return Path.Combine(d, "settings.json"); }

    [Fact]
    public void EveryChangeIsSavedAndLoadsBackExactly()
    {
        var path = TempFile();
        var p = Prefs.Load(path);
        p.Accent = Accent.Orange; p.Appearance = Appearance.Light; p.TextScale = 1.1; p.IconScale = 0.9;
        p.HoverSpeed = HoverSpeed.Relaxed; p.LiveActivity = false; p.Buddy = false; p.UserName = "Alex Morgan";
        p.WaterReminders = false; p.WaterInterval = WaterInterval.M45; p.WaterGoal = 11; p.AutoUpdate = false;
        p.SetTabHidden("music", true); p.SetTabHidden("system", true);
        var q = Prefs.Load(path);
        Assert.Equal(Accent.Orange, q.Accent); Assert.Equal(Appearance.Light, q.Appearance);
        Assert.Equal(1.1, q.TextScale, 6); Assert.Equal(0.9, q.IconScale, 6);
        Assert.Equal(HoverSpeed.Relaxed, q.HoverSpeed); Assert.False(q.LiveActivity); Assert.False(q.Buddy);
        Assert.Equal("Alex Morgan", q.UserName); Assert.False(q.WaterReminders);
        Assert.Equal(WaterInterval.M45, q.WaterInterval); Assert.Equal(11, q.WaterGoal); Assert.False(q.AutoUpdate);
        Assert.Equal(new[] { "music", "system" }, q.HiddenTabs);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void LoadingNeverWrites()
    {
        var path = TempFile();
        var p = Prefs.Load(path); p.Accent = Accent.Teal;
        var before = File.GetLastWriteTimeUtc(path);
        Thread.Sleep(30);
        var q = Prefs.Load(path);
        Assert.Equal(before, File.GetLastWriteTimeUtc(path));
        Assert.Equal(Accent.Teal, q.Accent);
    }

    [Fact]
    public void ADamagedFileStartsWithDefaultsInsteadOfCrashing()
    {
        var path = TempFile();
        File.WriteAllText(path, "{ \"Accent\": \"Pink\", \"TextScale\": ");     // cut off half-way
        var p = Prefs.Load(path);
        Assert.Equal(Accent.Indigo, p.Accent);
        Assert.True(p.Buddy);
    }

    [Fact]
    public void ChangesAreAnnouncedOnceAndValuesAreKeptInRange()
    {
        var p = Prefs.Load(TempFile());
        var seen = new List<string>();
        p.Changed += seen.Add;
        p.Accent = Accent.Pink; p.Accent = Accent.Pink;          // the same value twice: one announcement
        p.TextScale = 5;                                          // out of range: clamped
        p.WaterGoal = 0;
        Assert.Equal(new[] { "Accent", "TextScale", "WaterGoal" }, seen);
        Assert.Equal(1.2, p.TextScale, 6);
        Assert.Equal(1, p.WaterGoal);
    }

    [Fact]
    public void ResetKeepsTheName()
    {
        var p = Prefs.Load(TempFile());
        p.UserName = "Alex"; p.Accent = Accent.Green; p.Buddy = false;
        p.Reset();
        Assert.Equal(Accent.Indigo, p.Accent); Assert.True(p.Buddy); Assert.Equal("Alex", p.UserName);
    }
}
