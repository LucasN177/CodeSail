using System.Net;
using Bunit;
using CodeSail.Components;
using CodeSail.Models;
using CodeSail.Services;
using CodeSail.Services.GitHub;

namespace CodeSail.Tests;

public class MergeServiceTests
{
    private readonly FakeGitHubService _gitHub = new();
    private readonly FakeFileCache _cache = new();
    private readonly WorkspaceService _workspace;
    private readonly MergeService _merge;
    private readonly CommitService _commits;

    private const string Original = "eins\nzwei\ndrei\nvier\nfünf\nsechs";

    public MergeServiceTests()
    {
        _gitHub.Push("main", new() { ["a.txt"] = Original });
        _workspace = new WorkspaceService(_gitHub, _cache);
        _merge = new MergeService(_gitHub, _cache);
        _commits = new CommitService(_gitHub, _cache);
    }

    private async Task<CachedFile> EditLocally(string content) =>
        await _workspace.SaveContentAsync(await _workspace.OpenFileAsync("o", "r", "main", "a.txt", GitBlobSha.Compute(Original)), content);

    [Fact]
    public async Task Resolve_CombinesSeparateChangesAndRebasesOnGitHub()
    {
        var local = await EditLocally("EINS\nzwei\ndrei\nvier\nfünf\nsechs");
        _gitHub.Push("main", new() { ["a.txt"] = "eins\nzwei\ndrei\nvier\nfünf\nSECHS" });

        var resolution = await _merge.ResolveAsync("o", "r", "main", [local]);

        var merged = Assert.Single(resolution.AutoMerged);
        Assert.Equal("EINS\nzwei\ndrei\nvier\nfünf\nSECHS", merged.Content);
        Assert.Equal(GitBlobSha.Compute("eins\nzwei\ndrei\nvier\nfünf\nSECHS"), merged.BlobSha);

        // Danach lässt sich ohne Konflikt committen, und beide Änderungen landen auf GitHub
        var result = await _commits.CommitAsync(new CommitRequest("o", "r", "main", "Zusammengeführt",
            await _workspace.GetChangesAsync("o", "r", "main")));
        Assert.True(result.Succeeded);
        Assert.Equal("EINS\nzwei\ndrei\nvier\nfünf\nSECHS", _gitHub.Commits[_gitHub.Heads["main"]]["a.txt"].Content);
    }

    [Fact]
    public async Task Resolve_MarksOverlappingChangesAndCommitIsRefusedUntilCleaned()
    {
        var local = await EditLocally("eins\nzwei (meins)\ndrei\nvier\nfünf\nsechs");
        _gitHub.Push("main", new() { ["a.txt"] = "eins\nzwei (GitHub)\ndrei\nvier\nfünf\nsechs" });

        var resolution = await _merge.ResolveAsync("o", "r", "main", [local]);

        var marked = Assert.Single(resolution.WithConflicts);
        Assert.Contains("<<<<<<< Deine Änderung", marked.Content);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _commits.CommitAsync(new CommitRequest("o", "r", "main", "x", [marked])));
        Assert.Contains("a.txt", error.Message);
    }

    [Fact]
    public async Task Resolve_LeavesFilesDeletedOnGitHubUnresolved()
    {
        var local = await EditLocally("anders");
        var head = _gitHub.Heads["main"];
        _gitHub.Commits["leer"] = [];
        _gitHub.Heads["main"] = "leer";

        var resolution = await _merge.ResolveAsync("o", "r", "main", [local]);

        Assert.Equal(["a.txt"], resolution.Unresolved);
        Assert.NotEqual(head, _gitHub.Heads["main"]);
    }

    [Fact]
    public async Task UpdateBranch_ReportsUpdatedUpToDateAndConflict()
    {
        Assert.Equal(BranchUpdateResult.Updated, await _merge.UpdateBranchAsync("o", "r", "feature", "main"));

        _gitHub.MergeBranchResult = () => null;
        Assert.Equal(BranchUpdateResult.UpToDate, await _merge.UpdateBranchAsync("o", "r", "feature", "main"));

        _gitHub.MergeBranchResult = () => throw new GitHubApiException(HttpStatusCode.Conflict, "Merge conflict");
        Assert.Equal(BranchUpdateResult.Conflict, await _merge.UpdateBranchAsync("o", "r", "feature", "main"));
    }
}

