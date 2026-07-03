using TokenArtTagger.Core;

namespace TokenArtTagger.Tests;

[TestClass]
public sealed class FileScannerTests
{
    [TestMethod]
    public async Task ScanAsync_RecursivelyFindsOnlySupportedImages()
    {
        using var temp = new TempFolder();
        var nested = Directory.CreateDirectory(Path.Combine(temp.Path, "nested"));
        File.WriteAllBytes(Path.Combine(temp.Path, "female-melee-blade-human.png"), [1]);
        File.WriteAllBytes(Path.Combine(nested.FullName, "male-range-gun-beastfolk.jfif"), [2]);
        File.WriteAllText(Path.Combine(temp.Path, "notes.txt"), "skip");

        var result = await FileScanner.ScanAsync(temp.Path);

        Assert.AreEqual(2, result.Items.Count);
        Assert.AreEqual(0, result.Errors.Count);
        CollectionAssert.Contains(result.Items.Select(item => item.Extension.ToLowerInvariant()).ToList(), ".png");
        CollectionAssert.Contains(result.Items.Select(item => item.Extension.ToLowerInvariant()).ToList(), ".jfif");
    }
}
