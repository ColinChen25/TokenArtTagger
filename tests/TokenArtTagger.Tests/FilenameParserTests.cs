using TokenArtTagger.Core;

namespace TokenArtTagger.Tests;

[TestClass]
public sealed class FilenameParserTests
{
    [TestMethod]
    [DataRow("female-caster-bard-human (036).jpg", "female", "caster", "bard", "human", ".jpg")]
    [DataRow("female-caster-bard-elf (840).JPG", "female", "caster", "bard", "elf", ".JPG")]
    [DataRow("female-caster-bard-halfling (75).png", "female", "caster", "bard", "halfling", ".png")]
    [DataRow("female-melee-blade-human.png", "female", "melee", "blade", "human", ".png")]
    [DataRow("male-range-gun-beastfolk.jfif", "male", "range", "gun", "beastfolk", ".jfif")]
    [DataRow("hero.gif", null, null, null, null, ".gif")]
    public void Parse_RecognizesSupportedNamesAndPreservesExtension(string fileName, string? gender, string? role, string? style, string? race, string extension)
    {
        var result = FilenameParser.Parse(fileName);

        Assert.AreEqual(extension, result.Extension);
        Assert.AreEqual(gender, result.Tags.Gender);
        Assert.AreEqual(role, result.Tags.Role);
        Assert.AreEqual(style, result.Tags.WeaponOrStyle);
        Assert.AreEqual(race, result.Tags.Race);
    }

    [TestMethod]
    public void Parse_RecognizesGenericFormWithoutStyle()
    {
        var result = FilenameParser.Parse("male-generic-human (12).webp");

        Assert.IsTrue(result.IsRecognized);
        Assert.AreEqual("male", result.Tags.Gender);
        Assert.AreEqual("generic", result.Tags.Role);
        Assert.IsNull(result.Tags.WeaponOrStyle);
        Assert.AreEqual("human", result.Tags.Race);
    }

    [TestMethod]
    public void Parse_LeavesUnknownNamesUnrecognized()
    {
        var result = FilenameParser.Parse("beautiful portrait 123.jpeg");

        Assert.IsFalse(result.IsRecognized);
        Assert.IsNull(result.Tags.Gender);
        Assert.AreEqual(".jpeg", result.Extension);
    }
}
