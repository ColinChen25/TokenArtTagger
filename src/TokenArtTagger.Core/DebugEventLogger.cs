using System.Text;

namespace TokenArtTagger.Core;

public sealed class DebugEventLogger : IDisposable
{
    private readonly object _sync = new();
    private readonly StreamWriter _writer;
    private bool _disposed;

    private DebugEventLogger(string logFilePath)
    {
        LogFilePath = logFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(logFilePath)!);
        var stream = new FileStream(logFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite);
        _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
        Write("LogHeader", details: new Dictionary<string, string?>
        {
            ["appVersion"] = AppInfo.Version,
            ["configuration"] = BuildConfiguration(),
            ["os"] = Environment.OSVersion.VersionString,
            ["runtime"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription
        });
    }

    public string LogFilePath { get; }

    public static string DefaultLogFolder()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TokenArtTagger",
            "Logs");
    }

    public static DebugEventLogger CreateDefault()
    {
        return Create(DefaultLogFolder(), DateTimeOffset.Now);
    }

    public static DebugEventLogger Create(string logFolder, DateTimeOffset launchTime)
    {
        Directory.CreateDirectory(logFolder);
        var baseName = $"{launchTime:yyyy-MM-dd_HHmmss}_{AppInfo.Version}.log";
        var path = Path.Combine(logFolder, baseName);
        var counter = 1;
        while (File.Exists(path))
        {
            path = Path.Combine(logFolder, $"{launchTime:yyyy-MM-dd_HHmmss}_{AppInfo.Version}-{counter}.log");
            counter++;
        }

        return new DebugEventLogger(path);
    }

    public IDisposable Enter(
        string eventName,
        string? mode = null,
        string? selected = null,
        IReadOnlyDictionary<string, string?>? details = null)
    {
        Write($"{eventName}.enter", mode, selected, details);
        return new EventScope(this, eventName, mode, selected);
    }

    public void Write(
        string eventName,
        string? mode = null,
        string? selected = null,
        IReadOnlyDictionary<string, string?>? details = null)
    {
        try
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _writer.Write(DateTimeOffset.Now.ToString("O"));
                _writer.Write(" tid=");
                _writer.Write(Environment.CurrentManagedThreadId);
                _writer.Write(" event=");
                _writer.Write(SanitizeToken(eventName));
                WriteField("mode", mode);
                WriteField("selected", selected);
                if (details is not null)
                {
                    foreach (var (key, value) in details.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                    {
                        WriteField(key, value);
                    }
                }

                _writer.WriteLine();
                _writer.Flush();
            }
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _writer.Dispose();
        }
    }

    public static string SanitizeValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "-";
        }

        var sanitized = value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ')
            .Trim();

        while (sanitized.Contains("  ", StringComparison.Ordinal))
        {
            sanitized = sanitized.Replace("  ", " ", StringComparison.Ordinal);
        }

        return sanitized.Length <= 240 ? sanitized : sanitized[..240] + "...";
    }

    public static string SafeNameFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "-";
        }

        try
        {
            return SanitizeValue(Path.GetFileName(path));
        }
        catch (ArgumentException)
        {
            return SanitizeValue(path);
        }
    }

    private void WriteField(string key, string? value)
    {
        _writer.Write(' ');
        _writer.Write(SanitizeToken(key));
        _writer.Write("=\"");
        _writer.Write(SanitizeValue(value).Replace("\"", "'", StringComparison.Ordinal));
        _writer.Write('"');
    }

    private static string SanitizeToken(string value)
    {
        var token = SanitizeValue(value);
        return string.Concat(token.Select(character =>
            char.IsLetterOrDigit(character) || character is '.' or '_' or '-' ? character : '_'));
    }

    private static string BuildConfiguration()
    {
#if DEBUG
        return "Debug";
#else
        return "Release";
#endif
    }

    private sealed class EventScope : IDisposable
    {
        private readonly DebugEventLogger _logger;
        private readonly string _eventName;
        private readonly string? _mode;
        private readonly string? _selected;
        private bool _disposed;

        public EventScope(DebugEventLogger logger, string eventName, string? mode, string? selected)
        {
            _logger = logger;
            _eventName = eventName;
            _mode = mode;
            _selected = selected;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _logger.Write($"{_eventName}.exit", _mode, _selected);
        }
    }
}
