namespace TokenArtTagger.Core;

public static class TagSchema
{
    public const string GenderCategory = "gender";
    public const string RoleCategory = "role";
    public const string StyleCategory = "style";
    public const string RaceCategory = "race";
    public const string GenericRole = "generic";

    public static readonly IReadOnlyList<string> Genders = ["male", "female"];
    public static readonly IReadOnlyList<string> Roles = ["melee", "range", "caster", "generic"];
    public static readonly IReadOnlyList<string> MeleeStyles = ["blade", "polearm", "dagger", "axe", "mace", "whip", "unarmed", "rare"];
    public static readonly IReadOnlyList<string> RangeStyles = ["bow", "gun", "crossbow"];
    public static readonly IReadOnlyList<string> CasterStyles = ["wizard", "cleric", "bard"];
    public static readonly IReadOnlyList<string> Races = ["human", "elf", "halfling", "fairy", "beastfolk", "centaur", "dragon", "other"];

    public static IEnumerable<TagButtonDefinition> TagButtons()
    {
        foreach (var gender in Genders)
        {
            yield return new TagButtonDefinition(GenderCategory, gender, "Gender");
        }

        foreach (var role in Roles)
        {
            yield return new TagButtonDefinition(RoleCategory, role, "Role");
        }

        foreach (var style in MeleeStyles)
        {
            yield return new TagButtonDefinition(StyleCategory, style, "Melee Weapon/Style");
        }

        foreach (var style in RangeStyles)
        {
            yield return new TagButtonDefinition(StyleCategory, style, "Range Weapon/Style");
        }

        foreach (var style in CasterStyles)
        {
            yield return new TagButtonDefinition(StyleCategory, style, "Caster Style");
        }

        foreach (var race in Races)
        {
            yield return new TagButtonDefinition(RaceCategory, race, "Race");
        }
    }

    public static bool IsKnownGender(string value) => Contains(Genders, value);

    public static bool IsKnownRole(string value) => Contains(Roles, value);

    public static bool IsKnownRace(string value) => Contains(Races, value);

    public static bool IsKnownStyleForRole(string role, string style)
    {
        return role switch
        {
            "melee" => Contains(MeleeStyles, style),
            "range" => Contains(RangeStyles, style),
            "caster" => Contains(CasterStyles, style),
            _ => false
        };
    }

    private static bool Contains(IEnumerable<string> values, string value)
    {
        return values.Any(candidate => string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed record TagButtonDefinition(string Category, string Value, string Group);
