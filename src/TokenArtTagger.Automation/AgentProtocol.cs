using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TokenArtTagger.Core;

namespace TokenArtTagger.Automation;

public static class AgentJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static JsonElement Element(object value) => JsonSerializer.SerializeToElement(value, Options);
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
public sealed record AgentRequest(string Name, JsonElement Arguments);
public sealed record AgentResult(object[] Content,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] object? StructuredContent = null, bool IsError = false)
{
    public static AgentResult Data(object data) => new([new { type = "text", text = JsonSerializer.Serialize(data, AgentJson.Options) }], data);
    public static AgentResult Error(string code, string message) => new([new { type = "text", text = $"{code}: {message}" }], new { code, message }, true);
}
public sealed class AgentException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
public static class AgentTagRules
{
    public static TagSet ReadReplacement(JsonElement json)
    {
        string? Read(string field)
        {
            var value = json.GetProperty(field);
            if (value.ValueKind == JsonValueKind.Null) return null;
            if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
                throw new AgentException("invalid_tags", $"{field} must be a tag string or null.");
            return value.GetString()!.Trim().ToLowerInvariant();
        }
        var tags = new TagSet(Read("gender"), Read("role"), Read("weaponOrStyle"), Read("race"));
        if (tags.Gender is not null && !TagSchema.IsKnownGender(tags.Gender) ||
            tags.Role is not null && !TagSchema.IsKnownRole(tags.Role) ||
            tags.Race is not null && !TagSchema.IsKnownRace(tags.Race) ||
            tags.WeaponOrStyle is not null && (tags.Role is null || !TagSchema.IsKnownStyleForRole(tags.Role, tags.WeaponOrStyle)))
            throw new AgentException("invalid_tags", "Unknown tag or incompatible role/style. Read tag_schema for allowed values.");
        return tags with { WeaponOrStyle = tags.WeaponOrStyle is null ? null : TagSchema.NormalizeStyle(tags.WeaponOrStyle) };
    }
}
public static class AgentArguments
{
    public static string RequiredString(this JsonElement value, string key) =>
        value.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(field.GetString())
            ? field.GetString()! : throw new AgentException("invalid_arguments", $"{key} must be a nonempty string.");
    public static string? OptionalString(this JsonElement value, string key) => value.TryGetProperty(key, out _) ? value.RequiredString(key) : null;
    public static bool Boolean(this JsonElement value, string key, bool fallback = false) => value.TryGetProperty(key, out var field) ? field.GetBoolean() : fallback;
    public static int Integer(this JsonElement value, string key, int fallback) => value.TryGetProperty(key, out var field) ? field.GetInt32() : fallback;
}
