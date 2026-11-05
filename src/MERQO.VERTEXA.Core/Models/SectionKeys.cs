namespace Merqo.Vertexa.Core.Models;

/// <summary>Stable section identifiers — these appear in reports and history, so they are versioned.</summary>
public static class SectionKeys
{
    public const string System = "system";
    public const string Cpu = "cpu";
    public const string Gpu = "gpu";
    public const string Memory = "memory";
    public const string Firmware = "firmware";
    public const string Battery = "battery";
    public const string Storage = "storage";
    public const string Smart = "storage.smart";
    public const string Drivers = "drivers";
    public const string Network = "network";
    public const string Peripherals = "peripherals";
    public const string Thermal = "thermal";
    public const string Security = "security";
    public const string Os = "os";
    public const string Display = "display";
    public const string Audio = "audio";
    public const string Sensors = "sensors";

    public static readonly (string Key, string Title, string Description)[] Catalogue =
    {
        (System, "System", "Machine identity and operating-system facts"),
        (Cpu, "Processor", "CPU identity, topology, clocks and caches"),
        (Gpu, "Graphics", "Graphics adapters, drivers and video memory"),
        (Memory, "Memory", "Installed modules, live usage and commit charge"),
        (Firmware, "Board & firmware", "Motherboard, BIOS/UEFI and SMBIOS data"),
        (Battery, "Battery & power", "Capacity, wear, cycles and charge state"),
        (Storage, "Storage devices", "Drives, media, partitions and free space"),
        (Smart, "Drive health", "S.M.A.R.T. and NVMe health telemetry"),
        (Drivers, "Drivers & devices", "PnP devices, driver versions and problem codes"),
        (Network, "Network", "Adapters, link state and wireless information"),
        (Peripherals, "Peripherals", "USB, Bluetooth, cameras and sensors"),
        (Thermal, "Thermal", "Temperature and throttling telemetry where exposed"),
        (Security, "Security platform", "Secure Boot, TPM and virtualisation-based security"),
        (Os, "Windows", "Edition, build, activation and update state"),
        (Display, "Display", "Monitors, resolutions and refresh rates"),
        (Audio, "Audio", "Playback and capture endpoints"),
        (Sensors, "Sensors", "Ambient light, accelerometer and other CIM sensors"),
    };
}
