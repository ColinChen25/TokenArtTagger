using TokenArtTagger.Core;

namespace TokenArtTagger.Tests;

[TestClass]
public sealed class DebugEventLoggerTests
{
    [TestMethod]
    public void Create_CreatesLogFolderAndLogFile()
    {
        using var temp = new TemporaryDirectory();
        var logFolder = Path.Combine(temp.Path, "logs");

        string logFilePath;
        using (var logger = DebugEventLogger.Create(logFolder, new DateTimeOffset(2026, 7, 8, 12, 34, 56, TimeSpan.Zero)))
        {
            logger.Write("TestEvent", "Library", "selected=1");
            logFilePath = logger.LogFilePath;
        }

        Assert.IsTrue(Directory.Exists(logFolder));
        Assert.IsTrue(File.Exists(logFilePath));
        StringAssert.Contains(File.ReadAllText(logFilePath), "event=TestEvent");
    }

    [TestMethod]
    public void Create_UsesSortableVersionedFilenameAndWritesHeader()
    {
        using var temp = new TemporaryDirectory();

        string logFilePath;
        using (var logger = DebugEventLogger.Create(temp.Path, new DateTimeOffset(2026, 7, 8, 23, 14, 55, TimeSpan.Zero)))
        {
            logFilePath = logger.LogFilePath;
        }

        Assert.AreEqual($"2026-07-08_231455_{AppInfo.Version}.log", Path.GetFileName(logFilePath));
        var text = File.ReadAllText(logFilePath);
        StringAssert.Contains(text, $"appVersion=\"{AppInfo.Version}\"");
        StringAssert.Contains(text, "runtime=\"");
        StringAssert.Contains(text, "os=\"");
    }

    [TestMethod]
    public void SanitizeValue_RemovesLineBreaksAndTabs()
    {
        var value = DebugEventLogger.SanitizeValue("alpha\r\nbeta\tgamma");

        Assert.AreEqual("alpha beta gamma", value);
    }

    [TestMethod]
    public void SafeNameFromPath_ReturnsOnlyFilename()
    {
        var value = DebugEventLogger.SafeNameFromPath(@"C:\Path\To\Your\ImageLibrary\sample image.png");

        Assert.AreEqual("sample image.png", value);
    }
}

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"TokenArtTaggerTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
