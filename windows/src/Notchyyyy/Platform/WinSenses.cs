using System.Runtime.InteropServices;
using Notchyyyy.Sprout;

namespace Notchyyyy.Platform;

/// <summary>
/// Sprout's senses on Windows, with no keyboard hook and no permission: idle time from GetLastInputInfo (when the
/// last keyboard, mouse or touch input happened), and key presses counted from raw keyboard input sent to a small
/// message-only window of Notchyyyy's own (RIDEV_INPUTSINK: it hears key presses while other apps have the focus).
/// Only a count is kept. Which key it was is looked at for one thing alone, to tell a held-down key repeating from
/// a new press, and is never stored or logged.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class WinSenses : ISenseSource
{
    private int _downs;
    // the key last pressed and not yet released (virtual key and scan code together; −1 = none), only to tell a
    // held key's repeats from new presses. Injected presses (the self-test's) carry no scan code, so the scan code
    // alone, starting at 0, made them all look like repeats (seen on the Windows runner).
    private int _held = -1;
    private readonly IntPtr _hwnd;
    private readonly WndProc _proc;   // kept alive: Windows calls it

    public WinSenses()
    {
        _proc = Proc;
        var wc = new WNDCLASSEX { cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(), lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc), hInstance = GetModuleHandle(null), lpszClassName = "NotchyyyySenses" };
        RegisterClassEx(ref wc);
        _hwnd = CreateWindowEx(0, "NotchyyyySenses", "", 0, 0, 0, 0, 0, new IntPtr(-3) /* HWND_MESSAGE */, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
        var dev = new RAWINPUTDEVICE { usUsagePage = 0x01, usUsage = 0x06 /* keyboard */, dwFlags = 0x100 /* RIDEV_INPUTSINK */, hwndTarget = _hwnd };
        Available = _hwnd != IntPtr.Zero && RegisterRawInputDevices(new[] { dev }, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
    }

    /// The raw keyboard input was registered (the self-test checks it).
    public bool Available { get; }

    public double SecondsSinceInput
    {
        get
        {
            var li = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            if (!GetLastInputInfo(ref li)) return 0;
            return unchecked((uint)Environment.TickCount - li.dwTime) / 1000.0;
        }
    }

    public int TakeKeyDowns() => Interlocked.Exchange(ref _downs, 0);

    private IntPtr Proc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == 0x00FF /* WM_INPUT */)
        {
            uint size = 0, header = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
            GetRawInputData(lParam, 0x10000003 /* RID_INPUT */, IntPtr.Zero, ref size, header);
            if (size > 0)
            {
                var buf = Marshal.AllocHGlobal((int)size);
                try
                {
                    if (GetRawInputData(lParam, 0x10000003, buf, ref size, header) == size)
                    {
                        var kb = Marshal.PtrToStructure<RAWKEYBOARD>(buf + (int)header);
                        bool up = (kb.Flags & 1) != 0;   // RI_KEY_BREAK
                        int key = kb.VKey << 16 | kb.MakeCode;
                        if (up) { if (key == _held) _held = -1; }
                        else if (key != _held) { _held = key; Interlocked.Increment(ref _downs); }
                    }
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
        }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize, style; public IntPtr lpfnWndProc; public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string? lpszMenuName; public string lpszClassName; public IntPtr hIconSm;
    }
    [StructLayout(LayoutKind.Sequential)] private struct RAWINPUTDEVICE { public ushort usUsagePage, usUsage; public uint dwFlags; public IntPtr hwndTarget; }
    [StructLayout(LayoutKind.Sequential)] private struct RAWINPUTHEADER { public uint dwType, dwSize; public IntPtr hDevice, wParam; }
    [StructLayout(LayoutKind.Sequential)] private struct RAWKEYBOARD { public ushort MakeCode, Flags, Reserved, VKey; public uint Message, ExtraInformation; }
    [StructLayout(LayoutKind.Sequential)] private struct LASTINPUTINFO { public uint cbSize, dwTime; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassEx(ref WNDCLASSEX wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowEx(uint ex, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);
    [DllImport("user32.dll")] private static extern uint GetRawInputData(IntPtr raw, uint cmd, IntPtr data, ref uint size, uint header);
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LASTINPUTINFO li);
}
