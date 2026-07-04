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
    [DataRow("female-caster-druid-tiefling.png", "female", "caster", "druid", "tiefling", ".png")]
    [DataRow("female-caster-druid-water (12).png", "female", "caster", "druid", "elemental", ".png")]
    [DataRow("female-caster-bard-human__8f21a3.jpg", "female", "caster", "bard", "human", ".jpg")]
    [DataRow("female-caster-bard-human__8f21a3.JPG", "female", "caster", "bard", "human", ".JPG")]
    [DataRow("male-melee-blade-tiefling__abcdef.png", "male", "melee", "blade", "tiefling", ".png")]
    [DataRow("female-generic-aasimar__123abc.webp", "female", "generic", null, "aasimar", ".webp")]
    [DataRow("male-melee-polearm-monster (76).jpg", "male", "melee", "polearm", "monster", ".jpg")]
    [DataRow("male-melee-rare-human (12).jpg", "male", "melee", "exotic", "human", ".jpg")]
    [DataRow("male-range-bow-elf(86).jpg", "male", "range", "bow", "elf", ".jpg")]
    [DataRow("male-melee-scyth-human(123).jpg", "male", "melee", "scythe", "human", ".jpg")]
    [DataRow("male-melee-scyth-human(124).jpg", "male", "melee", "scythe", "human", ".jpg")]
    [DataRow("male-melee-flail-human (82).PNG", "male", "melee", "flail", "human", ".PNG")]
    [DataRow("male-melee-mace-dwarf (21).png", "male", "melee", "mace", "dwarf", ".png")]
    [DataRow("male-melee-flail-human (119).jpg", "male", "melee", "flail", "human", ".jpg")]
    [DataRow("male-melee-flail-human (240).jpg", "male", "melee", "flail", "human", ".jpg")]
    [DataRow("male-melee-blade-human narukami.png", "male", "melee", "blade", "human", ".png")]
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

    [TestMethod]
    [DataRow("tiefling")]
    [DataRow("aasimar")]
    [DataRow("vampire")]
    [DataRow("demon")]
    [DataRow("kitsune")]
    [DataRow("elemental")]
    [DataRow("dwarf")]
    [DataRow("grippli")]
    [DataRow("oread")]
    [DataRow("mermaid")]
    [DataRow("construct")]
    [DataRow("chimera")]
    [DataRow("monster")]
    public void Parse_RecognizesExpandedRaceTags(string race)
    {
        var result = FilenameParser.Parse($"male-melee-blade-{race}.jpg");

        Assert.IsTrue(result.IsRecognized);
        Assert.AreEqual(race, result.Tags.Race);
    }

    [TestMethod]
    [DataRow("male-melee-stick-human.jpg", "melee", "blunt", "human")]
    [DataRow("male-melee-baton-human.jpg", "melee", "blunt", "human")]
    [DataRow("male-melee-tonfa-human.jpg", "melee", "blunt", "human")]
    [DataRow("male-range-starknife-human.jpg", "range", "thrown", "human")]
    [DataRow("male-melee-kama-human.jpg", "melee", "dagger", "human")]
    [DataRow("male-melee-drill-construct.jpg", "melee", "exotic", "construct")]
    [DataRow("male-melee-chainsaw-mecha.jpg", "melee", "exotic", "construct")]
    [DataRow("male-melee-rare-human.jpg", "melee", "exotic", "human")]
    [DataRow("male-melee-blade-automaton.jpg", "melee", "blade", "construct")]
    [DataRow("male-melee-blade-doll.jpg", "melee", "blade", "construct")]
    [DataRow("male-melee-blade-clockwork.jpg", "melee", "blade", "construct")]
    [DataRow("female-melee-blade-gripple.jpg", "melee", "blade", "grippli")]
    [DataRow("female-caster-druid-water.jpg", "caster", "druid", "elemental")]
    public void Parse_NormalizesLegacyAliases(string fileName, string role, string style, string race)
    {
        var result = FilenameParser.Parse(fileName);

        Assert.IsTrue(result.IsRecognized);
        Assert.AreEqual(role, result.Tags.Role);
        Assert.AreEqual(style, result.Tags.WeaponOrStyle);
        Assert.AreEqual(race, result.Tags.Race);
    }

    [TestMethod]
    public void Parse_PreservesInvalidStructuredTagsForVisualWarning()
    {
        var result = FilenameParser.Parse("male-range-blade-human.jpg");

        Assert.IsFalse(result.IsRecognized);
        Assert.AreEqual("male", result.Tags.Gender);
        Assert.AreEqual("range", result.Tags.Role);
        Assert.AreEqual("blade", result.Tags.WeaponOrStyle);
        Assert.AreEqual("human", result.Tags.Race);
    }

    [TestMethod]
    public void Parse_PreservesUnknownLegacyPartsWithoutThrowing()
    {
        var result = FilenameParser.Parse("male-melee-lance-minotaur.jpg");

        Assert.IsFalse(result.IsRecognized);
        Assert.AreEqual("male", result.Tags.Gender);
        Assert.AreEqual("melee", result.Tags.Role);
        Assert.AreEqual("lance", result.Tags.WeaponOrStyle);
        Assert.AreEqual("minotaur", result.Tags.Race);
    }
}
