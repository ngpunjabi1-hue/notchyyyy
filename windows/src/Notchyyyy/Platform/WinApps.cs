using System.Diagnostics;
using System.Runtime.InteropServices;
using Notchyyyy.Apps;
using SkiaSharp;

namespace Notchyyyy.Platform;

/// <summary>
/// Apps as the Start menu lists them: the shell's Apps folder (shell:AppsFolder), which holds both desktop programs
/// and Store apps, each with the id that opens it. Icons come from the shell at the exact size asked for.
/// </summary>
public sealed class WinApps : IAppCatalog
{
    private static readonly string[] Skip = { "uninstall", "readme", "read me", "release notes", "documentation", "license" };
    private static readonly string[] SkipExt = { ".url", ".chm", ".txt", ".pdf", ".html", ".htm", ".rtf", ".ini", ".log", ".msc" };

    public List<AppEntry> Scan()
    {
        var apps = new List<AppEntry>();
        if (!OperatingSystem.IsWindows()) return apps;
        var t = Type.GetTypeFromProgID("Shell.Application");
        if (t is null) return apps;
        dynamic shell = Activator.CreateInstance(t)!;
        try
        {
            dynamic folder = shell.NameSpace("shell:AppsFolder");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (dynamic item in folder.Items())
            {
                string name = item.Name, id = item.Path;
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id)) continue;
                var lower = name.ToLowerInvariant();
                if (Skip.Any(lower.Contains)) continue;
                if (SkipExt.Any(e => id.EndsWith(e, StringComparison.OrdinalIgnoreCase))) continue;
                if (id.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
                if (!seen.Add(name)) continue;
                apps.Add(new AppEntry(id, name));
            }
        }
        finally { Marshal.FinalReleaseComObject(shell); }
        apps.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return apps;
    }

    public void Launch(string id)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{id}") { UseShellExecute = false }); }
        catch (Exception e) { Theme.Debug.Log($"couldn't open {id}: {e.Message}"); }
    }

    // ---- icons: IShellItemImageFactory on the app's shell item ----

    public SKImage? Icon(string id, int px)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var iid = typeof(IShellItemImageFactory).GUID;
        if (SHCreateItemFromParsingName($"shell:AppsFolder\\{id}", IntPtr.Zero, ref iid, out var factory) != 0 || factory is null) return null;
        try
        {
            // icons only (no thumbnails of documents), scaled up if the app has no big one
            if (factory.GetImage(new SIZE { cx = px, cy = px }, 0x4 /* SIIGBF_ICONONLY */, out var hbm) != 0 || hbm == IntPtr.Zero) return null;
            try { return FromHBitmap(hbm); }
            finally { DeleteObject(hbm); }
        }
        finally { Marshal.ReleaseComObject(factory); }
    }

    /// A 32-bit shell bitmap (premultiplied BGRA) as an image.
    private static SKImage? FromHBitmap(IntPtr hbm)
    {
        var bm = new BITMAP();
        if (GetObject(hbm, Marshal.SizeOf<BITMAP>(), ref bm) == 0 || bm.bmWidth <= 0 || bm.bmHeight == 0) return null;
        int w = bm.bmWidth, h = Math.Abs(bm.bmHeight);
        var info = new BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };   // top-down
        var px = new byte[w * h * 4];
        var dc = GetDC(IntPtr.Zero);
        try { if (GetDIBits(dc, hbm, 0, (uint)h, px, ref info, 0) == 0) return null; }
        finally { ReleaseDC(IntPtr.Zero, dc); }
        // some icons come without alpha (every pixel 0): treat those as opaque
        bool anyAlpha = false;
        for (int i = 3; i < px.Length; i += 4) if (px[i] != 0) { anyAlpha = true; break; }
        if (!anyAlpha) for (int i = 3; i < px.Length; i += 4) px[i] = 255;
        var infoSk = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        var handle = GCHandle.Alloc(px, GCHandleType.Pinned);
        try
        {
            using var pixmap = new SKPixmap(infoSk, handle.AddrOfPinnedObject(), w * 4);
            return SKImage.FromPixelCopy(pixmap);
        }
        finally { handle.Free(); }
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory { [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm); }

    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)] private struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public short bmPlanes, bmBitsPixel; public IntPtr bmBits; }
    [StructLayout(LayoutKind.Sequential)] private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight; public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindCtx, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? item);
    [DllImport("gdi32.dll")] private static extern int GetObject(IntPtr h, int size, ref BITMAP bm);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bmp, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER info, uint usage);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
}
