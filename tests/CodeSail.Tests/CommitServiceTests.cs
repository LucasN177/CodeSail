using CodeSail.Models;
using CodeSail.Services;
using CodeSail.Services.GitHub;

namespace CodeSail.Tests;

public class CommitServiceTests
{
    private const string Owner = "octo";
    private const string Repo = "demo";

    private readonly FakeGitHubService _gitHub = new();
    private readonly FakeFileCache _cache = new();
    private readonly WorkspaceService _workspace;
    private readonly CommitService _commits;

    public CommitServiceTests()
    {
        _gitHub.Push("main", new() { ["README.md"] = "hallo", ["src/app.cs"] = "v1" });
        _gitHub.Push("main", new() { ["run.sh"] = "echo" }, mode: "100755");
        _workspace = new WorkspaceService(_gitHub, _cache);
        _commits = new CommitService(_gitHub, _cache);
    }

    private Dictionary<string, (string Content, string Mode)> MainFiles => _gitHub.Commits[_gitHub.Heads["main"]];

    /// <summary>Öffnet eine Datei so, wie es der Workspace tut: mit der Blob-SHA aus dem aktuellen Baum.</summary>
    private async Task<CachedFile> Open(string path, string branch = "main")
    {
        var tree = await _gitHub.GetTreeAsync(Owner, Repo, _gitHub.Heads[branch]);
        var entry = tree.Entries.Single(e => e.Path == path);
        _gitHub.Blobs[entry.Sha!] = System.Text.Encoding.UTF8.GetBytes(MainFiles[path].Content);
        return await _workspace.OpenFileAsync(Owner, Repo, branch, path, entry.Sha);
    }

    private async Task<CachedFile> Edit(string path, string content) =>
        await _workspace.SaveContentAsync(await Open(path), content);

    private Task<IReadOnlyList<CachedFile>> Changes() => _workspace.GetChangesAsync(Owner, Repo, "main");

    private async Task<CommitResult> Commit(string message = "Änderung", string? newBranch = null, bool pr = false, bool overwrite = false) =>
        await _commits.CommitAsync(new CommitRequest(Owner, Repo, "main", message, await Changes(), newBranch, pr, overwrite));

    [Fact]
    public async Task Commit_PushesAllChangesAsOneCommit()
    {
        await Edit("README.md", "neu");
        var repoTree = RepoTree.FromEntries("t", (await _gitHub.GetTreeAsync(Owner, Repo, _gitHub.Heads["main"])).Entries, false);
        await _workspace.CreateFileAsync(Owner, Repo, "main", "docs/guide.md", repoTree, "# Guide");
        await _workspace.DeleteFileAsync(await Open("src/app.cs"));

        var result = await Commit("Doku ergänzen\n\nAusführliche Beschreibung");

        Assert.True(result.Succeeded);
        Assert.Equal(result.CommitSha, _gitHub.Heads["main"]);
        Assert.Equal("Doku ergänzen\n\nAusführliche Beschreibung", _gitHub.Messages[result.CommitSha!]);
        Assert.Equal("neu", MainFiles["README.md"].Content);
        Assert.Equal("# Guide", MainFiles["docs/guide.md"].Content);
        Assert.False(MainFiles.ContainsKey("src/app.cs"));
        Assert.Single(_gitHub.CreatedTrees);
    }

    [Fact]
    public async Task Commit_MarksFilesCleanWithMatchingBlobSha()
    {
        await Edit("README.md", "neu");

        await Commit();

        Assert.Empty(await Changes());
        var cached = _cache.Files[CacheKeys.File(Owner, Repo, "main", "README.md")];
        Assert.Equal("neu", cached.OriginalContent);
        // Die lokal berechnete SHA muss zu GitHubs Baum passen, sonst würde die Datei erneut geladen
        var remoteSha = (await _gitHub.GetTreeAsync(Owner, Repo, _gitHub.Heads["main"])).Entries.Single(e => e.Path == "README.md").Sha;
        Assert.Equal(remoteSha, cached.BlobSha);
    }

    [Fact]
    public async Task Commit_KeepsExecutableModeAndUsesDefaultForNewFiles()
    {
        await Edit("run.sh", "echo neu");
        var repoTree = RepoTree.FromEntries("t", (await _gitHub.GetTreeAsync(Owner, Repo, _gitHub.Heads["main"])).Entries, false);
        await _workspace.CreateFileAsync(Owner, Repo, "main", "neu.txt", repoTree, "x");

        await Commit();

        Assert.Equal("100755", MainFiles["run.sh"].Mode);
        Assert.Equal("100644", MainFiles["neu.txt"].Mode);
    }

