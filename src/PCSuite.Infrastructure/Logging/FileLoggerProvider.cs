using System.Text;
using Microsoft.Extensions.Logging;

namespace PCSuite.Infrastructure.Logging;

/// <summary>
/// Minimal daily file logger writing to %LOCALAPPDATA%\PCSuite\Logs\pcsuite-yyyy-MM-dd.log.
/// Callers must never pass secrets (passwords, private keys, tokens) into log messages.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly object _sync = new();
    private StreamWriter? _writer;
    private string _openFileDate = "";

    public FileLoggerProvider(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
        lock (_sync)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    private void WriteLine(string line)
    {
        // Logging must never crash the application.
        try
        {
            lock (_sync)
            {
                var today = DateTime.Now.ToString("yyyy-MM-dd");
                if (_writer is null || !string.Equals(today, _openFileDate, StringComparison.Ordinal))
                {
                    _writer?.Dispose();
                    var path = Path.Combine(_directory, $"pcsuite-{today}.log");
                    _writer = new StreamWriter(
                        new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read),
                        Encoding.UTF8)
                    {
                        AutoFlush = true,
                    };
                    _openFileDate = today;
                }

                _writer.WriteLine(line);
            }
        }
        catch
        {
            // Swallow: log output must never break the app.
        }
    }

    private sealed class FileLogger(FileLoggerProvider provider, string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var builder = new StringBuilder();
            builder.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            builder.Append(' ').Append('[').Append(ToShortString(logLevel)).Append(']');
            builder.Append(" [").Append(categoryName).Append("] ");
            builder.Append(formatter(state, exception));

            if (exception is not null)
            {
                builder.AppendLine().Append(exception);
            }

            provider.WriteLine(builder.ToString());
        }

        private static string ToShortString(LogLevel level) => level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "LOG",
        };
    }
}
