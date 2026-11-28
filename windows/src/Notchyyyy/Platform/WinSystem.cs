using System.Runtime.InteropServices;
using Notchyyyy.SystemInfo;

namespace Notchyyyy.Platform;

/// <summary>
/// Windows' own numbers, as Task Manager shows them, with no administrator rights:
/// CPU per core from the kernel's processor times (NtQuerySystemInformation), GPU from the "GPU Engine" counters
/// (the 3D engines' utilisation, summed per graphics chip), memory from GlobalMemoryStatusEx, storage from the
/// system drive.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class WinSystem : ISystemSource, IDisposable
{
    private long[]? _lastIdle, _lastTotal;
    private IntPtr _query, _gpuCounter;
    private bool _gpuReady;

    public WinSystem()
    {
        Chip = Registry(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString") ?? "This PC";
        Chip = System.Text.RegularExpressions.Regex.Replace(Chip, @"\s+", " ").Replace("(R)", "").Replace("(TM)", "").Replace(" CPU", "").Trim();
        int at = Chip.IndexOf(" @ ", StringComparison.Ordinal);
        if (at > 0) Chip = Chip[..at];
        GpuName = GraphicsName() ?? "";
        try
        {
            if (PdhOpenQuery(null, IntPtr.Zero, out _query) == 0
                && PdhAddEnglishCounter(_query, @"\GPU Engine(*engtype_3D)\Utilization Percentage", IntPtr.Zero, out _gpuCounter) == 0)
            {
                PdhCollectQueryData(_query);   // rates need two samples: the first is taken now
                _gpuReady = true;
            }
        }
        catch { _gpuReady = false; }
    }

    public string Chip { get; }
    public string GpuName { get; }

    public SystemSample Read()
    {
        var (cpu, cores) = ReadCpu();
        double? gpu = ReadGpu();
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        GlobalMemoryStatusEx(ref m);
        double memTotal = m.ullTotalPhys, memUsed = m.ullTotalPhys - m.ullAvailPhys;
        double diskUsed = 0, diskTotal = 1;
        try
        {
            var root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? @"C:\";
            var d = new DriveInfo(root);
            diskTotal = d.TotalSize; diskUsed = d.TotalSize - d.AvailableFreeSpace;
        }
        catch { }
        return new SystemSample(cpu, cores, gpu, memUsed, memTotal, diskUsed, diskTotal);
    }

    /// Busy share per core since the last reading: (kernel + user − idle) / (kernel + user).
    private (double, double[]) ReadCpu()
    {
        int n = Environment.ProcessorCount;
        int size = Marshal.SizeOf<SPPI>();
        var buf = Marshal.AllocHGlobal(size * n);
        try
        {
            if (NtQuerySystemInformation(8 /* SystemProcessorPerformanceInformation */, buf, size * n, out int got) != 0) return (0, new double[n]);
            int count = got / size;
            var idle = new long[count]; var total = new long[count];
            for (int i = 0; i < count; i++)
            {
                var p = Marshal.PtrToStructure<SPPI>(buf + i * size);
                idle[i] = p.IdleTime; total[i] = p.KernelTime + p.UserTime;   // kernel time includes idle
            }
            var cores = new double[count];
            double busyAll = 0, allTotal = 0;
            if (_lastIdle is { } li && _lastTotal is { } lt && li.Length == count)
                for (int i = 0; i < count; i++)
                {
                    double dt = total[i] - lt[i], di = idle[i] - li[i];
                    cores[i] = dt > 0 ? Math.Clamp((dt - di) / dt, 0, 1) : 0;
                    busyAll += dt - di; allTotal += dt;
                }
            _lastIdle = idle; _lastTotal = total;
            return (allTotal > 0 ? Math.Clamp(busyAll / allTotal, 0, 1) : 0, cores);
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    /// The busiest graphics chip's 3D engines, summed (as Task Manager's GPU graph does); null without counters.
    private double? ReadGpu()
    {
        if (!_gpuReady) return null;
        if (PdhCollectQueryData(_query) != 0) return null;
        uint bytes = 0, items = 0;
        int r = PdhGetFormattedCounterArray(_gpuCounter, 0x200 /* PDH_FMT_DOUBLE */, ref bytes, ref items, IntPtr.Zero);
        if (r != unchecked((int)0x800007D2) /* PDH_MORE_DATA */ || bytes == 0) return items == 0 ? 0 : null;
        var buf = Marshal.AllocHGlobal((int)bytes);
        try
        {
            if (PdhGetFormattedCounterArray(_gpuCounter, 0x200, ref bytes, ref items, buf) != 0) return null;
            var perChip = new Dictionary<string, double>();
            int stride = Marshal.SizeOf<PDH_FMT_COUNTERVALUE_ITEM_DOUBLE>();
            for (int i = 0; i < items; i++)
            {
                var item = Marshal.PtrToStructure<PDH_FMT_COUNTERVALUE_ITEM_DOUBLE>(buf + i * stride);
                string name = Marshal.PtrToStringUni(item.szName) ?? "";
                // "pid_1234_luid_0x00000000_0x0000D1A2_phys_0_eng_0_engtype_3D": the chip is the luid
                int l = name.IndexOf("luid_", StringComparison.Ordinal);
                string chip = l >= 0 ? name[l..Math.Min(name.Length, l + 27)] : "";
                if (item.FmtValue.CStatus == 0) perChip[chip] = perChip.GetValueOrDefault(chip) + item.FmtValue.doubleValue;
            }
            return perChip.Count == 0 ? 0 : Math.Clamp(perChip.Values.Max() / 100, 0, 1);
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private static string? GraphicsName()
    {
        // the display adapters' class key: the first real adapter's driver description
        const string cls = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
        try
        {
            using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(cls);
            if (k is null) return null;
            var names = k.GetSubKeyNames().Where(n => n.Length == 4 && n.All(char.IsDigit)).OrderBy(n => n)
                .Select(n => Registry(cls + "\\" + n, "DriverDesc")).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
            return names.FirstOrDefault(n => !n!.Contains("Basic Display", StringComparison.OrdinalIgnoreCase) && !n.Contains("Remote", StringComparison.OrdinalIgnoreCase))
                ?? names.FirstOrDefault();
        }
        catch { return null; }
    }

    private static string? Registry(string key, string value)
    {
        try { using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(key); return k?.GetValue(value) as string; }
        catch { return null; }
    }

    public void Dispose() { if (_query != IntPtr.Zero) PdhCloseQuery(_query); _query = IntPtr.Zero; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SPPI { public long IdleTime, KernelTime, UserTime, DpcTime, InterruptTime; public uint InterruptCount; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PDH_FMT_COUNTERVALUE { public uint CStatus; public double doubleValue; }
    [StructLayout(LayoutKind.Sequential)]
    private struct PDH_FMT_COUNTERVALUE_ITEM_DOUBLE { public IntPtr szName; public PDH_FMT_COUNTERVALUE FmtValue; }

    [DllImport("ntdll.dll")] private static extern int NtQuerySystemInformation(int cls, IntPtr info, int length, out int returned);
    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern int PdhOpenQuery(string? source, IntPtr user, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern int PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern int PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern int PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint bytes, ref uint count, IntPtr items);
    [DllImport("pdh.dll")] private static extern int PdhCloseQuery(IntPtr query);
}
