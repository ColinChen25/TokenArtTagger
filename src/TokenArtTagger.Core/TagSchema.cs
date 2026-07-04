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
    public static readonly IReadOnlyList<string> MeleeStyles = ["blade", "polearm", "dagger", "axe", "mace", "whip", "unarmed", "flail", "scythe", "blunt", "exotic", "rare"];
    public static readonly IReadOnlyList<string> RangeStyles = ["bow", "gun", "crossbow", "thrown"];
    public static readonly IReadOnlyList<string> CasterStyles = ["wizard", "cleric", "bard", "druid"];
    public static readonly IReadOnlyList<string> Races =
    [
        "human",
        "elf",
        "halfling",
        "fairy",
        "beastfolk",
        "centaur",
        "dragon",
        "dwarf",
        "grippli",
        "oread",
        "mermaid",
        "construct",
        "chimera",
        "tiefling",
        "aasimar",
        "vampire",
        "demon",
        "kitsune",
        "elemental",
        "other"
    ];

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

    public static string NormalizeRace(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "gripple" => "grippli",
            "water" => "elemental",
            "mecha" or "robot" or "golem" or "android" => "construct",
            var normalized => normalized
        };
    }

    public static string NormalizeStyle(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "scyth" => "scythe",
            "stick" or "baton" or "tonfa" => "blunt",
            "starknife" => "thrown",
            "drill" or "chainsaw" => "exotic",
            "kama" => "dagger",
            var normalized => normalized
        };
    }

    public static bool IsKnownStyleForRole(string role, string style)
    {
        var normalizedStyle = NormalizeStyle(style);
        return role switch
        {
            "melee" => Contains(MeleeStyles, normalizedStyle),
            "range" => Contains(RangeStyles, normalizedStyle),
            "caster" => Contains(CasterStyles, normalizedStyle),
            _ => false
        };
    }

    public static IReadOnlyList<string> StylesForRole(string role)
    {
        return role switch
        {
            "melee" => MeleeStyles,
            "range" => RangeStyles,
            "caster" => CasterStyles,
            _ => []
        };
    }

    public static string StyleRequirementMessage(string role)
    {
        var styles = StylesForRole(role);
        return styles.Count == 0
            ? string.Empty
            : $"{role} requires {string.Join(", ", styles.Take(styles.Count - 1))}{(styles.Count > 1 ? ", or " : string.Empty)}{styles[^1]}";
    }

    private static bool Contains(IEnumerable<string> values, string value)
    {
        return values.Any(candidate => string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed record TagButtonDefinition(string Category, string Value, string Group);
