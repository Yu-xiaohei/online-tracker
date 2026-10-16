using System.Collections.Concurrent;

namespace StatusTracker.Services;

public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error,
}

/// <summary>按天滚动的文件日志。敏感字段禁止写入。</summary>
public sealed class Logger
{
    private static readonly Lazy<Logger> LazyInstance = new(() => new Logger());
    public static Logger Instance => LazyInstance.Value;

    private readonly ConcurrentQueue<string> _pending = new();
    private readonly string _logDirectory;
    private readonly object _writeLock = new();

    public LogLevel MinLevel { get; set; } = LogLevel.Debug;

    private Logger()
    {
        _logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PcStatusNotifier", "logs");
        Directory.CreateDirectory(_logDirectory);
    }

    public string LogDirectory => _logDirectory;

    public void Debug(string message) => Write(LogLevel.Debug, message);
    public void Info(string message) => Write(LogLevel.Info, message);
    public void Warn(string message) => Write(LogLevel.Warn, message);
    public void Error(string message) => Write(LogLevel.Error, message);

    public void Write(LogLevel level, string message)
    {
        if (level < MinLevel) return;
        _pending.Enqueue($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{level,-5}] {message}");

        lock (_writeLock)
        {
            try
            {
                var path = Path.Combine(_logDirectory, $"app-{DateTime.Now:yyyyMMdd}.log");
                while (_pending.TryDequeue(out var entry))
                {
                    File.AppendAllText(path, entry + Environment.NewLine);
                }
            }
            catch
            {
                // 日志失败不影响主流程
            }
        }
    }
}