    [Fact]
    public async Task Commit_WhenSameFileChangedOnGitHub_ReportsConflictAndCommitsNothing()
    {
        await Edit("README.md", "lokal");
        var before = _gitHub.Push("main", new() { ["README.md"] = "remote" });

        var result = await Commit();

        Assert.False(result.Succeeded);
        Assert.Equal(["README.md"], result.Conflicts);
        Assert.Equal(before, _gitHub.Heads["main"]);
        Assert.Empty(_gitHub.CreatedTrees);
    }

    [Fact]
    public async Task Commit_WithOverwrite_ReplacesRemoteVersion()
    {
        await Edit("README.md", "lokal");
        _gitHub.Push("main", new() { ["README.md"] = "remote" });

        var result = await Commit(overwrite: true);

        Assert.True(result.Succeeded);
        Assert.Equal("lokal", MainFiles["README.md"].Content);
    }

    [Fact]
    public async Task Commit_WhenOtherFilesChangedOnGitHub_CommitsOnTopWithoutConflict()
    {
        await Edit("README.md", "lokal");
        _gitHub.Push("main", new() { ["src/app.cs"] = "remote" });

        var result = await Commit();

        Assert.True(result.Succeeded);
        Assert.Equal("lokal", MainFiles["README.md"].Content);
        Assert.Equal("remote", MainFiles["src/app.cs"].Content);
    }

    [Fact]
    public async Task Commit_WhenBranchMovesDuringPush_RetriesAgainstNewHead()
    {
        await Edit("README.md", "lokal");
        _gitHub.BeforeUpdateBranch = () => _gitHub.Push("main", new() { ["src/app.cs"] = "dazwischen" });

        var result = await Commit();

        Assert.True(result.Succeeded);
        Assert.Equal("lokal", MainFiles["README.md"].Content);
        Assert.Equal("dazwischen", MainFiles["src/app.cs"].Content);
        Assert.Equal(2, _gitHub.CreatedTrees.Count);
    }

    [Fact]
    public async Task Commit_ToNewBranch_CreatesBranchAndPullRequestAndMovesChanges()
    {
        var mainHead = _gitHub.Heads["main"];
        await Edit("README.md", "feature");

        var result = await Commit("Neue Funktion\n\nDetails", newBranch: "feature/x", pr: true);

        Assert.True(result.Succeeded);
        Assert.Equal("feature/x", result.TargetBranch);
        Assert.Equal(mainHead, _gitHub.Heads["main"]);
        Assert.Equal("feature", _gitHub.Commits[_gitHub.Heads["feature/x"]]["README.md"].Content);
        Assert.Equal(("Neue Funktion", "Details", "feature/x", "main"), Assert.Single(_gitHub.PullRequests));
        Assert.NotNull(result.PullRequestUrl);

        // Auf main ist danach nichts mehr offen, auf dem neuen Branch liegt die committete Fassung
        Assert.Empty(await Changes());
        Assert.False(_cache.Files[CacheKeys.File(Owner, Repo, "feature/x", "README.md")].IsDirty);
    }

    [Fact]
    public async Task Commit_DeletingFileAlreadyDeletedOnGitHub_IsNoConflict()
    {
        await _workspace.DeleteFileAsync(await Open("src/app.cs"));
        await Edit("README.md", "neu");
        var head = _gitHub.Heads["main"];
        var files = new Dictionary<string, (string, string)>(_gitHub.Commits[head]);
        files.Remove("src/app.cs");
        _gitHub.Commits["gone"] = files;
        _gitHub.Heads["main"] = "gone";

        var result = await Commit();

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(_gitHub.CreatedTrees.Single(), c => c.Path == "src/app.cs");
    }

    [Fact]
    public async Task Commit_WithoutMessage_Throws()
    {
        await Edit("README.md", "neu");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Commit("  "));
    }

    [Fact]
    public void SplitMessage_SeparatesTitleAndBody()
    {
        Assert.Equal(("Titel", ""), CommitService.SplitMessage("Titel\n"));
        Assert.Equal(("Titel", "Zeile 1\nZeile 2"), CommitService.SplitMessage("Titel\n\nZeile 1\nZeile 2"));
    }
}
