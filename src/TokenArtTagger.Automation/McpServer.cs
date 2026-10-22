using System.Text.Json;

namespace TokenArtTagger.Automation;

// A tools-only stdio implementation. No HTTP listener, model dependency, or package restore at startup.
public sealed class McpServer(Func<AgentRequest, CancellationToken, Task<AgentResult>> invoke)
{
    private bool _initialized;
    public async Task<string?> HandleAsync(string line, CancellationToken token = default)
    {
        JsonElement id = default;
        object Error(int code, string message) => new { jsonrpc = "2.0", id = id.ValueKind == JsonValueKind.Undefined ? (object?)null : id, error = new { code, message } };
        try
        {
            if (line.Length > 256 * 1024) return JsonSerializer.Serialize(Error(-32600, "Request too large."));
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return JsonSerializer.Serialize(Error(-32600, "Request must be an object."));
            var hasId = root.TryGetProperty("id", out id);
            if (hasId) id = id.Clone();
            if (!root.TryGetProperty("jsonrpc", out var version) || version.GetString() != "2.0" || !root.TryGetProperty("method", out var method) || method.ValueKind != JsonValueKind.String)
                return JsonSerializer.Serialize(Error(-32600, "Invalid JSON-RPC request."));
            if (!hasId) return null; // MCP notifications never receive responses.
            if (id.ValueKind is not (JsonValueKind.String or JsonValueKind.Number)) return JsonSerializer.Serialize(Error(-32600, "Invalid request ID."));
            var parameters = root.TryGetProperty("params", out var supplied) ? supplied : AgentJson.Element(new { });
            object result;
            switch (method.GetString())
            {
                case "initialize":
                    var proposed = parameters.RequiredString("protocolVersion");
                    var negotiated = proposed is "2025-06-18" or "2025-11-25" ? proposed : "2025-11-25";
                    _initialized = true;
                    result = new { protocolVersion = negotiated, capabilities = new { tools = new { listChanged = false } }, serverInfo = new { name = "token-art-tagger", version = "1.0.0" }, instructions = "Use app_status, scan_images, list_images, view_image, and revision-checked update_tags. Rename confirmation stays in the desktop app." };
                    break;
                case "ping": result = new { }; break;
                case "tools/list" when _initialized: result = new { tools = AgentToolCatalog.Tools }; break;
                case "tools/call" when _initialized:
                    var name = parameters.RequiredString("name");
                    if (!AgentToolCatalog.Names.Contains(name)) return JsonSerializer.Serialize(Error(-32602, "Unknown tool."));
                    var arguments = parameters.TryGetProperty("arguments", out var a) ? a : AgentJson.Element(new { });
                    try
                    {
                        AgentToolCatalog.ValidateArguments(name, arguments);
                        result = await invoke(new AgentRequest(name, arguments), token);
                    }
                    catch (AgentException ex) { result = AgentResult.Error(ex.Code, ex.Message); }
                    catch (Exception ex) { result = AgentResult.Error("operation_failed", ex.Message); }
                    break;
                default: return JsonSerializer.Serialize(Error(_initialized ? -32601 : -32002, _initialized ? "Method not supported." : "Initialize first."));
            }
            return JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result }, AgentJson.Options);
        }
        catch (JsonException) { return JsonSerializer.Serialize(Error(-32700, "Parse error.")); }
        catch (Exception ex) when (ex is AgentException or InvalidOperationException or KeyNotFoundException)
        { return JsonSerializer.Serialize(Error(-32602, ex.Message)); }
    }
}
