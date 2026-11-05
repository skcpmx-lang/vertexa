using Merqo.Vertexa.Core.Contracts;
using Merqo.Vertexa.Shared;
using Merqo.Vertexa.Windows.Native;
using Merqo.Vertexa.Windows.Providers;
using Merqo.Vertexa.Windows.Wmi;

namespace Merqo.Vertexa.Windows;

/// <summary>
/// Live telemetry sampler. One dedicated loop, WMI performance classes only (no raw
/// PDH interop), each metric attributed, and every unavailable metric named in the
/// sample so the UI can show why a tile is empty instead of a fake flat line.
/// </summary>
public sealed class WindowsMonitoringService : IMonitoringService
{
    private readonly IAppLogger _log;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public WindowsMonitoringService(IAppLogger? log = null) => _log = log ?? NullLogger.Instance;

    public event EventHandler<MonitoringSample>? Sampled;
    public bool IsRunning => _loop is { IsCompleted: false };
    public TimeSpan Interval { get; private set; } = TimeSpan.FromSeconds(1);

    public void Start(TimeSpan interval)
    {
        if (IsRunning) return;
        Interval = interval < TimeSpan.FromMilliseconds(250) ? TimeSpan.FromMilliseconds(250) : interval;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loop = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var sample = await SampleOnceAsync(token).ConfigureAwait(false);
                    Sampled?.Invoke(this, sample);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _log.Warn("monitor", "sampling failed", ex);
                }
                try
                {
                    await Task.Delay(Interval, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, token);
        _log.Info("monitor", $"monitoring started at {Interval.TotalMilliseconds:0} ms");
    }

    public void Stop()
    {
        if (_cts is null) return;
        _cts.Cancel();
        _cts.Dispose();
        _cts = null;
        _loop = null;
        _log.Info("monitor", "monitoring stopped");
    }

    public Task<MonitoringSample> SampleOnceAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Sample(cancellationToken), cancellationToken);

    private MonitoringSample Sample(CancellationToken cancellationToken)
    {
        var unavailable = new List<string>();
        double? cpuLoad = null;
        double? cpuClockMhz = null;
        double? memoryUsed = null;
        double? memoryAvailable = null;
        double? diskRead = null;
        double? diskWrite = null;
        double? network = null;
        double? gpu = null;
        double? temperature = null;
        string? temperatureSource = null;
        double? batteryPercent = null;
        string? powerSource = null;

        var cpu = WmiQuery.Run("SELECT PercentProcessorTime, PercentPrivilegedTime FROM Win32_PerfFormattedData_PerfOS_Processor WHERE Name='_Total'");
        if (cpu.First?.ULong("PercentProcessorTime") is { } load) cpuLoad = load;
        else unavailable.Add("CPU load");

        var processorInfo = WmiQuery.Run("SELECT PercentProcessorPerformance, PercentofMaximumFrequency FROM Win32_PerfFormattedData_Counters_ProcessorInformation WHERE Name='_Total'");
        var performance = processorInfo.First?.ULong("PercentProcessorPerformance");
        var nominal = WmiQuery.Run("SELECT MaxClockSpeed FROM Win32_Processor").First?.ULong("MaxClockSpeed");
        if (performance is > 0 && nominal is > 0) cpuClockMhz = Math.Round(nominal.Value * performance.Value / 100.0, 0);
        else unavailable.Add("CPU clock");

        var memory = NativeMethods.ReadMemoryStatus();
        if (memory.TotalPhysical > 0)
        {
            memoryUsed = memory.MemoryLoad;
            memoryAvailable = memory.AvailablePhysical;
        }
        else
        {
            unavailable.Add("Memory");
        }

        var disk = WmiQuery.Run("SELECT DiskReadBytesPersec, DiskWriteBytesPersec FROM Win32_PerfFormattedData_PerfDisk_PhysicalDisk WHERE Name='_Total'");
        if (disk.First?.ULong("DiskReadBytesPersec") is { } read) diskRead = read;
        if (disk.First?.ULong("DiskWriteBytesPersec") is { } write) diskWrite = write;
        if (read is null && write is null) unavailable.Add("Disk activity");

        var networkRows = WmiQuery.Run("SELECT BytesTotalPersec FROM Win32_PerfFormattedData_Tcpip_NetworkInterface");
        if (networkRows.Ok && networkRows.Rows.Count > 0)
            network = networkRows.Rows.Sum(r => (double)(r.ULong("BytesTotalPersec") ?? 0));
        else unavailable.Add("Network activity");

        var gpuRows = WmiQuery.Run("SELECT UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");
        if (gpuRows.Ok && gpuRows.Rows.Count > 0)
            gpu = gpuRows.Rows.Max(r => (double)(r.ULong("UtilizationPercentage") ?? 0));
        else unavailable.Add("GPU utilisation");

        var thermal = ThermalProbe.ReadHottest();
        temperature = thermal.Celsius;
        temperatureSource = thermal.Source;
        if (temperature is null) unavailable.Add("Temperature");

        if (NativeMethods.TryGetSystemPowerStatus(out var power))
        {
            if (power.BatteryLifePercent is > 0 and <= 100) batteryPercent = power.BatteryLifePercent;
            powerSource = power.AclineStatus == 1 ? "AC" : power.AclineStatus == 0 ? "Battery" : "Unknown";
        }
        cancellationToken.ThrowIfCancellationRequested();

        return new MonitoringSample(DateTimeOffset.UtcNow, cpuLoad, cpuClockMhz, memoryUsed, memoryAvailable,
            diskRead, diskWrite, network, gpu, temperature, temperatureSource, batteryPercent, powerSource, unavailable);
    }

    public void Dispose() => Stop();
}
