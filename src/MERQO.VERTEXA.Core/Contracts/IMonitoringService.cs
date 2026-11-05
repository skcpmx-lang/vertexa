namespace Merqo.Vertexa.Core.Contracts;

/// <summary>One live sample. Every field is nullable because a metric may be unavailable.</summary>
public sealed record MonitoringSample(
    DateTimeOffset Timestamp,
    double? CpuLoadPercent,
    double? CpuClockMhz,
    double? MemoryUsedPercent,
    double? MemoryAvailableBytes,
    double? DiskReadBytesPerSecond,
    double? DiskWriteBytesPerSecond,
    double? NetworkBytesPerSecond,
    double? GpuUtilisationPercent,
    double? HottestTemperatureCelsius,
    string? HottestTemperatureSource,
    double? BatteryPercent,
    string? PowerSource,
    IReadOnlyList<string> Unavailable);

public interface IMonitoringService : IDisposable
{
    event EventHandler<MonitoringSample>? Sampled;
    bool IsRunning { get; }
    TimeSpan Interval { get; }
    void Start(TimeSpan interval);
    void Stop();
    Task<MonitoringSample> SampleOnceAsync(CancellationToken cancellationToken = default);
}
