using System.Diagnostics;

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

    [TestMethod]
    public void GitIgnore_ActuallyExcludesPrivatePublicationArtifacts()
    {
        var root = Path.GetDirectoryName(FindRepoFile(".gitignore"))!;
        var paths = new[]
        {
            ".env", ".env.local", ".aws/credentials", ".codex/config.toml", "local-secret.key", "identity.pem",
            "id_rsa", "id_ed25519", "model.gguf", "model.safetensors", "encoder.onnx", "features.npy",
            "features.npz", "classifier.pkl", "private.sqlite3", "AiSuggestions/Reviews/example.json", "queue-v1.json",
            "dataset-manifest.json", "inventory-summary.json", "file-metadata.json", "review.html", "AGENTS.local.md",
            "private-history.bundle", "portrait.avif", "portrait.heic", "portrait.bmp", "portrait.tiff",
            ".cache/privacy-audit/pre-cleanup.bundle", ".cache/local-ai/project-status.md"
        };
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = root, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
        };
        foreach (var arg in new[] { "-c", $"safe.directory={root.Replace('\\', '/')}", "check-ignore", "--no-index", "--stdin" })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        process.StandardInput.Write(string.Join('\n', paths) + "\n");
        process.StandardInput.Close();
        var ignored = process.StandardOutput.ReadToEnd().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var error = process.StandardError.ReadToEnd();
        Assert.IsTrue(process.WaitForExit(10000), "git check-ignore timed out.");
        Assert.AreEqual(0, process.ExitCode, error);
        CollectionAssert.AreEquivalent(paths, ignored, "Private artifacts must be excluded by Git's actual matching rules.");
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
