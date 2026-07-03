namespace TokenArtTagger.Core;

public sealed record TagSet(
    string? Gender = null,
    string? Role = null,
    string? WeaponOrStyle = null,
    string? Race = null)
{
    public bool IsGeneric => string.Equals(Role, TagSchema.GenericRole, StringComparison.OrdinalIgnoreCase);

    public TagSet WithTag(string category, string value)
    {
        var normalizedCategory = category.Trim().ToLowerInvariant();
        var normalizedValue = value.Trim().ToLowerInvariant();

        return normalizedCategory switch
        {
            TagSchema.GenderCategory => this with { Gender = normalizedValue },
            TagSchema.RoleCategory => WithRole(normalizedValue),
            TagSchema.StyleCategory => this with { WeaponOrStyle = normalizedValue },
            TagSchema.RaceCategory => this with { Race = TagSchema.NormalizeRace(normalizedValue) },
            _ => this
        };
    }

    private TagSet WithRole(string role)
    {
        if (role == TagSchema.GenericRole)
        {
            return this with { Role = role, WeaponOrStyle = null };
        }

        if (!string.IsNullOrWhiteSpace(WeaponOrStyle) &&
            !TagSchema.IsKnownStyleForRole(role, WeaponOrStyle))
        {
            return this with { Role = role, WeaponOrStyle = null };
        }

        return this with { Role = role };
    }
}
