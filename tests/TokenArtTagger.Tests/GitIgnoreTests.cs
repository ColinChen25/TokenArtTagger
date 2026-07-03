namespace TokenArtTagger.Tests;

[TestClass]
public sealed class GitIgnoreTests
{
    [TestMethod]
    [DataRow(".tokenarttagger-undo/")]
    [DataRow("*.undo.json")]
    [DataRow("*undo-log*.json")]
    [DataRow("appsettings.Local.json")]
    [DataRow("user-settings.json")]
    [DataRow("work-in-progress-tags.json")]
    [DataRow("*.jpg")]
    [DataRow("*.jpeg")]
    [DataRow("*.jfif")]
    [DataRow("*.png")]
    [DataRow("*.webp")]
    [DataRow("*.gif")]
    [DataRow(".cache/")]
    [DataRow("thumbnails/")]
    [DataRow("thumbnail-cache/")]
    public void GitIgnore_IncludesPublicSafetyPattern(string pattern)
    {
        var gitIgnore = File.ReadAllText(FindRepoFile(".gitignore"));

        StringAssert.Contains(gitIgnore, pattern);
    }

    private static string FindRepoFile(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find {fileName} from {AppContext.BaseDirectory}.");
    }
}
