using System.Text;

namespace MicaBrowser.Services;

public static class Logger
{
    private static readonly object Gate = new();
    private static string? _logFile;

    public static void Initialize(string logsDirectory)
    {
        Directory.CreateDirectory(logsDirectory);
        _logFile = Path.Combine(logsDirectory, "exet.log");
    }

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message, Exception? exception = null) => Write("WARN", message, exception);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            var builder = new StringBuilder();
            builder.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            builder.Append(" [").Append(level).Append("] ").Append(message);
            if (exception is not null)
            {
                builder.AppendLine();
                builder.Append(exception);
            }
            builder.AppendLine();

            lock (Gate)
            {
                if (_logFile is null)
                {
                    return;
                }

                if (File.Exists(_logFile) && new FileInfo(_logFile).Length > 2 * 1024 * 1024)
                {
                    File.Move(_logFile, _logFile + ".old", true);
                }

                File.AppendAllText(_logFile, builder.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never crash the browser.
        }
    }
}
