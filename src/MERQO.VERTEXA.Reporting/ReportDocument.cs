namespace Merqo.Vertexa.Reporting;

public enum ReportType
{
    Full = 0,
    BuyerInspection = 1,
    Technician = 2,
    BatteryHealth = 3,
    StorageHealth = 4,
    Performance = 5,
    DriverInspection = 6,
    SingleTest = 7,
}

public static class ReportTypeNames
{
    public static string Title(this ReportType type) => type switch
    {
        ReportType.Full => "Full system diagnostic report",
        ReportType.BuyerInspection => "Buyer inspection report",
        ReportType.Technician => "Technician report",
        ReportType.BatteryHealth => "Battery health report",
        ReportType.StorageHealth => "Storage health report",
        ReportType.Performance => "Performance report",
        ReportType.DriverInspection => "Driver inspection report",
        _ => "Test report",
    };

    public static string FileStem(this ReportType type) => type switch
    {
        ReportType.Full => "full-diagnostic",
        ReportType.BuyerInspection => "buyer-inspection",
        ReportType.Technician => "technician",
        ReportType.BatteryHealth => "battery-health",
        ReportType.StorageHealth => "storage-health",
        ReportType.Performance => "performance",
        ReportType.DriverInspection => "driver-inspection",
        _ => "test",
    };
}

public sealed record ReportRow(string Name, string Value, string Source, string State, string Confidence);

public sealed record ReportSection(string Title, string? Intro, IReadOnlyList<ReportRow> Rows, IReadOnlyList<string>? Notes = null);

public sealed record ReportDocument(
    string Title,
    string Subtitle,
    ReportType Type,
    string RunId,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<(string Name, string Value)> Metadata,
    IReadOnlyList<ReportSection> Sections,
    IReadOnlyList<string> Findings,
    IReadOnlyList<string> Limitations,
    string Watermark)
{
    public int RowCount => Sections.Sum(s => s.Rows.Count);
}
