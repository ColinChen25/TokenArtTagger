using TokenArtTagger.Core;

namespace TokenArtTagger.Tests;

[TestClass]
public sealed class CrashLogServiceTests
{
    [TestMethod]
    public void SanitizeExceptionText_RemovesLocalPathsButKeepsFileName()
    {
        var text = CrashLogService.SanitizeExceptionText(
            @"Could not load C:\Path\To\Your\ImageLibrary\monster sample.jpg from D:\Projects\TokenArtTagger\TestImages\monster sample.jpg");

        Assert.IsFalse(text.Contains(@"C:\Path\To\Your\ImageLibrary", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(text.Contains(@"D:\Projects\TokenArtTagger\TestImages", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(text, "monster sample.jpg");
    }

    [TestMethod]
    public async Task WriteCrashLogAsync_WritesOutsideImageLibrary()
    {
        using var temp = new TempFolder();
        var logFolder = Path.Combine(temp.Path, "appdata", "Logs");

        var path = await CrashLogService.WriteCrashLogAsync(
            new InvalidOperationException(@"Sample failure in C:\Path\To\Your\ImageLibrary\sample.jpg"),
            logFolder);

        Assert.IsTrue(path.StartsWith(logFolder, StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(File.Exists(path));
        var text = await File.ReadAllTextAsync(path);
        Assert.IsFalse(text.Contains(@"C:\Path\To\Your\ImageLibrary", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(text, "sample.jpg");
    }
}
