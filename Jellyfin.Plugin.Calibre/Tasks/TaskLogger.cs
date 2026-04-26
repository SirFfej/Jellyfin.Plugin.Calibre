using System;
using System.IO;
using System.Text;

namespace Jellyfin.Plugin.Calibre.Tasks;

public static class TaskLogger
{
    private static readonly object _lock = new();
    private static string? _lastDate;

    public static void Log(string logDir, string taskName, string message)
    {
        if (string.IsNullOrEmpty(logDir))
        {
            return;
        }

        var date = DateTime.Now.ToString("yyyyMMdd");
        var path = Path.Combine(logDir, $"calibre-{taskName}-{date}.log");

        lock (_lock)
        {
            if (_lastDate != date)
            {
                _lastDate = date;
            }

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var line = $"{timestamp} {message}{Environment.NewLine}";

            try
            {
                File.AppendAllText(path, line, Encoding.UTF8);
            }
            catch
            {
            }
        }
    }
}