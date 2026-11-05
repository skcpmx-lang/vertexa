using Merqo.Vertexa.Core;
using Merqo.Vertexa.Core.Contracts;
using Merqo.Vertexa.Shared;
using Merqo.Vertexa.Windows.Providers;

namespace Merqo.Vertexa.Windows;

/// <summary>Composition entry point for the Windows integration layer.</summary>
public static class WindowsServices
{
    /// <summary>
    /// Builds the collector set. Order matters: identity first (cheap, informs the rest),
    /// then heavy telemetry. The orchestrator runs them in this order.
    /// </summary>
    public static IReadOnlyList<IScanCollector> CreateCollectors(IAppLogger? log = null) => new IScanCollector[]
    {
        new SystemCollector(log),
        new OsCollector(log),
        new CpuCollector(log),
        new MemoryCollector(log),
        new FirmwareCollector(log),
        new SecurityCollector(log),
        new GpuCollector(log),
        new StorageCollector(log),
        new BatteryCollector(log),
        new DisplayCollector(log),
        new ThermalCollector(log),
        new DriverCollector(log),
        new NetworkCollector(log),
        new PeripheralCollector(log),
    };

    public static IMonitoringService CreateMonitoring(IAppLogger? log = null) => new WindowsMonitoringService(log);

    /// <summary>True when the process holds an administrator token (read-only check).</summary>
    public static bool IsProcessElevated() => Native.NativeMethods.IsProcessElevated();

    public static ServiceRegistry AddWindowsServices(this ServiceRegistry registry, IAppLogger log)
    {
        registry.AddSingleton(log);
        registry.AddSingleton<IReadOnlyList<IScanCollector>>(_ => CreateCollectors(log));
        registry.AddSingleton<IMonitoringService>(_ => CreateMonitoring(log));
        return registry;
    }
}