public class SearchServiceTests
{
    private readonly FakeGitHubService _gitHub = new();
    private readonly FakeFileCache _cache = new();
    private readonly WorkspaceService _workspace;
    private readonly SearchService _search;
    private readonly RepoTree _tree;

    public SearchServiceTests()
    {
        _gitHub.Blobs["s1"] = "Hallo Welt\nnoch eine Zeile mit welt"u8.ToArray();
        _gitHub.Blobs["s2"] = "var weltweit = 1;"u8.ToArray();
        _gitHub.Blobs["s3"] = "nichts"u8.ToArray();
        _workspace = new WorkspaceService(_gitHub, _cache);
        _search = new SearchService(_workspace, _cache);
        _tree = RepoTree.FromEntries("t",
        [
            new("a.txt", false, "s1", Size: 30), new("b.js", false, "s2", Size: 20), new("c.md", false, "s3", Size: 6),
            new("logo.png", false, "png", Size: 100), new("riesig.log", false, "big", Size: 5_000_000)
        ], false);
    }

    private Task<SearchResult> Find(string query, bool matchCase = false, bool wholeWord = false, bool regex = false) =>
        _search.SearchAsync("o", "r", "main", _tree, new SearchOptions(query, matchCase, wholeWord, regex));

    [Fact]
    public async Task SearchableFiles_SkipBinariesAndLargeFiles() =>
        Assert.Equal(["a.txt", "b.js", "c.md"], _search.SearchableFiles(_tree).Select(e => e.Path));

    [Fact]
    public async Task Search_CountsFilesThatWereNeverLoaded()
    {
        await _workspace.OpenFileAsync("o", "r", "main", "a.txt", "s1");

        var result = await Find("welt");

        Assert.Equal((2, 1, 2), (result.TotalMatches, result.SearchedFiles, result.NotLoaded));
        Assert.Equal(2, (await _search.MissingFilesAsync("o", "r", "main", _tree)).Count);
    }

    [Fact]
    public async Task Download_LoadsMissingFilesWithProgress()
    {
        var reported = new List<int>();

        await _search.DownloadAsync("o", "r", "main", await _search.MissingFilesAsync("o", "r", "main", _tree),
            new SyncProgress(reported.Add), CancellationToken.None);

        Assert.Equal(3, reported.Max());
        Assert.Empty(await _search.MissingFilesAsync("o", "r", "main", _tree));
        Assert.Equal(3, (await Find("welt")).TotalMatches);
    }

    [Fact]
    public async Task Search_RespectsCaseWholeWordAndRegex()
    {
        await _search.DownloadAsync("o", "r", "main", _search.SearchableFiles(_tree), new SyncProgress(_ => { }), CancellationToken.None);

        Assert.Equal(1, (await Find("Welt", matchCase: true)).TotalMatches);
        Assert.Equal(2, (await Find("welt", wholeWord: true)).TotalMatches);
        Assert.Equal(1, (await Find(@"var \w+ =", regex: true)).TotalMatches);
    }

    [Fact]
    public async Task Search_IncludesLocalEditsAndNewFiles()
    {
        await _workspace.SaveContentAsync(await _workspace.OpenFileAsync("o", "r", "main", "c.md", "s3"), "jetzt mit welt");
        await _workspace.CreateFileAsync("o", "r", "main", "neu.txt", _tree, "welt auch hier");

        var result = await Find("welt");

        Assert.Contains(result.Files, f => f.Path == "c.md");
        Assert.Contains(result.Files, f => f.Path == "neu.txt");
    }

    [Fact]
    public void Find_ReturnsLineColumnAndPreviewPosition()
    {
        var match = Assert.Single(SearchService.Find("erste\n    zweite welt", SearchService.BuildRegex(new SearchOptions("welt")), 10));

        Assert.Equal((2, 12, 4), (match.Line, match.Column, match.Length));
        Assert.Equal("welt", match.Preview.Substring(match.PreviewStart, match.Length));
    }

    [Fact]
    public void BuildRegex_RejectsInvalidPattern() =>
        Assert.ThrowsAny<ArgumentException>(() => SearchService.BuildRegex(new SearchOptions("(", UseRegex: true)));

    /// <summary>Progress&lt;T&gt; meldet asynchron; für Tests sofort und synchron.</summary>
    private sealed class SyncProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }
}

