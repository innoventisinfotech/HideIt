using System.IO;

namespace HideIt.Services;

/// <summary>Minimal append-only crash/diagnostic logger to %AppData%\HideIt\logs.</summary>
public static class Logger
{
    private static readonly object Gate = new();
    private static string _logFile = "";

    public static string LogDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HideIt", "logs");

    public static void Init()
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            _logFile = Path.Combine(LogDir, $"hideit-{DateTime.Now:yyyyMMdd}.log");
        }
        catch { /* logging must never crash the app */ }
    }

    public static void LogException(string context, Exception? ex) =>
        Write($"[{context}] {ex}");

    public static void Write(string message)
    {
        if (string.IsNullOrEmpty(_logFile)) return;
        try
        {
            lock (Gate)
                File.AppendAllText(_logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
        }
        catch { /* swallow */ }
    }
}
