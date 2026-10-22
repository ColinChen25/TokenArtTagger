using System.Text.Json;
using TokenArtTagger.Automation;
using TokenArtTagger.Core;

namespace TokenArtTagger.Tests;

[TestClass]
public sealed class AgentProtocolTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [TestMethod]
    public async Task Mcp_InitializesAndListsToolsWithoutDesktop()
    {
        var server = new McpServer((_, _) => throw new AssertFailedException("Must not contact desktop."));
        var init = Json((await server.HandleAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18"}}"""))!);
        Assert.AreEqual("2025-06-18", init.GetProperty("result").GetProperty("protocolVersion").GetString());
        Assert.IsNull(await server.HandleAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}"""));
        var tools = Json((await server.HandleAsync("""{"jsonrpc":"2.0","id":"tools","method":"tools/list"}"""))!);
        Assert.AreEqual(11, tools.GetProperty("result").GetProperty("tools").GetArrayLength());
        Assert.AreEqual("tools", tools.GetProperty("id").GetString());
    }

    [TestMethod]
    public async Task Mcp_RecoversFromMalformedInputAndReturnsToolValidationErrors()
    {
        var called = false;
        var server = new McpServer((_, _) => { called = true; return Task.FromResult(AgentResult.Data(new { ok = true })); });
        Assert.AreEqual(-32700, Json((await server.HandleAsync("{"))!).GetProperty("error").GetProperty("code").GetInt32());
        await server.HandleAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25"}}""");
        var invalid = Json((await server.HandleAsync("""{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"update_tags","arguments":{"changes":[]}}}"""))!);
        Assert.IsTrue(invalid.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.IsFalse(called);
        var result = Json((await server.HandleAsync("""{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"app_status","arguments":{}}}"""))!);
        Assert.IsTrue(result.GetProperty("result").GetProperty("structuredContent").GetProperty("ok").GetBoolean());
    }

    [TestMethod]
    public void Schema_RejectsUnknownAndMissingFieldsAndExcessiveImageRequests()
    {
        Assert.Throws<AgentException>(() => AgentToolCatalog.ValidateArguments("view_image", Json("""{"id":"x","expectedRevision":"r","maxDimension":99999}""")));
        Assert.Throws<AgentException>(() => AgentToolCatalog.ValidateArguments("app_status", Json("""{"rename":true}""")));
        Assert.Throws<AgentException>(() => AgentToolCatalog.ValidateArguments("update_tags", Json("""{"changes":[{"id":"x","expectedRevision":"r","tags":{"gender":"female"}}]}""")));
        Assert.Throws<AgentException>(() => AgentToolCatalog.ValidateArguments("list_images", Json("""{"limit":1,"limit":2}""")));
    }

    [TestMethod]
    public void TagRules_AllowPartialTagsButRejectContradictions()
    {
        var partial = AgentTagRules.ReadReplacement(Json("""{"gender":"female","role":"caster","weaponOrStyle":null,"race":null}"""));
        Assert.AreEqual("caster", partial.Role);
        Assert.Throws<AgentException>(() => AgentTagRules.ReadReplacement(Json("""{"gender":"female","role":"generic","weaponOrStyle":"bow","race":"elf"}""")));
        Assert.Throws<AgentException>(() => AgentTagRules.ReadReplacement(Json("""{"gender":"female","role":"range","weaponOrStyle":"wizard","race":"elf"}""")));
        Assert.Throws<AgentException>(() => AgentTagRules.ReadReplacement(Json("""{"gender":null,"role":null,"weaponOrStyle":"blade","race":null}""")));
    }

    [TestMethod]
    public async Task PipeFrames_RejectOversizedFramesAndRoundTripUnicode()
    {
        using var stream = new MemoryStream();
        await PipeFrames.WriteAsync(stream, "{\"name\":\"精灵\"}", default);
        stream.Position = 0;
        Assert.AreEqual("{\"name\":\"精灵\"}", await PipeFrames.ReadAsync(stream, 100, default));
        stream.Position = 0;
        await Assert.ThrowsAsync<IOException>(() => PipeFrames.ReadAsync(stream, 1, default));
    }

    [TestMethod]
    public async Task Scan_ExcludesNestedTaggedFolderWithoutExcludingSibling()
    {
        using var temp = new TempFolder();
        Directory.CreateDirectory(Path.Combine(temp.Path, "Sorted"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "Sorted-extra"));
        File.WriteAllBytes(Path.Combine(temp.Path, "one.png"), [1]);
        File.WriteAllBytes(Path.Combine(temp.Path, "Sorted", "two.png"), [2]);
        File.WriteAllBytes(Path.Combine(temp.Path, "Sorted-extra", "three.png"), [3]);
        var scan = await FileScanner.ScanAsync(temp.Path, true, [Path.Combine(temp.Path, "Sorted") + Path.DirectorySeparatorChar]);
        Assert.AreEqual(2, scan.Items.Count);
        Assert.IsFalse(scan.Items.Any(i => i.FileName == "two.png"));
        Assert.AreEqual(1, (await FileScanner.ScanAsync(temp.Path, false, [])).Items.Count);
    }

    [TestMethod]
    public async Task Wip_MergingAndResettingOneFolderPreservesOtherFolder()
    {
        using var temp = new TempFolder();
        var first = Path.Combine(temp.Path, "first.png");
        var second = Path.Combine(temp.Path, "second.png");
        File.WriteAllBytes(first, [1]); File.WriteAllBytes(second, [2]);
        var store = new WorkInProgressTagStore(Path.Combine(temp.Path, "wip.json"));
        await store.MergeAsync([new(FileIdentity.FromPath(first).Key, new TagSet("female"))], [first]);
        await store.MergeAsync([new(FileIdentity.FromPath(second).Key, new TagSet("male"))], [second]);
        Assert.AreEqual(2, (await store.LoadAsync()).Count);
        await store.MergeAsync([], [first]);
        var retained = await store.LoadAsync();
        Assert.AreEqual(1, retained.Count);
        Assert.AreEqual(FileIdentity.FromPath(second).Key, retained[0].IdentityKey);
        Assert.AreEqual("male", retained[0].Tags.Gender);
    }

    [TestMethod]
    public async Task Wip_CancelledWriteDoesNotDestroyPreviousData()
    {
        using var temp = new TempFolder();
        var store = new WorkInProgressTagStore(Path.Combine(temp.Path, "wip.json"));
        await store.SaveAsync([new("old", new TagSet("female"))]);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => store.SaveAsync([], cancelled.Token));
        Assert.AreEqual("old", (await store.LoadAsync())[0].IdentityKey);
    }
}
