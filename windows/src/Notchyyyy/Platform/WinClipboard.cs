using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Threading;
using Notchyyyy.Clipboard;
using SkiaSharp;

namespace Notchyyyy.Platform;

/// <summary>
/// The Windows clipboard: notices each copy (the clipboard's sequence number, checked twice a second, as the Mac
/// app checks its pasteboard), reads the formats Notchyyyy keeps, and writes items back.
///
/// Never kept: anything a password manager or another app marks as private (Windows' own clipboard history honours
/// the same marks: ExcludeClipboardContentFromMonitorProcessing, CanIncludeInClipboardHistory = 0,
/// "Clipboard Viewer Ignore").
/// </summary>
public sealed class WinClipboard : IClipboardSource
{
    private readonly Func<IntPtr> _owner;
    private uint _last, _own;
    private DispatcherTimer? _timer;

    public WinClipboard(Func<IntPtr> owner) { _owner = owner; }

    public void Start(Action<ClipSnapshot> changed)
    {
        if (!OperatingSystem.IsWindows()) return;
        _last = GetClipboardSequenceNumber();
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(0.5), DispatcherPriority.Background, (_, _) =>
        {
            uint n = GetClipboardSequenceNumber();
            if (n == _last) return;
            _last = n;
            if (n == _own) return;   // our own copy back
            if (Read() is { } snap) changed(snap);
        });
        _timer.Start();
    }

    // ---- reading ----

    private const uint CF_UNICODETEXT = 13, CF_HDROP = 15, CF_DIB = 8, CF_DIBV5 = 17;
    private static readonly uint PngFormat = Reg("PNG"), SvgFormat = Reg("image/svg+xml"),
        Exclude = Reg("ExcludeClipboardContentFromMonitorProcessing"), History = Reg("CanIncludeInClipboardHistory"),
        ViewerIgnore = Reg("Clipboard Viewer Ignore");
    private static uint Reg(string name) => OperatingSystem.IsWindows() ? RegisterClipboardFormat(name) : 0;

    /// Reads what's on the clipboard now (null if it's busy for more than ~200 ms).
    public static ClipSnapshot? Read()
    {
        if (!Open(IntPtr.Zero)) return null;
        try
        {
            bool priv = IsClipboardFormatAvailable(Exclude) || IsClipboardFormatAvailable(ViewerIgnore)
                        || (IsClipboardFormatAvailable(History) && Bytes(History) is { Length: >= 4 } h && BitConverter.ToInt32(h, 0) == 0);
            if (priv) return new ClipSnapshot(Private: true);
            string? text = IsClipboardFormatAvailable(CF_UNICODETEXT) && Bytes(CF_UNICODETEXT) is { } t ? Encoding.Unicode.GetString(t).TrimEnd('\0') : null;
            string? svg = IsClipboardFormatAvailable(SvgFormat) && Bytes(SvgFormat) is { } s ? Encoding.UTF8.GetString(s).TrimEnd('\0') : null;
            byte[]? png = IsClipboardFormatAvailable(PngFormat) ? Bytes(PngFormat) : null;
            if (png is null && (IsClipboardFormatAvailable(CF_DIBV5) || IsClipboardFormatAvailable(CF_DIB)))
                png = DibToPng(Bytes(IsClipboardFormatAvailable(CF_DIBV5) ? CF_DIBV5 : CF_DIB));
            List<string>? files = null;
            if (IsClipboardFormatAvailable(CF_HDROP))
            {
                var hdrop = GetClipboardData(CF_HDROP);
                uint n = DragQueryFile(hdrop, 0xFFFFFFFF, null, 0);
                files = new List<string>();
                for (uint i = 0; i < n; i++)
                {
                    var sb = new StringBuilder(1024);
                    DragQueryFile(hdrop, i, sb, sb.Capacity);
                    files.Add(sb.ToString());
                }
            }
            return new ClipSnapshot(text, png, svg, files);
        }
        finally { CloseClipboard(); }
    }

    private static byte[]? Bytes(uint format)
    {
        var h = GetClipboardData(format);
        if (h == IntPtr.Zero) return null;
        var p = GlobalLock(h);
        if (p == IntPtr.Zero) return null;
        try
        {
            int size = (int)GlobalSize(h);
            var b = new byte[size];
            Marshal.Copy(p, b, 0, size);
            return b;
        }
        finally { GlobalUnlock(h); }
    }

    /// A device-independent bitmap (what screenshots and many apps put on the clipboard) as PNG.
    public static byte[]? DibToPng(byte[]? dib)
    {
        if (dib is null || dib.Length < 40) return null;
        int headerSize = BitConverter.ToInt32(dib, 0);
        int bitCount = BitConverter.ToInt16(dib, 14), compression = BitConverter.ToInt32(dib, 16);
        int colors = BitConverter.ToInt32(dib, 32);
        if (colors == 0 && bitCount <= 8) colors = 1 << bitCount;
        int masks = headerSize == 40 && compression == 3 ? 12 : 0;   // BI_BITFIELDS after a plain header
        int offset = 14 + headerSize + masks + colors * 4;
        // a BMP file is the DIB with a 14-byte file header in front
        var file = new byte[14 + dib.Length];
        file[0] = (byte)'B'; file[1] = (byte)'M';
        BitConverter.GetBytes(file.Length).CopyTo(file, 2);
        BitConverter.GetBytes(offset).CopyTo(file, 10);
        dib.CopyTo(file, 14);
        // a plain 32-bit bitmap's fourth byte is padding, often 0: make it opaque, or the decoder may read it as alpha
        if (bitCount == 32 && compression == 0)
            for (int i = offset + 3; i < file.Length; i += 4) file[i] = 255;
        return Clips.ToPng(file);
    }

    // ---- writing ----

    public void WriteText(string text, string? svg = null)
    {
        if (!OperatingSystem.IsWindows()) return;
        Write(() =>
        {
            Set(CF_UNICODETEXT, Encoding.Unicode.GetBytes(text + "\0"));
            if (svg is not null) Set(SvgFormat, Encoding.UTF8.GetBytes(svg));
        });
    }

    public void WritePng(byte[] png)
    {
        if (!OperatingSystem.IsWindows()) return;
        Write(() =>
        {
            Set(PngFormat, png);                    // browsers, Office, Figma read PNG (keeps transparency)
            if (PngToDib(png) is { } dib) Set(CF_DIB, dib);   // everything else reads a bitmap
        });
    }

    /// Writes text marked the way password managers mark theirs (for the self-test: it must not be kept).
    public void WritePrivateText(string text)
    {
        if (!OperatingSystem.IsWindows()) return;
        Write(() => { Set(CF_UNICODETEXT, Encoding.Unicode.GetBytes(text + "\0")); Set(Exclude, new byte[4]); });
    }

    private void Write(Action fill)
    {
        if (!Open(_owner())) return;
        try { EmptyClipboard(); fill(); }
        finally { CloseClipboard(); }
        _own = GetClipboardSequenceNumber();
        _last = _own;
    }

    private static void Set(uint format, byte[] data)
    {
        var h = GlobalAlloc(0x0002 /* GMEM_MOVEABLE */, (UIntPtr)data.Length);
        var p = GlobalLock(h);
        Marshal.Copy(data, 0, p, data.Length);
        GlobalUnlock(h);
        if (SetClipboardData(format, h) == IntPtr.Zero) GlobalFree(h);   // on success the clipboard owns it
    }

    /// PNG → a 32-bit bottom-up DIB (BITMAPINFOHEADER + pixels).
    public static byte[]? PngToDib(byte[] png)
    {
        using var bmp = SKBitmap.Decode(png);
        if (bmp is null) return null;
        using var bgra = bmp.Copy(SKColorType.Bgra8888);
        int w = bgra.Width, h = bgra.Height;
        var dib = new byte[40 + w * h * 4];
        BitConverter.GetBytes(40).CopyTo(dib, 0);
        BitConverter.GetBytes(w).CopyTo(dib, 4);
        BitConverter.GetBytes(h).CopyTo(dib, 8);      // positive: bottom-up
        BitConverter.GetBytes((short)1).CopyTo(dib, 12);
        BitConverter.GetBytes((short)32).CopyTo(dib, 14);
        BitConverter.GetBytes(w * h * 4).CopyTo(dib, 20);
        var px = bgra.Bytes;
        for (int y = 0; y < h; y++)
        {
            // un-premultiply isn't needed for opaque pixels; transparent ones keep their colour in the PNG format
            Buffer.BlockCopy(px, (h - 1 - y) * w * 4, dib, 40 + y * w * 4, w * 4);
        }
        return dib;
    }

    private static bool Open(IntPtr owner)
    {
        for (int i = 0; i < 10; i++)
        {
            if (OpenClipboard(owner)) return true;
            Thread.Sleep(20);   // another app has it open for a moment
        }
        return false;
    }

    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint format);
    [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint format, IntPtr mem);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormat(string name);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr mem);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr mem);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr mem);
    [DllImport("kernel32.dll")] private static extern UIntPtr GlobalSize(IntPtr mem);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint DragQueryFile(IntPtr drop, uint index, StringBuilder? file, int size);
}
