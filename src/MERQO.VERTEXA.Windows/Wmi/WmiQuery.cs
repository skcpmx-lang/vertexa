using System.Globalization;
using System.Management;

namespace Merqo.Vertexa.Windows.Wmi;

/// <summary>
/// Thin, defensive wrapper over System.Management. WMI is the primary source for most
/// identity facts, but individual classes/properties are frequently missing or access
/// denied on real machines, so every call reports its own failure instead of throwing
/// into a collector.
/// </summary>
internal static class WmiQuery
{
    internal const string DefaultScope = @"root\cimv2";
    internal const string WmiScope = @"root\wmi";
    internal const string TpmScope = @"root\cimv2\security\microsofttpm";

    internal sealed class WmiRow
    {
        private readonly Dictionary<string, object?> _values;

        internal WmiRow(Dictionary<string, object?> values) => _values = values;

        public IReadOnlyDictionary<string, object?> Values => _values;

        public object? this[string property] => _values.TryGetValue(property, out var value) ? value : null;

        public string? Text(string property)
        {
            var value = this[property];
            return value switch
            {
                null => null,
                string s => string.IsNullOrWhiteSpace(s) ? null : s.Trim(),
                string[] arr => arr.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)),
                ushort[] arr => Convert.ToString(arr.FirstOrDefault(), CultureInfo.InvariantCulture),
                _ => Convert.ToString(value, CultureInfo.InvariantCulture),
            };
        }

        public uint? UInt(string property)
        {
            var value = this[property];
            if (value is null) return null;
            try { return Convert.ToUInt32(value, CultureInfo.InvariantCulture); }
            catch { return null; }
        }

        public ulong? ULong(string property)
        {
            var value = this[property];
            if (value is null) return null;
            try { return Convert.ToUInt64(value, CultureInfo.InvariantCulture); }
            catch { return null; }
        }

        public int? Int(string property)
        {
            var value = this[property];
            if (value is null) return null;
            try { return Convert.ToInt32(value, CultureInfo.InvariantCulture); }
            catch { return null; }
        }

        public bool? Bool(string property)
        {
            var value = this[property];
            if (value is null) return null;
            try { return Convert.ToInt32(value, CultureInfo.InvariantCulture) != 0; }
            catch { return null; }
            }

        public DateTime? Date(string property)
        {
            var value = this[property];
            if (value is null) return null;
            try { return ManagementDateTimeConverter.ToDateTime(value.ToString()!); }
            catch { return null; }
        }
    }

    internal sealed record QueryResult(IReadOnlyList<WmiRow> Rows, string? Error, TimeSpan Elapsed)
    {
        public bool Ok => Error is null;
        public WmiRow? First => Rows.FirstOrDefault();
        public string? FirstText(string property) => First?.Text(property);
    }

    /// <summary>
    /// Runs a WQL query. Never throws: failures come back in <see cref="QueryResult.Error"/>
    /// so the caller can attribute them (this is the multi-method fallback contract).
    /// </summary>
    internal static QueryResult Run(string wql, string scope = DefaultScope, int timeoutMs = 8000, bool includeNullValues = false)
    {
        var started = DateTime.UtcNow;
        var rows = new List<WmiRow>();
        try
        {
            var searcher = new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(wql));
            searcher.Options.Timeout = TimeSpan.FromMilliseconds(timeoutMs);
            searcher.Options.ReturnImmediately = true;
            using (searcher)
            using (var collection = searcher.Get())
            {
                foreach (ManagementBaseObject item in collection)
                {
                    using (item)
                    {
                        var bag = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                        try
                        {
                            foreach (PropertyData property in item.Properties)
                            {
                                object? value;
                                try { value = property.Value; }
                                catch { value = null; }
                                if (value is null && !includeNullValues) continue;
                                bag[property.Name] = value;
                            }
                        }
                        catch (ManagementException ex)
                        {
                            bag["__propertyError"] = ex.Message;
                        }
                        rows.Add(new WmiRow(bag));
                    }
                }
            }
            return new QueryResult(rows, null, DateTime.UtcNow - started);
        }
        catch (ManagementException ex)
        {
            var reason = ex.ErrorCode switch
            {
                ManagementStatus.AccessDenied => "access denied (elevation required for this class)",
                ManagementStatus.NotFound => "class or instance not present on this system",
                ManagementStatus.InvalidClass => "WMI class is not supported by this OS build",
                ManagementStatus.InvalidQuery => "query rejected: " + ex.Message,
                ManagementStatus.Timedout => "WMI query timed out",
                _ => $"WMI error {ex.ErrorCode}: {ex.Message}",
            };
            return new QueryResult(rows, reason, DateTime.UtcNow - started);
        }
        catch (UnauthorizedAccessException ex)
        {
            return new QueryResult(rows, "access denied: " + ex.Message, DateTime.UtcNow - started);
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            return new QueryResult(rows, "WMI COM failure: " + ex.Message, DateTime.UtcNow - started);
        }
        catch (Exception ex)
        {
            return new QueryResult(rows, $"{ex.GetType().Name}: {ex.Message}", DateTime.UtcNow - started);
        }
    }
}
