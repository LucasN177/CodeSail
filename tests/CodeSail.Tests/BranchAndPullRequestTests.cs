using System.Net;
using Bunit;
using CodeSail.Components;
using CodeSail.Models;
using CodeSail.Services;
using CodeSail.Services.GitHub;
using Microsoft.Extensions.DependencyInjection;

namespace CodeSail.Tests;

public class BranchManagementTests
{
    private readonly FakeGitHubService _gitHub = new();
    private readonly FakeFileCache _cache = new();
    private readonly WorkspaceService _workspace;

    public BranchManagementTests()
    {
        _gitHub.Blobs["sha"] = "inhalt"u8.ToArray();
        _gitHub.Heads["main"] = "c1";
        _gitHub.Heads["feature/x"] = "c2";
        _workspace = new WorkspaceService(_gitHub, _cache);
    }

    private async Task EditOn(string branch, string path) =>
        await _workspace.SaveContentAsync(await _workspace.OpenFileAsync("o", "r", branch, path, "sha"), "geändert");

    [Fact]
    public async Task LocalChangeCounts_AreGroupedByBranch()
    {
        await EditOn("main", "a.txt");
        await EditOn("feature/x", "a.txt");
        await EditOn("feature/x", "b.txt");
        await _workspace.OpenFileAsync("o", "r", "main", "unverändert.txt", "sha");

        var counts = await _workspace.GetLocalChangeCountsAsync("o", "r");

        Assert.Equal(1, counts["main"]);
        Assert.Equal(2, counts["feature/x"]);
    }

    [Fact]
    public async Task DeleteBranch_RemovesBranchAndItsLocalCopies()
    {
        await EditOn("feature/x", "a.txt");
        await EditOn("main", "a.txt");

        await _workspace.DeleteBranchAsync("o", "r", new BranchInfo("feature/x", "c2"), "main");

        Assert.Equal(["feature/x"], _gitHub.DeletedBranches);
        Assert.DoesNotContain(_cache.Files.Values, f => f.Branch == "feature/x");
        Assert.Contains(_cache.Files.Values, f => f.Branch == "main");
    }

    [Theory]
    [InlineData("main", false)]
    [InlineData("release", true)]
    public async Task DeleteBranch_RefusesDefaultAndProtectedBranches(string name, bool isProtected)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _workspace.DeleteBranchAsync("o", "r", new BranchInfo(name, "c", isProtected), "main"));

        Assert.Empty(_gitHub.DeletedBranches);
    }

    [Fact]
    public async Task RenameBranch_MovesLocalChangesToNewName()
    {
        await EditOn("feature/x", "a.txt");

        var renamed = await _workspace.RenameBranchAsync("o", "r", new BranchInfo("feature/x", "c2"), "feature/y", [new("main", "c1")]);

        Assert.Equal("feature/y", renamed.Name);
        Assert.True(_gitHub.Heads.ContainsKey("feature/y"));
        var moved = Assert.Single(_cache.Files.Values);
        Assert.Equal(("feature/y", CacheKeys.File("o", "r", "feature/y", "a.txt")), (moved.Branch, moved.Key));
        Assert.True(moved.IsDirty);
    }

    [Fact]
    public async Task RenameBranch_ValidatesNewName() =>
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _workspace.RenameBranchAsync("o", "r", new BranchInfo("feature/x", "c2"), "main", [new("main", "c1")]));
}

public class PullRequestServiceTests
{
    private readonly FakeGitHubService _gitHub = new();
    private readonly PullRequestService _service;

    public PullRequestServiceTests() => _service = new PullRequestService(_gitHub);

    private static PullRequestDetails Details(PullRequestState state = PullRequestState.Open, string headRepo = "o/r", bool draft = false) =>
        new(new PullRequestSummary(7, "Titel", state, draft, "ada", "feature/x", headRepo, "main", DateTimeOffset.UtcNow, "https://github.com/o/r/pull/7"),
            "Text", "headsha", true, "clean", 2, 10, 3, 1);

    [Fact]
    public async Task Create_TrimsAndPassesDraftFlag()
    {
        await _service.CreateAsync("o", "r", new NewPullRequest("  Neue Seite ", " Beschreibung ", "feature/x", "main", Draft: true));

        Assert.Equal(("Neue Seite", "Beschreibung", "feature/x", "main"), Assert.Single(_gitHub.PullRequests));
        Assert.True(Assert.Single(_gitHub.PullRequestDrafts));
    }

