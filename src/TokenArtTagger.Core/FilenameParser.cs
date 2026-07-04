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

        if (TryParseGeneric(parts, out var genericTags, out var genericIsValid))
        {
            return new FilenameParseResult(fileName, parseBaseName, extension, genericTags, genericIsValid);
        }

        if (TryParseNormal(parts, out var normalTags, out var normalIsValid))
        {
            return new FilenameParseResult(fileName, parseBaseName, extension, normalTags, normalIsValid);
        }

        return new FilenameParseResult(fileName, parseBaseName, extension, new TagSet(), false);
    }

    private static bool TryParseGeneric(IReadOnlyList<string> parts, out TagSet tags, out bool isValid)
    {
        tags = new TagSet();
        isValid = false;
        if (parts.Count != 3)
        {
            return false;
        }

        var gender = parts[0];
        var role = parts[1];
        var race = TagSchema.NormalizeRace(FirstToken(parts[2]));

        if (!TagSchema.IsKnownGender(gender) || role != TagSchema.GenericRole)
        {
            return false;
        }

        tags = new TagSet(gender, role, null, race);
        isValid = TagSchema.IsKnownRace(race);
        return true;
    }

    private static bool TryParseNormal(IReadOnlyList<string> parts, out TagSet tags, out bool isValid)
    {
        tags = new TagSet();
        isValid = false;
        if (parts.Count != 4)
        {
            return false;
        }

        var gender = parts[0];
        var role = parts[1];
        var style = TagSchema.NormalizeStyle(parts[2]);
        var race = TagSchema.NormalizeRace(FirstToken(parts[3]));

        if (!TagSchema.IsKnownGender(gender) ||
            !TagSchema.IsKnownRole(role) ||
            role == TagSchema.GenericRole)
        {
            return false;
        }

        tags = new TagSet(gender, role, style, race);
        isValid = TagSchema.IsKnownStyleForRole(role, style) && TagSchema.IsKnownRace(race);
        return true;
    }

    private static string FirstToken(string value)
    {
        return value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault()
            ?? string.Empty;
    }

    [GeneratedRegex(@"\s?\(\d+\)$", RegexOptions.CultureInvariant)]
    private static partial Regex DuplicateSuffixRegex();

    [GeneratedRegex(@"__[0-9a-fA-F]{6,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex HashSuffixRegex();
}
