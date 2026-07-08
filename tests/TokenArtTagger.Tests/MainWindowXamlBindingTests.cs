using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TokenArtTagger.Tests;

[TestClass]
public class MainWindowXamlBindingTests
{
    [TestMethod]
    [DataRow("FullPath")]
    [DataRow("FileName")]
    [DataRow("ParsedTagsText")]
    [DataRow("CurrentTagsText")]
    [DataRow("ProposedFileName")]
    [DataRow("PreviewError")]
    public void SelectableLibraryDetailTextBoxesUseOneWayBindings(string propertyName)
    {
        var xaml = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "TokenArtTagger.App", "MainWindow.xaml"));

        StringAssert.Contains(
            xaml,
            $"Text=\"{{Binding {propertyName}, Mode=OneWay}}\"",
            $"Library details TextBox binding for {propertyName} must be OneWay. TextBox.Text defaults to TwoWay, which crashes when bound to read-only view-model properties.");
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TokenArtTagger.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate TokenArtTagger repository root.");
    }
}
