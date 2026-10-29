using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TokenArtTagger.App;
using TokenArtTagger.App.ViewModels;
using TokenArtTagger.Automation;
using TokenArtTagger.Core;

[assembly: DoNotParallelize]
namespace TokenArtTagger.App.Tests;

[TestClass]
public sealed class AiReviewTests
{
    // A real WPF dispatcher exercises asynchronous continuations and bindings on their owning thread.
    private static Task Sta(Func<Task> test)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                try { await test(); done.SetResult(); }
                catch (Exception ex) { done.SetException(ex); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string? _previous = Environment.GetEnvironmentVariable("TOKENARTTAGGER_DATA_DIR");
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "TokenArtTagger-review-tests", Guid.NewGuid().ToString("N"));
        public MainViewModel Workspace { get; }
        public ImageItemViewModel Image { get; }
        public AiSuggestion Proposal { get; private set; } = null!;
        public Fixture(bool tagged = false)
        {
            Directory.CreateDirectory(Root);
            Environment.SetEnvironmentVariable("TOKENARTTAGGER_DATA_DIR", Path.Combine(Root, "state"));
            var path = Path.Combine(Root, tagged ? "male-range-bow-human.png" : "fixture.png");
            var pixels = new byte[8 * 6 * 4];
            for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = 60; pixels[i + 1] = 120; pixels[i + 2] = 200; pixels[i + 3] = 255; }
            var bitmap = BitmapSource.Create(8, 6, 96, 96, PixelFormats.Bgra32, null, pixels, 32);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var output = File.Create(path)) encoder.Save(output);
            Workspace = new MainViewModel();
            Image = new ImageItemViewModel(ImageItem.FromPath(path));
            Workspace.Items.Add(Image);
        }
        public async Task SeedAsync()
        {
            var hash = await ContentHasher.HashFileHexAsync(Image.FullPath);
            var run = new string('c', 64);
            Proposal = new(AiSuggestion.Identity(Image.FullPath, hash, "fixture", "local-test", run), Image.FullPath,
                hash, new("female", "caster", "wizard", "elf"), "fixture", "local-test", run, "Generated test image; not an accuracy test.",
                FileIdentity.FromPath(Image.FullPath).Key, Image.Item.CurrentTags, DateTimeOffset.UtcNow);
            await AiSuggestionStore.CreateDefault().AddAsync([Proposal]);
        }
        public Task<AiReviewTarget> TargetAsync() => GetTargetAsync();
        private async Task<AiReviewTarget> GetTargetAsync() => (await Workspace.LoadAiReviewsAsync()).Single();
        public void Dispose()
        {
            Environment.SetEnvironmentVariable("TOKENARTTAGGER_DATA_DIR", _previous);
            // Preserve isolated receipts and image fixtures for inspection after a failed test.
        }
    }

    [TestMethod]
    public Task ExplicitFieldSavePersistsAndKeepsSourceBytesAndProposalUnchanged() => Sta(async () =>
    {
        using var f = new Fixture();
        await f.SeedAsync();
        var bytes = await File.ReadAllBytesAsync(f.Image.FullPath);
        await f.Workspace.WithAiReviewAsync(async () =>
        {
            var target = await f.TargetAsync();
            var plan = AiReviewPlan.KeepAll with { Gender = new(AiFieldAction.Suggestion), Race = new(AiFieldAction.Set, "human") };
            await f.Workspace.SaveAiReviewAsync(target, plan, false);
            Assert.AreEqual(new TagSet("female", Race: "human"), f.Image.Item.CurrentTags);
            Assert.AreEqual(f.Image.Item.CurrentTags, (await WorkInProgressTagStore.CreateDefault().LoadAsync()).Single().Tags);
            var receipt = (await f.TargetAsync()).Receipt!;
            Assert.AreEqual("staged", receipt.Outcome);
            Assert.AreEqual(plan, receipt.Decisions);
            Assert.AreEqual(f.Proposal, (await AiSuggestionStore.CreateDefault().LoadAsync()).Single());
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() => f.Workspace.SaveAiReviewAsync(target, plan, false));
        });
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(f.Image.FullPath));
        Assert.IsTrue(f.Workspace.IsWorkspaceAvailable);
    });

    [TestMethod]
    public Task FilenameReplacementNeedsConfirmationAndRoleStyleMustAgree() => Sta(async () =>
    {
        using var f = new Fixture(tagged: true);
        await f.SeedAsync();
        await f.Workspace.WithAiReviewAsync(async () =>
        {
            var target = await f.TargetAsync();
            var role = AiReviewPlan.KeepAll with { Role = new(AiFieldAction.Suggestion) };
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() => f.Workspace.SaveAiReviewAsync(target, role, true));
            var gender = AiReviewPlan.KeepAll with { Gender = new(AiFieldAction.Suggestion) };
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() => f.Workspace.SaveAiReviewAsync(target, gender, false));
            Assert.AreEqual(target.CurrentTags, f.Image.Item.CurrentTags);
            await f.Workspace.SaveAiReviewAsync(target, gender, true);
            Assert.AreEqual(target.CurrentTags with { Gender = "female" }, f.Image.Item.CurrentTags);
            Assert.AreEqual("male", f.Image.Item.ParsedTags.Gender);
        });
    });

    [TestMethod]
    public Task ChangedTagsAndSameMetadataChangedBytesBlockStaging() => Sta(async () =>
    {
        using var f = new Fixture();
        await f.SeedAsync();
        await f.Workspace.WithAiReviewAsync(async () =>
        {
            var target = await f.TargetAsync();
            var plan = AiReviewPlan.KeepAll with { Race = new(AiFieldAction.Suggestion) };
            f.Image.ReplaceItem(f.Image.Item with { CurrentTags = new("male") });
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() => f.Workspace.SaveAiReviewAsync(target, plan, false));
            target = await f.TargetAsync();
            var timestamp = File.GetLastWriteTimeUtc(f.Image.FullPath);
            var bytes = await File.ReadAllBytesAsync(f.Image.FullPath);
            bytes[^1] ^= 1;
            await File.WriteAllBytesAsync(f.Image.FullPath, bytes);
            File.SetLastWriteTimeUtc(f.Image.FullPath, timestamp);
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() => f.Workspace.SaveAiReviewAsync(target, plan, false));
            Assert.AreEqual(new TagSet("male"), f.Image.Item.CurrentTags);
            Assert.IsNull(await AiReviewStore.CreateDefault().LoadAsync(f.Proposal.Id));
        });
    });

    [TestMethod]
    public Task TagPersistenceFailureRollsBackViewModelAndLeavesProposalPending() => Sta(async () =>
    {
        using var f = new Fixture();
        await f.SeedAsync();
        Directory.CreateDirectory(Path.Combine(AppDataPaths.Root, "work-in-progress-tags.json"));
        await f.Workspace.WithAiReviewAsync(async () =>
        {
            var target = await f.TargetAsync();
            var plan = AiReviewPlan.KeepAll with { Gender = new(AiFieldAction.Suggestion) };
            try { await f.Workspace.SaveAiReviewAsync(target, plan, false); Assert.Fail("Save should fail."); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            Assert.AreEqual(new TagSet(), f.Image.Item.CurrentTags);
            Assert.IsNull(await AiReviewStore.CreateDefault().LoadAsync(f.Proposal.Id));
        });
    });

    [TestMethod]
    public Task DismissMissingSourcePersistsAndReviewedFilterCanBeOpenedFromEmptyQueue() => Sta(async () =>
    {
        using var f = new Fixture();
        await f.SeedAsync();
        File.Delete(f.Image.FullPath);
        await f.Workspace.WithAiReviewAsync(async () =>
        {
            var vm = new AiReviewViewModel(f.Workspace);
            await vm.RefreshAsync();
            Assert.AreEqual(1, vm.Proposals.Count);
            await vm.SaveAsync(dismiss: true);
            Assert.AreEqual(0, vm.Proposals.Count);
            Assert.IsTrue(vm.CanNavigate);
            vm.IncludeReviewed = true;
            Assert.AreEqual(1, vm.Proposals.Count);
            Assert.AreEqual("dismissed", vm.Selected!.State);
            Assert.IsFalse(vm.Editable);
            Assert.IsFalse(vm.CanSave);
            Assert.AreEqual(new TagSet(), f.Image.Item.CurrentTags);
            vm.Close();
        });
    });

    [TestMethod]
    public Task ReviewGateBlocksMcpEditsAndReleasesOnException() => Sta(async () =>
    {
        using var f = new Fixture();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Workspace.LoadAiReviewsAsync());
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => f.Workspace.WithAiReviewAsync(async () =>
        {
            var result = await f.Workspace.HandleAgentRequestAsync(new("app_status", AgentJson.Element(new { })), CancellationToken.None);
            Assert.IsTrue(result.IsError);
            Assert.AreEqual("app_busy", AgentJson.Element(result.StructuredContent!).GetProperty("code").GetString());
            throw new InvalidDataException("fixture failure");
        }));
        Assert.IsTrue(f.Workspace.IsWorkspaceAvailable);
    });

    [TestMethod]
    public Task WindowLoadsBindingsAndOnlyExplicitChoicesEnableSave() => Sta(async () =>
    {
        using var f = new Fixture();
        await f.SeedAsync();
        await f.Workspace.WithAiReviewAsync(async () =>
        {
            var vm = new AiReviewViewModel(f.Workspace);
            var window = new AiReviewWindow(vm);
            await vm.RefreshAsync();
            window.Measure(new Size(1000, 640));
            window.Arrange(new Rect(0, 0, 1000, 640));
            window.UpdateLayout();
            var content = (FrameworkElement)window.Content;
            content.Measure(new Size(964, 580));
            content.Arrange(new Rect(0, 0, 964, 580));
            content.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.Background);
            var choices = Descendants<ComboBox>(content).ToArray();
            Assert.AreEqual(4, choices.Length, "All four field templates must render.");
            Assert.IsTrue(choices.All(c => c.ActualWidth > 100 && c.SelectedIndex == 0));
            var save = Descendants<Button>(content).Single(b => Equals(b.Content, "Save reviewed tags"));
            Assert.IsFalse(save.IsEnabled);
            Assert.IsFalse(vm.CanSave);
            Assert.IsTrue(vm.Fields.All(r => r.Selected.Decision.Action == AiFieldAction.Keep));
            choices[0].SelectedIndex = 1;
            for (var i = 0; i < 100 && vm.Preview is null; i++) await Task.Delay(10);
            Assert.IsNotNull(vm.Preview);
            Assert.IsTrue(vm.CanSave);
            Assert.IsTrue(save.IsEnabled);
            await vm.SaveAsync(false);
            Assert.AreEqual(new TagSet("female"), f.Image.Item.CurrentTags);
            Assert.AreEqual(0, vm.Proposals.Count);
            vm.IncludeReviewed = true;
            Assert.IsFalse(vm.CanSave);
            vm.Close();
            window.Close();
        });
    });
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