    [Theory]
    [InlineData("", "feature/x", "main")]
    [InlineData("Titel", "main", "main")]
    public async Task Create_RejectsMissingTitleOrSameBranches(string title, string head, string baseBranch) =>
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateAsync("o", "r", new NewPullRequest(title, "", head, baseBranch, false)));

    [Fact]
    public async Task Merge_UsesHeadShaAndDeletesBranchInSameRepository()
    {
        var deleted = await _service.MergeAsync("o", "r", Details(), MergeMethod.Squash, deleteBranch: true);

        Assert.True(deleted);
        Assert.Equal((7, MergeMethod.Squash, "headsha"), Assert.Single(_gitHub.Merges));
        Assert.Equal(["feature/x"], _gitHub.DeletedBranches);
    }

    [Fact]
    public async Task Merge_NeverDeletesBranchOfFork()
    {
        var deleted = await _service.MergeAsync("o", "r", Details(headRepo: "fremd/r"), MergeMethod.Merge, deleteBranch: true);

        Assert.False(deleted);
        Assert.Empty(_gitHub.DeletedBranches);
    }

    [Fact]
    public async Task Merge_SucceedsEvenIfBranchCannotBeDeleted()
    {
        _gitHub.DeleteBranchError = new GitHubApiException(HttpStatusCode.UnprocessableEntity, "protected");

        var deleted = await _service.MergeAsync("o", "r", Details(), MergeMethod.Merge, deleteBranch: true);

        Assert.False(deleted);
        Assert.Single(_gitHub.Merges);
    }

    [Fact]
    public async Task Merge_OfClosedPullRequest_Throws() =>
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.MergeAsync("o", "r", Details(PullRequestState.Closed), MergeMethod.Merge, false));

    [Fact]
    public void CanMerge_RequiresOpenNonDraftWithoutConflicts()
    {
        Assert.True(Details().CanMerge);
        Assert.False(Details(draft: true).CanMerge);
        Assert.False((Details() with { Mergeable = false }).CanMerge);
        Assert.False((Details() with { Mergeable = null }).CanMerge);
    }

    [Theory]
    [InlineData("feature/neue-seite", "Neue seite")]
    [InlineData("fix_login", "Fix login")]
    [InlineData("main", "Main")]
    public void SuggestTitle_UsesLastPartOfBranchName(string branch, string expected) =>
        Assert.Equal(expected, NewPullRequestDialog.SuggestTitle(branch));

    [Fact]
    public void TimeFormat_IsShortAndRelative()
    {
        var now = new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal("gerade eben", TimeFormat.Relative(now.AddSeconds(-20), now));
        Assert.Equal("vor 5 Min.", TimeFormat.Relative(now.AddMinutes(-5), now));
        Assert.Equal("vor 3 Std.", TimeFormat.Relative(now.AddHours(-3), now));
        Assert.Equal("gestern", TimeFormat.Relative(now.AddHours(-30), now));
        Assert.Equal("vor 4 Tagen", TimeFormat.Relative(now.AddDays(-4), now));
    }
}

public class BranchesPanelTests : MudTestContext
{
    private static readonly BranchInfo[] Branches =
        [new("zeta", "1"), new("main", "2"), new("feature/a", "3"), new("release", "4", Protected: true)];

    [Fact]
    public void ShowsCurrentAndDefaultFirstWithTags()
    {
        var cut = Render<BranchesPanel>(p => p
            .Add(x => x.Branches, Branches)
            .Add(x => x.Current, "feature/a")
            .Add(x => x.DefaultBranch, "main")
            .Add(x => x.LocalChanges, new Dictionary<string, int> { ["feature/a"] = 2 }));

        var names = cut.FindAll(".mud-list-item .text-truncate").Select(e => e.TextContent.Trim()).ToList();
        Assert.Equal(["feature/a", "main", "release", "zeta"], names);
        Assert.Contains("aktuell", cut.Markup);
        Assert.Contains("2 lokal", cut.Markup);
        Assert.Contains("geschützt", cut.Markup);
    }

    [Fact]
    public void TappingAnotherBranchSwitches()
    {
        BranchInfo? switched = null;
        var cut = Render<BranchesPanel>(p => p
            .Add(x => x.Branches, Branches)
            .Add(x => x.Current, "main")
            .Add(x => x.DefaultBranch, "main")
            .Add(x => x.Switch, b => switched = b));

        cut.FindAll(".mud-list-item").Last().Click();

        cut.WaitForAssertion(() => Assert.Equal("zeta", switched?.Name));
    }
}

public class PullRequestsPanelTests : MudTestContext
{
    [Fact]
    public void ListsOpenPullRequestsAndReportsCount()
    {
        var gitHub = new FakeGitHubService();
        gitHub.PullRequestList.Add(new PullRequestSummary(3, "Neue Seite", PullRequestState.Open, false, "ada", "feature/x", "o/r", "main", DateTimeOffset.UtcNow, "u"));
        gitHub.PullRequestList.Add(new PullRequestSummary(2, "Alt", PullRequestState.Merged, false, "bob", "fix", "o/r", "main", DateTimeOffset.UtcNow, "u"));
        Services.AddSingleton<IGitHubService>(gitHub);
        Services.AddSingleton<PullRequestService>();
        var openCount = -1;

        var cut = Render<PullRequestsPanel>(p => p
            .Add(x => x.Owner, "o")
            .Add(x => x.Repo, "r")
            .Add(x => x.OpenCountChanged, count => openCount = count));

        cut.WaitForAssertion(() => Assert.Contains("Neue Seite", cut.Markup));
        Assert.DoesNotContain("Alt", cut.Markup);
        Assert.Contains("#3 · feature/x → main", cut.Markup);
        Assert.Equal(1, openCount);
    }
}
