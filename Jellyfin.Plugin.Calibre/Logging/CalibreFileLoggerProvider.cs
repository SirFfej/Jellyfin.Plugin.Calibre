using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.Calibre.Logging;

public sealed class CalibreFileLoggerProvider : ILoggerProvider
{
    private const string Prefix = "Jellyfin.Plugin.Calibre";
    private const int MaxRetainedFiles = 7;
    private const string FileNamePattern = "calibre-{0:yyyyMMdd}.log";

    private readonly string _logDirectory;
    private readonly Channel<string> _queue;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _writerTask;

    private readonly ConcurrentDictionary<string, CalibreFileLogger> _loggers = new();

    public CalibreFileLoggerProvider(string logDirectory)
    {
        _logDirectory = logDirectory;
        _queue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            AllowSynchronousContinuations = false
        });

        DeleteOldLogs();
        _writerTask = Task.Run(WriteLoopAsync);
    }

    public ILogger CreateLogger(string categoryName)
    {
        if (!categoryName.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return NullLogger.Instance;
        }

        return _loggers.GetOrAdd(categoryName, name => new CalibreFileLogger(name, _queue.Writer));
    }

    public void Dispose()
    {
        _queue.Writer.TryComplete();
        _cts.CancelAfter(TimeSpan.FromSeconds(3));

        try
        {
            _writerTask.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _cts.Dispose();
        }
    }

    private async Task WriteLoopAsync()
    {
        StreamWriter? writer = null;
        string? currentDay = null;

        try
        {
            await foreach (var line in _queue.Reader.ReadAllAsync(_cts.Token))
            {
                string today = DateTime.Now.ToString("yyyyMMdd");
                if (today != currentDay)
                {
                    writer?.Dispose();
                    string path = Path.Combine(_logDirectory, string.Format(FileNamePattern, DateTime.Now));
                    writer = new StreamWriter(path, append: true, encoding: Encoding.UTF8, bufferSize: 4096)
                    {
                        AutoFlush = false
                    };
                    currentDay = today;
                }

                await writer!.WriteLineAsync(line).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            while (_queue.Reader.TryRead(out var remaining))
            {
                writer?.WriteLine(remaining);
            }

            writer?.Flush();
        }
        finally
        {
            writer?.Dispose();
        }
    }

    private void DeleteOldLogs()
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-MaxRetainedFiles);
            foreach (var file in Directory.GetFiles(_logDirectory, "calibre-*.log"))
            {
                if (File.GetLastWriteTime(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch
        {
        }
    }
}

internal sealed class CalibreFileLogger : ILogger
{
    private readonly string _category;
    private readonly ChannelWriter<string> _writer;

    private const string Prefix = "Jellyfin.Plugin.Calibre.";

    internal CalibreFileLogger(string category, ChannelWriter<string> writer)
    {
        _category = category.StartsWith(Prefix, StringComparison.Ordinal)
            ? category[Prefix.Length..]
            : category;
        _writer = writer;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

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

        string levelStr = logLevel switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "???"
        };

        string message = formatter(state, exception);
        var sb = new StringBuilder(128);
        sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
        sb.Append(" [");
        sb.Append(levelStr);
        sb.Append("] ");
        sb.Append(_category);
        sb.Append(": ");
        sb.Append(message);

        if (exception is not null)
        {
            sb.AppendLine();
            sb.Append(exception);
        }

        string logLine = sb.ToString()
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

        _writer.TryWrite(logLine);
    }
}