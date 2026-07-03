namespace TokenArtTagger.Core;

public static class FilenameGenerator
{
    public const int DefaultHashLength = 6;

    public static string Generate(TagSet tags, string fullHashHex, string extension)
    {
        return Generate(tags, fullHashHex, extension, DefaultHashLength);
    }

    public static string GenerateConflictSafe(
        TagSet tags,
        string fullHashHex,
        string extension,
        ISet<string> existingFileNames)
    {
        ValidateHash(fullHashHex);

        for (var length = DefaultHashLength; length <= fullHashHex.Length; length++)
        {
            var candidate = Generate(tags, fullHashHex, extension, length);
            if (!existingFileNames.Contains(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("A filename conflict exists even with the full content hash.");
    }

    public static IReadOnlyList<string> Validate(TagSet tags)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(tags.Gender))
        {
            errors.Add("gender is required");
        }

        if (string.IsNullOrWhiteSpace(tags.Role))
        {
            errors.Add("role is required");
        }

        if (string.IsNullOrWhiteSpace(tags.Race))
        {
            errors.Add("race is required");
        }

        if (!tags.IsGeneric && string.IsNullOrWhiteSpace(tags.WeaponOrStyle))
        {
            errors.Add("weapon/style is required for melee, range, and caster roles");
        }

        return errors;
    }

    private static string Generate(TagSet tags, string fullHashHex, string extension, int hashLength)
    {
        ValidateHash(fullHashHex);
        var errors = Validate(tags);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join("; ", errors));
        }

        var hash = fullHashHex[..Math.Min(hashLength, fullHashHex.Length)].ToLowerInvariant();
        var normalizedExtension = extension.StartsWith('.') ? extension : $".{extension}";

        if (tags.IsGeneric)
        {
            return $"{tags.Gender}-{TagSchema.GenericRole}-{tags.Race}__{hash}{normalizedExtension}";
        }

        return $"{tags.Gender}-{tags.Role}-{tags.WeaponOrStyle}-{tags.Race}__{hash}{normalizedExtension}";
    }

    private static void ValidateHash(string fullHashHex)
    {
        if (fullHashHex.Length < DefaultHashLength)
        {
            throw new ArgumentException("Hash must be at least 6 hex characters.", nameof(fullHashHex));
        }
    }
}
