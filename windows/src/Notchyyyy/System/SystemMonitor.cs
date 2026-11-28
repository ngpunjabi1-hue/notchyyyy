using System.Globalization;
using Avalonia.Threading;

namespace Notchyyyy.SystemInfo;

/// <summary>One reading: every share is 0…1; sizes in bytes. Null GPU = no GPU counters on this machine.</summary>
public sealed record SystemSample(double Cpu, double[] Cores, double? Gpu, double MemUsed, double MemTotal, double DiskUsed, double DiskTotal);

/// <summary>Where readings come from: Windows' own counters, or a calm stand-in on the Mac preview.</summary>
public interface ISystemSource
{
    /// "Intel Core i7-1360P", read once.
    string Chip { get; }
    /// "Intel Iris Xe Graphics" (empty if unknown).
    string GpuName { get; }
    /// Called off the UI thread, every 1.5 s.
    SystemSample Read();
}

/// <summary>
/// CPU, GPU, memory and storage, read every 1.5 s on a background thread (the Mac's SystemMonitor), with the last
/// 40 readings of each for the sparklines. Storage is read every 30 s (it barely moves).
/// </summary>
public sealed class SystemMonitor
{
    public const int History = 40;
    private ISystemSource? _source;
    private Timer? _timer;
    private int _busy;
    private bool _primed;
    private DateTime _diskAt = DateTime.MinValue;

    public double Cpu { get; private set; }
    public double[] Cores { get; private set; } = Array.Empty<double>();
    public double? Gpu { get; private set; }
    public double MemUsed { get; private set; }
    public double MemTotal { get; private set; } = 1;
    public double DiskUsed { get; private set; }
    public double DiskTotal { get; private set; } = 1;
    public double Mem => MemTotal > 0 ? MemUsed / MemTotal : 0;
    public double Disk => DiskTotal > 0 ? DiskUsed / DiskTotal : 0;
    public double[] CpuHistory { get; private set; } = new double[History];
    public double[] GpuHistory { get; private set; } = new double[History];
    public double[] MemHistory { get; private set; } = new double[History];
    public string Chip => _source?.Chip ?? "";
    public string GpuName => _source?.GpuName ?? "";
    /// Readings taken so far (tests).
    public int Samples { get; private set; }

    /// After each reading (on the UI thread).
    public event Action? Changed;

    /// The source to name the chip and GPU (tests feed readings through <see cref="Apply"/> themselves).
    public void Use(ISystemSource source) => _source = source;

    public void Start(ISystemSource source, double interval = 1.5)
    {
        _source = source;
        _timer = new Timer(_ => Tick(), null, TimeSpan.Zero, TimeSpan.FromSeconds(interval));
    }

    private void Tick()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1 || _source is not { } src) return;
        try
        {
            var s = src.Read();
            Dispatcher.UIThread.Post(() => Apply(s));
        }
        catch (Exception e) { Theme.Debug.Log($"system reading failed: {e.Message}"); }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }

    /// Takes a reading (the timer's, or a test's).
    public void Apply(SystemSample s)
    {
        Cpu = Math.Clamp(s.Cpu, 0, 1); Cores = s.Cores; Gpu = s.Gpu is { } g ? Math.Clamp(g, 0, 1) : null;
        MemUsed = s.MemUsed; MemTotal = s.MemTotal;
        if (DateTime.UtcNow - _diskAt > TimeSpan.FromSeconds(30) || DiskTotal <= 1) { DiskUsed = s.DiskUsed; DiskTotal = s.DiskTotal; _diskAt = DateTime.UtcNow; }
        // the first real reading fills the history, so the graphs start level instead of rising from zero
        if (!_primed && s.Cores.Length > 0)
        {
            _primed = true;
            Array.Fill(CpuHistory = new double[History], Cpu);
            Array.Fill(GpuHistory = new double[History], Gpu ?? 0);
            Array.Fill(MemHistory = new double[History], Mem);
        }
        else if (_primed)
        {
            CpuHistory = Push(CpuHistory, Cpu);
            GpuHistory = Push(GpuHistory, Gpu ?? 0);
            MemHistory = Push(MemHistory, Mem);
        }
        Samples++;
        Changed?.Invoke();
    }

    private static double[] Push(double[] a, double v) { var b = new double[a.Length]; Array.Copy(a, 1, b, 0, a.Length - 1); b[^1] = v; return b; }

    /// "8.1 GB", "512 GB", "1.8 TB" (the Mac's bytes()).
    public static string Bytes(double b)
    {
        double gb = b / 1_073_741_824;
        if (gb >= 1000) return (gb / 1024).ToString("0.0", CultureInfo.InvariantCulture) + " TB";
        if (gb >= 100) return gb.ToString("0", CultureInfo.InvariantCulture) + " GB";
        return gb.ToString("0.0", CultureInfo.InvariantCulture) + " GB";
    }
}

/// <summary>
/// The Mac preview's stand-in (and `--sample`'s): a calm, slowly wandering machine, so the tab can be seen moving.
/// </summary>
public sealed class SampleSystem : ISystemSource
{
    private readonly Random _r;
    private readonly double[] _cores = new double[12];
    private double _gpu = 0.18, _mem = 0.52;
    public SampleSystem(int seed = 7) { _r = new Random(seed); for (int i = 0; i < _cores.Length; i++) _cores[i] = 0.1 + 0.3 * _r.NextDouble(); }
    public string Chip => "Intel Core Ultra 7 155H";
    public string GpuName => "Intel Arc Graphics";
    public SystemSample Read()
    {
        for (int i = 0; i < _cores.Length; i++) _cores[i] = Math.Clamp(_cores[i] + (_r.NextDouble() - 0.5) * 0.2, 0.02, 0.95);
        _gpu = Math.Clamp(_gpu + (_r.NextDouble() - 0.5) * 0.1, 0.02, 0.9);
        _mem = Math.Clamp(_mem + (_r.NextDouble() - 0.5) * 0.02, 0.3, 0.8);
        const double total = 16.0 * 1_073_741_824, disk = 512.0 * 1_073_741_824;
        return new SystemSample(_cores.Average(), (double[])_cores.Clone(), _gpu, _mem * total, total, disk * 0.62, disk);
    }
}
