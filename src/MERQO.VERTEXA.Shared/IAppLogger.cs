namespace Merqo.Vertexa.Shared;

public enum LogLevel { Debug = 0, Info = 1, Warn = 2, Error = 3 }

/// <summary>Structured logging abstraction (no third-party logging dependency).</summary>
public interface IAppLogger
{
    void Log(LogLevel level, string area, string message, Exception? error = null, IReadOnlyDictionary<string, object?>? data = null);
}

public static class AppLoggerExtensions
{
    public static void Debug(this IAppLogger log, string area, string message) => log.Log(LogLevel.Debug, area, message);
    public static void Info(this IAppLogger log, string area, string message, IReadOnlyDictionary<string, object?>? data = null) => log.Log(LogLevel.Info, area, message, null, data);
    public static void Warn(this IAppLogger log, string area, string message, Exception? error = null) => log.Log(LogLevel.Warn, area, message, error);
    public static void Error(this IAppLogger log, string area, string message, Exception? error = null, IReadOnlyDictionary<string, object?>? data = null) => log.Log(LogLevel.Error, area, message, error, data);
}

public sealed class NullLogger : IAppLogger
{
    public static readonly NullLogger Instance = new();
    public void Log(LogLevel level, string area, string message, Exception? error = null, IReadOnlyDictionary<string, object?>? data = null) { }
}
