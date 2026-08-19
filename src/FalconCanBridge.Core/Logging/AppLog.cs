using System;

namespace FalconCanBridge.Core.Logging;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

public sealed class LogEntry
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public LogLevel Level { get; init; }
    public string Source { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;

    public override string ToString() => $"{Timestamp:HH:mm:ss.fff} [{Level}] {Source}: {Message}";
}

/// <summary>
/// Process-wide, extremely lightweight log bus. All connectors/adapters funnel their
/// human-readable status and error messages here so the WPF UI can bind a single
/// scrolling console without every component needing a reference to the view layer.
/// </summary>
public static class AppLog
{
    public static event Action<LogEntry>? EntryLogged;

    public static void Write(LogLevel level, string source, string message)
    {
        var entry = new LogEntry { Level = level, Source = source, Message = message };
        EntryLogged?.Invoke(entry);
    }

    public static void Debug(string source, string message) => Write(LogLevel.Debug, source, message);
    public static void Info(string source, string message) => Write(LogLevel.Info, source, message);
    public static void Warning(string source, string message) => Write(LogLevel.Warning, source, message);
    public static void Error(string source, string message) => Write(LogLevel.Error, source, message);
}
