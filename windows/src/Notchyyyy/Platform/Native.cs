using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace Notchyyyy.Platform;

/// <summary>
/// Where the pointer is, anywhere on screen. The notch has to know even when the pointer is over
/// another app (that's how hovering the pill opens it), so this reads the OS cursor directly.
/// </summary>
public interface IPointerSource
{
    /// The pointer in DIPs relative to the top centre of <paramref name="stage"/>, y down; null if unknown.
    Point? Read(Visual stage);
}

public static class PointerSources
{
    public static IPointerSource ForThisOS() =>
        OperatingSystem.IsWindows() ? new WinPointer() :
        OperatingSystem.IsMacOS() ? new MacPointer() : new NoPointer();
}

internal sealed class NoPointer : IPointerSource { public Point? Read(Visual stage) => null; }

/// Windows: GetCursorPos gives physical pixels in the same space as Avalonia's PointToScreen.
internal sealed class WinPointer : IPointerSource
{
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);

    public Point? Read(Visual stage)
    {
        if (!GetCursorPos(out var c)) return null;
        var origin = stage.PointToScreen(new Point(stage.Bounds.Width / 2, 0));
        double s = TopLevel.GetTopLevel(stage)?.RenderScaling ?? 1;
        return new Point((c.X - origin.X) / s, (c.Y - origin.Y) / s);
    }
}

public static class MacPointerProbe { public static Point? Global() => MacPointer.Global(); }

/// macOS (for development): CGEventGetLocation gives global points with a top-left origin.
/// Avalonia's PointToScreen on macOS also works in points, so no scaling is applied.
internal sealed class MacPointer : IPointerSource
{
    [StructLayout(LayoutKind.Sequential)] private struct CGPoint { public double X, Y; }
    private const string CG = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    [DllImport(CG)] private static extern IntPtr CGEventCreate(IntPtr source);
    [DllImport(CG)] private static extern CGPoint CGEventGetLocation(IntPtr e);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")] private static extern void CFRelease(IntPtr p);

    public static Point? Global()
    {
        var e = CGEventCreate(IntPtr.Zero);
        if (e == IntPtr.Zero) return null;
        var p = CGEventGetLocation(e);
        CFRelease(e);
        return new Point(p.X, p.Y);
    }

    public Point? Read(Visual stage)
    {
        if (Global() is not { } g) return null;
        var origin = stage.PointToScreen(new Point(stage.Bounds.Width / 2, 0));
        return new Point(g.X - origin.X, g.Y - origin.Y);
    }
}

/// <summary>
/// The Windows overlay: off the taskbar and Alt+Tab, always on top, and click-through while closed
/// so the transparent window never blocks what's under it. Does nothing on other systems.
/// </summary>
public static class Overlay
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_APPWINDOW = 0x40000;
    private const uint LWA_ALPHA = 0x2;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr h);

    private static IntPtr _previous;

    /// Remembers the app the user was in when the notch opened (it opens on hover, without taking the keyboard).
    public static void RememberForeground(Window w)
    {
        if (!OperatingSystem.IsWindows()) return;
        var fg = GetForegroundWindow();
        if (fg != IntPtr.Zero && fg != Hwnd(w)) _previous = fg;
    }

    /// After a click inside, the notch has the keyboard; once it has closed, hand it back to that app,
    /// so typing never goes to the invisible overlay.
    public static void ReturnForeground(Window w)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (GetForegroundWindow() == Hwnd(w) && _previous != IntPtr.Zero && IsWindow(_previous)) SetForegroundWindow(_previous);
    }

    private static IntPtr Hwnd(Window w) => w.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;

    public static void Prepare(Window w)
    {
        if (!OperatingSystem.IsWindows()) return;
        var h = Hwnd(w);
        if (h == IntPtr.Zero) return;
        long ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
        ex = (ex | WS_EX_TOOLWINDOW | WS_EX_LAYERED) & ~WS_EX_APPWINDOW;
        SetWindowLongPtr(h, GWL_EXSTYLE, new IntPtr(ex));
        SetLayeredWindowAttributes(h, 0, 255, LWA_ALPHA);
    }

    public static void SetClickThrough(Window w, bool through)
    {
        if (!OperatingSystem.IsWindows()) return;
        var h = Hwnd(w);
        if (h == IntPtr.Zero) return;
        long ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
        long next = through ? ex | WS_EX_TRANSPARENT : ex & ~WS_EX_TRANSPARENT;
        if (next != ex) SetWindowLongPtr(h, GWL_EXSTYLE, new IntPtr(next));
    }
}
