using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace TokenArtTagger.Automation;

public sealed record AgentInstance(int ProcessId, long StartedUtcTicks, string PipeName);

public static class AgentDiscovery
{
    public static string Folder => Path.Combine(TokenArtTagger.Core.AppDataPaths.Root, "Agent", "instances");
    public static IReadOnlyList<AgentInstance> List()
    {
        var instances = new List<AgentInstance>();
        if (!Directory.Exists(Folder)) return instances;
        foreach (var file in Directory.EnumerateFiles(Folder, "*.json"))
        {
            try
            {
                var instance = JsonSerializer.Deserialize<AgentInstance>(File.ReadAllText(file), AgentJson.Options);
                if (instance is null) continue;
                using var process = Process.GetProcessById(instance.ProcessId);
                if (!process.HasExited && process.StartTime.ToUniversalTime().Ticks == instance.StartedUtcTicks) instances.Add(instance);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
        return instances.OrderBy(i => i.ProcessId).ToArray();
    }
}

public static class PipeFrames
{
    public static async Task WriteAsync(Stream stream, string json, CancellationToken token)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        if (bytes.Length > 16 * 1024 * 1024) throw new IOException("Agent response is too large.");
        await stream.WriteAsync(BitConverter.GetBytes(bytes.Length), token);
        await stream.WriteAsync(bytes, token);
        await stream.FlushAsync(token);
    }
    public static async Task<string> ReadAsync(Stream stream, int maxBytes, CancellationToken token)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, token);
        var size = BitConverter.ToInt32(header);
        if (size < 1 || size > maxBytes) throw new IOException("Invalid agent message size.");
        var buffer = new byte[size];
        await stream.ReadExactlyAsync(buffer, token);
        return Encoding.UTF8.GetString(buffer);
    }
}

// Only the same Windows user can connect. One request is processed at a time, on the app dispatcher.
public sealed class AgentPipeServer : IDisposable
{
    private readonly CancellationTokenSource _shutdown = new();
    private readonly string _registration;
    private readonly string _pipeName;
    private readonly Func<AgentRequest, CancellationToken, Task<AgentResult>> _handler;
    public Task Completion { get; }

    public AgentPipeServer(Func<AgentRequest, CancellationToken, Task<AgentResult>> handler)
    {
        _handler = handler;
        _pipeName = $"TokenArtTagger.Agent.v1.{Environment.ProcessId}.{Guid.NewGuid():N}";
        Directory.CreateDirectory(AgentDiscovery.Folder);
        _registration = Path.Combine(AgentDiscovery.Folder, $"{Environment.ProcessId}.json");
        using var process = Process.GetCurrentProcess();
        File.WriteAllText(_registration, JsonSerializer.Serialize(new AgentInstance(Environment.ProcessId, process.StartTime.ToUniversalTime().Ticks, _pipeName), AgentJson.Options));
        Completion = Task.Run(RunAsync);
    }
    private async Task RunAsync()
    {
        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                await using var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_shutdown.Token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
                timeout.CancelAfter(TimeSpan.FromMinutes(2));
                try
                {
                    AgentResult result;
                    try
                    {
                        var json = await PipeFrames.ReadAsync(pipe, 256 * 1024, timeout.Token);
                        var request = JsonSerializer.Deserialize<AgentRequest>(json, AgentJson.Options) ?? throw new JsonException();
                        if (!AgentToolCatalog.Names.Contains(request.Name)) throw new AgentException("unknown_tool", "Unknown agent tool.");
                        AgentToolCatalog.ValidateArguments(request.Name, request.Arguments);
                        result = await _handler(request, timeout.Token);
                    }
                    catch (AgentException ex) { result = AgentResult.Error(ex.Code, ex.Message); }
                    catch (OperationCanceledException) { result = AgentResult.Error("cancelled", "Operation cancelled or timed out. Re-read state before retrying."); }
                    catch (Exception ex) { result = AgentResult.Error("operation_failed", ex.Message); }
                    await PipeFrames.WriteAsync(pipe, JsonSerializer.Serialize(result, AgentJson.Options), timeout.Token);
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException) { }
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        finally { TryRemoveRegistration(); }
    }
    private void TryRemoveRegistration()
    {
        try { File.Delete(_registration); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
    public void Dispose()
    {
        _shutdown.Cancel();
        TryRemoveRegistration();
    }
}

public sealed class AgentPipeClient(int? instanceId = null)
{
    public async Task<AgentResult> CallAsync(AgentRequest request, CancellationToken token = default)
    {
        if (!AgentToolCatalog.Names.Contains(request.Name)) return AgentResult.Error("unknown_tool", request.Name);
        try { AgentToolCatalog.ValidateArguments(request.Name, request.Arguments); }
        catch (AgentException ex) { return AgentResult.Error(ex.Code, ex.Message); }
        if (request.Name == "agent_instances") return AgentResult.Data(new { instances = AgentDiscovery.List() });
        if (request.Name == "tag_schema") return AgentResult.Data(AgentToolCatalog.Schema);
        var instances = AgentDiscovery.List().Where(i => instanceId is null || i.ProcessId == instanceId).ToArray();
        if (instances.Length == 0) return AgentResult.Error("app_not_running", "Open TokenArtTagger under the same Windows account with agent access enabled.");
        if (instances.Length != 1) return AgentResult.Error("ambiguous_instance", "Multiple apps are open. Use --instance <processId> from agent_instances.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            await using var pipe = new NamedPipeClientStream(".", instances[0].PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(3000, timeout.Token);
            await PipeFrames.WriteAsync(pipe, JsonSerializer.Serialize(request, AgentJson.Options), timeout.Token);
            return JsonSerializer.Deserialize<AgentResult>(await PipeFrames.ReadAsync(pipe, 16 * 1024 * 1024, timeout.Token), AgentJson.Options)!;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException or UnauthorizedAccessException)
        {
            return AgentResult.Error("connection_failed", "Desktop connection failed or timed out. Re-read app state before retrying any write. " + ex.Message);
        }
    }
}
