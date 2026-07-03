using System.IO;
using System.Text.RegularExpressions;

namespace TokenArtTagger.Core;

public static partial class FilenameParser
{
    public static FilenameParseResult Parse(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var parseBaseName = HashSuffixRegex().Replace(DuplicateSuffixRegex().Replace(baseName, string.Empty), string.Empty);
        var parts = parseBaseName.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.ToLowerInvariant())
            .ToArray();

        if (TryParseGeneric(parts, out var genericTags))
        {
            return new FilenameParseResult(fileName, parseBaseName, extension, genericTags, true);
        }

        if (TryParseNormal(parts, out var normalTags))
        {
            return new FilenameParseResult(fileName, parseBaseName, extension, normalTags, true);
        }

        return new FilenameParseResult(fileName, parseBaseName, extension, new TagSet(), false);
    }

    private static bool TryParseGeneric(IReadOnlyList<string> parts, out TagSet tags)
    {
        tags = new TagSet();
        if (parts.Count != 3)
        {
            return false;
        }

        var gender = parts[0];
        var role = parts[1];
        var race = TagSchema.NormalizeRace(parts[2]);

        if (!TagSchema.IsKnownGender(gender) || role != TagSchema.GenericRole || !TagSchema.IsKnownRace(race))
        {
            return false;
        }

        tags = new TagSet(gender, role, null, race);
        return true;
    }

    private static bool TryParseNormal(IReadOnlyList<string> parts, out TagSet tags)
    {
        tags = new TagSet();
        if (parts.Count != 4)
        {
            return false;
        }

        var gender = parts[0];
        var role = parts[1];
        var style = parts[2];
        var race = TagSchema.NormalizeRace(parts[3]);

        if (!TagSchema.IsKnownGender(gender) ||
            !TagSchema.IsKnownRole(role) ||
            role == TagSchema.GenericRole ||
            !TagSchema.IsKnownStyleForRole(role, style) ||
            !TagSchema.IsKnownRace(race))
        {
            return false;
        }

        tags = new TagSet(gender, role, style, race);
        return true;
    }

    [GeneratedRegex(@"\s\(\d+\)$", RegexOptions.CultureInvariant)]
    private static partial Regex DuplicateSuffixRegex();

    [GeneratedRegex(@"__[0-9a-fA-F]{6,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex HashSuffixRegex();
}
