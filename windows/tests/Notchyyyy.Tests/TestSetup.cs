using System.Runtime.CompilerServices;

namespace Notchyyyy.Tests;

static class TestSetup
{
    /// Before anything else: the app's own settings are never written by tests (--sample makes them read-only).
    [ModuleInitializer]
    internal static void Init()
    {
        Theme.Debug.Args = new[] { "--sample" };
        // a fixed time of day, so the header clock can't tick over between two frames a test compares
        Theme.Wall.Now = () => new DateTime(2026, 11, 28, 13, 24, 0);
    }
}
