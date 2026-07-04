using System.Text.RegularExpressions;

namespace TokenArtTagger.Core;

public static partial class CrashLogService
{
    public static string DefaultLogFolder()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TokenArtTagger",
            "Logs");
    }

    public static async Task<string> WriteCrashLogAsync(
        Exception exception,
        string? logFolder = null,
        CancellationToken cancellationToken = default)
    {
        var folder = logFolder ?? DefaultLogFolder();
        Directory.CreateDirectory(folder);
        var logPath = Path.Combine(folder, $"crash-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}.log");
        var text = SanitizeExceptionText(exception.ToString());
        await File.WriteAllTextAsync(logPath, text, cancellationToken).ConfigureAwait(false);
        return logPath;
    }

    public static string SanitizeExceptionText(string text)
    {
        return WindowsPathRegex().Replace(text, match => Path.GetFileName(match.Value.TrimEnd('\\')));
    }

    [GeneratedRegex(@"[A-Za-z]:\\[^\r\n]+?(?=(?:\s+(?:from|at|in)\s+)|:line\s+\d+|$)", RegexOptions.CultureInvariant)]
    private static partial Regex WindowsPathRegex();
}
