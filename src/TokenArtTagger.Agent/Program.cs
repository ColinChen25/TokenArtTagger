using System.Text;
using System.Text.Json;
using TokenArtTagger.Automation;

Console.InputEncoding = new UTF8Encoding(false);
Console.OutputEncoding = new UTF8Encoding(false);
int? instance = null;
string? call = null;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--instance" && i + 1 < args.Length && int.TryParse(args[++i], out var pid)) instance = pid;
    else if (args[i] == "--call" && i + 1 < args.Length) call = args[++i];
    else
    {
        Console.Error.WriteLine("Usage: TokenArtTagger.Agent [--instance PID] [--call TOOL]. With --call, supply one JSON arguments object on stdin; otherwise speaks MCP stdio.");
        return 2;
    }
}
var client = new AgentPipeClient(instance);
if (call is not null)
{
    try
    {
        var line = await Console.In.ReadLineAsync();
        using var json = JsonDocument.Parse(string.IsNullOrWhiteSpace(line) ? "{}" : line);
        var result = await client.CallAsync(new AgentRequest(call, json.RootElement));
        Console.WriteLine(JsonSerializer.Serialize(result, AgentJson.Options));
        return result.IsError ? 1 : 0;
    }
    catch (JsonException ex) { Console.Error.WriteLine(ex.Message); return 2; }
}
var server = new McpServer(client.CallAsync);
while (await Console.In.ReadLineAsync() is { } input)
{
    var output = await server.HandleAsync(input);
    if (output is not null) { Console.WriteLine(output); await Console.Out.FlushAsync(); }
}
return 0;
