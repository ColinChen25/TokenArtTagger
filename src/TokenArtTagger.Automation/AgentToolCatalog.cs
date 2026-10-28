using System.Text.Json;
using TokenArtTagger.Core;

namespace TokenArtTagger.Automation;

public static class AgentToolCatalog
{
    private static object String(string description) => new { type = "string", description };
    private static object Integer(int min, int max) => new { type = "integer", minimum = min, maximum = max };
    private static object Object(Dictionary<string, object> properties, params string[] required) => new { type = "object", properties, required, additionalProperties = false };
    private static object Array(object items) => new { type = "array", items, minItems = 1, maxItems = 100 };
    private static object Target() => Object(new() { ["id"] = String("Image ID."), ["expectedRevision"] = String("Current image revision.") }, "id", "expectedRevision");
    private static object Tool(string name, string description, object schema, bool readOnly) => new
    {
        name, description, inputSchema = schema,
        annotations = new { readOnlyHint = readOnly, destructiveHint = false, idempotentHint = readOnly, openWorldHint = false }
    };
    private static object NullableString() => new { type = new[] { "string", "null" } };
    public static readonly object[] Tools =
    [
        Tool("agent_instances", "List running desktop instances. For multiple instances, launch this bridge with --instance <processId>.", Object(new()), true),
        Tool("tag_schema", "Read allowed tags and role/style dependencies. Works even if the desktop app is closed.", Object(new()), true),
        Tool("app_status", "Read live workspace revision, root, counts, tag distributions, scan errors and pending changes.", Object(new()), true),
        Tool("scan_images", "Scan a folder into the live app, saving current temporary tags first. Exclude nested tagged folders explicitly when needed. Scan itself reads filenames, not image bytes.", Object(new()
        {
            ["root"] = String("Absolute image folder."), ["expectedWorkspaceRevision"] = String("Revision from app_status."),
            ["recursive"] = new { type = "boolean" }, ["excludeDirectories"] = new { type = "array", items = String("Absolute subfolder to exclude with descendants."), maxItems = 100 }
        }, "root", "expectedWorkspaceRevision"), false),
        Tool("list_images", "Page images with IDs, revisions, current tags and filename tags. Tagged/untagged refer to recognized filenames. Does not change UI filtering or selection.", Object(new()
        {
            ["offset"] = Integer(0, int.MaxValue), ["limit"] = Integer(1, 100), ["query"] = String("Filename/tag substring."),
            ["folder"] = String("Absolute folder within scanned root."), ["recursive"] = new { type = "boolean" },
            ["state"] = new { type = "string", @enum = new[] { "all", "tagged", "untagged", "changed", "incomplete", "invalid" } },
            ["expectedWorkspaceRevision"] = String("Optional consistency check for pagination.")
        }), true),
        Tool("image_details", "Read image details and optionally full SHA-256. includeHash reads original bytes.", Object(new()
        { ["id"] = String("Image ID."), ["includeHash"] = new { type = "boolean" } }, "id"), true),
        Tool("view_image", "Return a bounded PNG to the calling agent/model. First frame only; Windows codecs determine WebP support. Requires current image revision.", Object(new()
        { ["id"] = String("Image ID."), ["expectedRevision"] = String("Current revision."), ["maxDimension"] = Integer(256, 1536) }, "id", "expectedRevision"), true),
        Tool("queue_ai_suggestions", "Persist review-only AI proposals separately from temporary tags. Verifies current revisions and full source hashes for the entire batch. Does not apply tags or rename. Provider/model provenance is supplied by the caller, not authenticated by this tool.", Object(new()
        {
            ["suggestions"] = Array(Object(new()
            {
                ["id"] = String("Image ID."), ["expectedRevision"] = String("Current image revision."),
                ["sha256"] = String("Full lowercase SHA-256 of the source bytes used for prediction."),
                ["tags"] = Object(new() { ["gender"] = NullableString(), ["role"] = NullableString(), ["weaponOrStyle"] = NullableString(), ["race"] = NullableString() }, "gender", "role", "weaponOrStyle", "race"),
                ["provider"] = String("Local provider name; at most 80 characters."),
                ["model"] = String("Model/version identity; at most 256 characters."),
                ["runFingerprint"] = String("Full lowercase SHA-256 identifying the inference settings or saved run."),
                ["evidence"] = String("Short visible evidence, at most 2000 characters; plain text only.")
            }, "id", "expectedRevision", "sha256", "tags", "provider", "model", "runFingerprint"))
        }, "suggestions"), false),
        Tool("list_ai_suggestions", "Page saved review-only proposals for images in the current scan. Includes live image revisions and import snapshots. Listing does not rehash source bytes; metadata equality does not establish content equality. No tag changes.", Object(new()
        {
            ["offset"] = Integer(0, int.MaxValue), ["limit"] = Integer(1, 100), ["imageId"] = String("Optional current image ID."),
            ["expectedWorkspaceRevision"] = String("Optional consistency check for the current scan."),
            ["expectedQueueRevision"] = String("Optional consistency check from the previous queue page.")
        }), true),
        Tool("update_tags", "Replace temporary tags in one validated batch (max 100). All four fields required; null means unresolved. Stale revisions reject the whole batch. Does not rename files.", Object(new()
        {
            ["changes"] = Array(Object(new()
            {
                ["id"] = String("Image ID."), ["expectedRevision"] = String("Current revision."),
                ["tags"] = Object(new() { ["gender"] = NullableString(), ["role"] = NullableString(), ["weaponOrStyle"] = NullableString(), ["race"] = NullableString() }, "gender", "role", "weaponOrStyle", "race")
            }, "id", "expectedRevision", "tags"))
        }, "changes"), false),
        Tool("reset_tags", "Restore filename tags for explicit targets, requiring current revisions. No file renames.", Object(new() { ["targets"] = Array(Target()) }, "targets"), false),
        Tool("select_images", "Select targets in the desktop Library tab. Requires current revisions.", Object(new() { ["targets"] = Array(Target()) }, "targets"), false),
        Tool("preview_rename", "Select targets and populate the desktop rename preview. Reads content hashes. User confirms Rename Selected in the app; this tool never renames files.", Object(new() { ["targets"] = Array(Target()) }, "targets"), false)
    ];
    public static readonly HashSet<string> Names = Tools.Select(t => AgentJson.Element(t).GetProperty("name").GetString()!).ToHashSet();
    public static object Schema => new
    {
        genders = TagSchema.Genders, roles = TagSchema.Roles, races = TagSchema.Races,
        stylesByRole = TagSchema.Roles.ToDictionary(r => r, TagSchema.StylesForRole),
        unresolved = "Use null. generic requires null weaponOrStyle. Incomplete tags cannot be renamed."
    };
    public static void ValidateArguments(string name, JsonElement arguments)
    {
        var tool = Tools.Select(AgentJson.Element).Single(t => t.GetProperty("name").GetString() == name);
        Validate(tool.GetProperty("inputSchema"), arguments, "arguments");
    }
    private static void Validate(JsonElement schema, JsonElement value, string path)
    {
        var type = schema.GetProperty("type");
        var types = type.ValueKind == JsonValueKind.Array ? type.EnumerateArray().Select(t => t.GetString()).ToArray() : [type.GetString()];
        var actual = value.ValueKind switch { JsonValueKind.Object => "object", JsonValueKind.Array => "array", JsonValueKind.String => "string", JsonValueKind.Null => "null", JsonValueKind.True or JsonValueKind.False => "boolean", JsonValueKind.Number => "integer", _ => "invalid" };
        if (!types.Contains(actual)) throw new AgentException("invalid_arguments", $"Invalid type for {path}.");
        if (actual == "object")
        {
            var properties = schema.GetProperty("properties");
            foreach (var required in schema.GetProperty("required").EnumerateArray())
                if (!value.TryGetProperty(required.GetString()!, out _)) throw new AgentException("invalid_arguments", $"Missing {path}.{required.GetString()}.");
            var seen = new HashSet<string>();
            foreach (var property in value.EnumerateObject())
            {
                if (!seen.Add(property.Name) || !properties.TryGetProperty(property.Name, out var child)) throw new AgentException("invalid_arguments", $"Unknown or duplicate {path}.{property.Name}.");
                Validate(child, property.Value, $"{path}.{property.Name}");
            }
        }
        if (actual == "array")
        {
            if (schema.TryGetProperty("minItems", out var min) && value.GetArrayLength() < min.GetInt32() || schema.TryGetProperty("maxItems", out var max) && value.GetArrayLength() > max.GetInt32())
                throw new AgentException("invalid_arguments", $"Invalid number of entries in {path}.");
            foreach (var item in value.EnumerateArray()) Validate(schema.GetProperty("items"), item, path + "[]");
        }
        if (actual == "integer" && (!value.TryGetInt32(out var number) || number < schema.GetProperty("minimum").GetInt32() || number > schema.GetProperty("maximum").GetInt32()))
            throw new AgentException("invalid_arguments", $"Invalid number for {path}.");
        if (schema.TryGetProperty("enum", out var choices) && !choices.EnumerateArray().Any(c => c.GetString() == value.GetString()))
            throw new AgentException("invalid_arguments", $"Unsupported value for {path}.");
    }
}
