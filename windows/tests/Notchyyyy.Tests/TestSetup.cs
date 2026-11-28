using System.Runtime.CompilerServices;

namespace Notchyyyy.Tests;

static class TestSetup
{
    /// Before anything else: the app's own settings are never written by tests (--sample makes them read-only).
    [ModuleInitializer]
    internal static void Init() => Theme.Debug.Args = new[] { "--sample" };
}
