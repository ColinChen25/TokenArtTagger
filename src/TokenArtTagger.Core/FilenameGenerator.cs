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
        tags = Normalize(tags);
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(tags.Gender))
        {
            errors.Add("gender is required");
        }
        else if (!TagSchema.IsKnownGender(tags.Gender))
        {
            errors.Add($"gender '{tags.Gender}' is not recognized");
        }

        if (string.IsNullOrWhiteSpace(tags.Role))
        {
            errors.Add("role is required");
        }
        else if (!TagSchema.IsKnownRole(tags.Role))
        {
            errors.Add($"role '{tags.Role}' is not recognized");
        }

        if (string.IsNullOrWhiteSpace(tags.Race))
        {
            errors.Add("race is required");
        }
        else if (!TagSchema.IsKnownRace(tags.Race))
        {
            errors.Add($"race '{tags.Race}' is not recognized");
        }

        if (!tags.IsGeneric && string.IsNullOrWhiteSpace(tags.WeaponOrStyle))
        {
            errors.Add("weapon/style is required for melee, range, and caster roles");
        }
        else if (!tags.IsGeneric &&
            !string.IsNullOrWhiteSpace(tags.Role) &&
            !string.IsNullOrWhiteSpace(tags.WeaponOrStyle) &&
            !TagSchema.IsKnownStyleForRole(tags.Role, tags.WeaponOrStyle))
        {
            errors.Add(TagSchema.StyleRequirementMessage(tags.Role));
        }

        return errors;
    }

    private static string Generate(TagSet tags, string fullHashHex, string extension, int hashLength)
    {
        ValidateHash(fullHashHex);
        tags = Normalize(tags);
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

    private static TagSet Normalize(TagSet tags)
    {
        return tags with
        {
            Gender = tags.Gender?.Trim().ToLowerInvariant(),
            Role = tags.Role?.Trim().ToLowerInvariant(),
            WeaponOrStyle = string.IsNullOrWhiteSpace(tags.WeaponOrStyle)
                ? null
                : TagSchema.NormalizeStyle(tags.WeaponOrStyle),
            Race = string.IsNullOrWhiteSpace(tags.Race)
                ? null
                : TagSchema.NormalizeRace(tags.Race)
        };
    }
}