public class EditorComfortTests
{
    [Fact]
    public void FuzzyMatcher_PrefersFileNameAndWordStarts()
    {
        string[] paths = ["src/Services/AuthState.cs", "src/Services/WorkspaceService.cs", "src/Pages/Workspace.razor", "README.md"];

        // "ws" + "s": Workspace + Service im Dateinamen schlägt den Ordner "Services"
        var results = FuzzyMatcher.Search("wss", paths);

        Assert.Equal("src/Services/WorkspaceService.cs", results[0]);
        Assert.DoesNotContain("README.md", results);
        Assert.Equal("src/Pages/Workspace.razor", FuzzyMatcher.Search("wsraz", paths)[0]);
    }

    [Fact]
    public void FuzzyMatcher_ExactFileNameWins() =>
        Assert.Equal("README.md", FuzzyMatcher.Search("readme.md", ["docs/readme-old.md", "README.md"])[0]);

    [Fact]
    public void FuzzyMatcher_NoMatchReturnsNull() =>
        Assert.Null(FuzzyMatcher.Score("xyz", "README.md"));

    [Fact]
    public void Settings_ParseClampsAndFallsBack()
    {
        Assert.Equal(EditorSettings.MaxFontSize, SettingsService.Parse("""{"FontSize": 99, "WordWrap": true}""").FontSize);
        Assert.True(SettingsService.Parse("""{"WordWrap": true}""").WordWrap);
        Assert.Equal(new EditorSettings(), SettingsService.Parse("kein json"));
    }

    [Fact]
    public void FileKinds_RecognizeImagesAndBinaries()
    {
        Assert.Equal(BinaryKind.Image, FileKinds.KindOf("img/Logo.PNG"));
        Assert.Equal(BinaryKind.Pdf, FileKinds.KindOf("doc.pdf"));
        Assert.True(FileKinds.IsKnownBinary("fonts/a.woff2"));
        Assert.False(FileKinds.IsKnownBinary("icon.svg"));
    }

    [Fact]
    public void CiStatus_OverallFollowsGitHubRules()
    {
        CheckItem Check(CheckState s) => new("c", s, null, null);

        Assert.Null(new CiStatus([], false).Overall);
        Assert.Equal(CheckState.Success, new CiStatus([Check(CheckState.Success), Check(CheckState.Neutral)], false).Overall);
        Assert.Equal(CheckState.Pending, new CiStatus([Check(CheckState.Success), Check(CheckState.Pending)], false).Overall);
        Assert.Equal(CheckState.Failure, new CiStatus([Check(CheckState.Pending), Check(CheckState.Failure)], false).Overall);
    }
}

public class PullRequestConversationTests
{
    private readonly FakeGitHubService _gitHub = new();
    private readonly PullRequestService _service;

    public PullRequestConversationTests() => _service = new PullRequestService(_gitHub);

    [Fact]
    public async Task Review_RequestChangesNeedsText()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ReviewAsync("o", "r", 1, ReviewAction.RequestChanges, " "));

        await _service.ReviewAsync("o", "r", 1, ReviewAction.Approve, "");
        Assert.Equal((1, ReviewAction.Approve, ""), Assert.Single(_gitHub.Reviews));
    }

    [Fact]
    public async Task Comment_And_Update_AreTrimmedAndValidated()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CommentAsync("o", "r", 1, ""));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.UpdateAsync("o", "r", 1, " ", "x"));

        await _service.CommentAsync("o", "r", 1, " Danke! ");
        await _service.UpdateAsync("o", "r", 1, " Neuer Titel ", " Text ");

        Assert.Equal("Danke!", Assert.Single(_gitHub.Conversation).Body);
        Assert.Equal((1, "Neuer Titel", "Text"), Assert.Single(_gitHub.PullRequestUpdates));
    }
}

public class TouchToolbarTests : MudTestContext
{
    [Fact]
    public void ButtonsTriggerEditorActions()
    {
        TouchToolbar.ToolbarButton? pressed = null;
        var cut = Render<TouchToolbar>(p => p.Add(x => x.Action, b => pressed = b));

        cut.Find("button[aria-label='Rückgängig']").Click();
        Assert.Equal("undo", pressed?.ActionId);

        cut.Find("button[aria-label='Klammer {']").Click();
        Assert.Equal("type", pressed?.ActionId);
    }
}
