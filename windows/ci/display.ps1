# CI only: sets the primary display's resolution and scaling on a Windows runner, live (no sign-out),
# the way Settings > Display does. Usage: display.ps1 -Width 1920 -Height 1080 -Scale 150
param([int]$Width = 1920, [int]$Height = 1080, [int]$Scale = 100)

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class Display
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettings(string dev, int mode, ref DEVMODE dm);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int ChangeDisplaySettings(ref DEVMODE dm, int flags);

    [StructLayout(LayoutKind.Sequential)] public struct LUID { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] public struct SOURCE { public LUID adapterId; public uint id, modeInfoIdx, statusFlags; }
    [StructLayout(LayoutKind.Sequential)] public struct TARGET
    {
        public LUID adapterId; public uint id, modeInfoIdx; public int outputTechnology, rotation, scaling;
        public uint refreshNum, refreshDen; public int scanLineOrdering, targetAvailable; public uint statusFlags;
    }
    [StructLayout(LayoutKind.Sequential)] public struct PATH { public SOURCE source; public TARGET target; public uint flags; }
    [StructLayout(LayoutKind.Sequential, Size = 64)] public struct MODE { public uint infoType, id; public LUID adapterId; }
    [StructLayout(LayoutKind.Sequential)] public struct HEADER { public int type; public uint size; public LUID adapterId; public uint id; }
    [StructLayout(LayoutKind.Sequential)] public struct DPI_GET { public HEADER header; public int minRel, curRel, maxRel; }
    [StructLayout(LayoutKind.Sequential)] public struct DPI_SET { public HEADER header; public int scaleRel; }

    [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint np, [Out] PATH[] paths, ref uint nm, [Out] MODE[] modes, IntPtr topology);
    [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref DPI_GET p);
    [DllImport("user32.dll")] static extern int DisplayConfigSetDeviceInfo(ref DPI_SET p);

    static readonly int[] Steps = { 100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500 };

    public static string SetResolution(int w, int h)
    {
        var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };
        if (!EnumDisplaySettings(null, -1, ref dm)) return "could not read the current mode";
        if (dm.dmPelsWidth == w && dm.dmPelsHeight == h) return "already " + w + "x" + h;
        dm.dmPelsWidth = w; dm.dmPelsHeight = h; dm.dmFields = 0x80000 | 0x100000;
        int r = ChangeDisplaySettings(ref dm, 0);
        return r == 0 ? "resolution " + w + "x" + h : "ChangeDisplaySettings failed: " + r;
    }

    public static string SetScale(int percent)
    {
        uint np, nm;
        if (GetDisplayConfigBufferSizes(2, out np, out nm) != 0) return "buffer sizes failed";
        var paths = new PATH[np]; var modes = new MODE[nm];
        if (QueryDisplayConfig(2, ref np, paths, ref nm, modes, IntPtr.Zero) != 0) return "QueryDisplayConfig failed";
        var src = paths[0].source;
        var get = new DPI_GET { header = new HEADER { type = -3, size = (uint)Marshal.SizeOf(typeof(DPI_GET)), adapterId = src.adapterId, id = src.id } };
        if (DisplayConfigGetDeviceInfo(ref get) != 0) return "could not read the scaling";
        int rec = Math.Abs(get.minRel);                       // index of the recommended scale
        int want = Array.IndexOf(Steps, percent);
        if (want < 0) return "unsupported scale " + percent;
        int rel = Math.Max(get.minRel, Math.Min(get.maxRel, want - rec));
        var set = new DPI_SET { header = new HEADER { type = -4, size = (uint)Marshal.SizeOf(typeof(DPI_SET)), adapterId = src.adapterId, id = src.id }, scaleRel = rel };
        int r = DisplayConfigSetDeviceInfo(ref set);
        return r == 0 ? "scale " + Steps[rec + rel] + "% (recommended " + Steps[rec] + "%, allowed " + Steps[rec + get.minRel] + "-" + Steps[rec + get.maxRel] + "%)"
                      : "DisplayConfigSetDeviceInfo failed: " + r;
    }
}
'@

Write-Host ([Display]::SetResolution($Width, $Height))
Start-Sleep -Seconds 2
Write-Host ([Display]::SetScale($Scale))
Start-Sleep -Seconds 3
