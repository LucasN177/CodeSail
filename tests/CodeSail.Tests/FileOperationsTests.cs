using CodeSail.Models;
using CodeSail.Services;
using CodeSail.Services.GitHub;

namespace CodeSail.Tests;

public class FileOperationsTests
{
    private readonly FakeGitHubService _gitHub = new();
    private readonly FakeFileCache _cache = new();
    private readonly WorkspaceService _workspace;
    private readonly RepoTree _tree;

    public FileOperationsTests()
    {
        _gitHub.Blobs["sha-a"] = "a"u8.ToArray();
        _workspace = new WorkspaceService(_gitHub, _cache);
        _tree = RepoTree.FromEntries("t", [new("src", true), new("src/a.cs", false, "sha-a")], false);
    }

    [Fact]
    public async Task CreateFile_AddsNewDirtyFile()
    {
        var file = await _workspace.CreateFileAsync("o", "r", "main", " docs\\neu.md ", _tree, "x");

        Assert.Equal("docs/neu.md", file.Path);
        Assert.True(file.IsNew);
        Assert.Equal(ChangeKind.Added, file.Change);
        Assert.Single(await _workspace.GetChangesAsync("o", "r", "main"));
    }

    [Theory]
    [InlineData("src/a.cs")]
    [InlineData("src")]
    public async Task CreateFile_RejectsExistingPaths(string path) =>
        await Assert.ThrowsAsync<InvalidOperationException>(() => _workspace.CreateFileAsync("o", "r", "main", path, _tree));

    [Theory]
    [InlineData("")]
    [InlineData("../geheim")]
    [InlineData(".git/config")]
    public void NormalizePath_RejectsInvalidPaths(string path) =>
        Assert.Throws<InvalidOperationException>(() => WorkspaceService.NormalizePath(path));

    [Fact]
    public async Task DeleteFile_MarksExistingFileAndRemovesNewFile()
    {
        var existing = await _workspace.OpenFileAsync("o", "r", "main", "src/a.cs", "sha-a");
        var created = await _workspace.CreateFileAsync("o", "r", "main", "b.cs", _tree);

        var deleted = await _workspace.DeleteFileAsync(existing);
        var removed = await _workspace.DeleteFileAsync(created);

        Assert.True(deleted!.Deleted);
        Assert.Null(removed);
        Assert.Equal(["src/a.cs"], (await _workspace.GetChangesAsync("o", "r", "main")).Select(f => f.Path));
    }

    [Fact]
    public async Task RenameFile_CreatesNewFileWithContentAndDeletesOld()
    {
        var file = await _workspace.SaveContentAsync(await _workspace.OpenFileAsync("o", "r", "main", "src/a.cs", "sha-a"), "geändert");

        var renamed = await _workspace.RenameFileAsync(file, "src/b.cs", _tree);

        Assert.Equal("geändert", renamed.Content);
        var changes = await _workspace.GetChangesAsync("o", "r", "main");
        Assert.Equal([(ChangeKind.Deleted, "src/a.cs"), (ChangeKind.Added, "src/b.cs")], changes.Select(f => (f.Change, f.Path)));
    }

    [Fact]
    public async Task DiscardChanges_RestoresDeletedFile()
    {
        var deleted = await _workspace.DeleteFileAsync(await _workspace.OpenFileAsync("o", "r", "main", "src/a.cs", "sha-a"));

        var restored = await _workspace.DiscardChangesAsync(deleted!);

        Assert.False(restored!.IsDirty);
        Assert.Empty(await _workspace.GetChangesAsync("o", "r", "main"));
    }

    [Fact]
    public async Task DeletedFiles_AreNotReopenedAsTabs()
    {
        await _workspace.DeleteFileAsync(await _workspace.OpenFileAsync("o", "r", "main", "src/a.cs", "sha-a"));

        Assert.Empty(await _workspace.GetDirtyFilesAsync("o", "r", "main"));
    }

    [Fact]
    public async Task ApplyLocalChanges_HidesDeletedAndShowsNewFiles()
    {
        var deleted = await _workspace.DeleteFileAsync(await _workspace.OpenFileAsync("o", "r", "main", "src/a.cs", "sha-a"));
        var created = await _workspace.CreateFileAsync("o", "r", "main", "docs/neu.md", _tree);

        var display = WorkspaceService.ApplyLocalChanges(_tree, [deleted!, created]);

        Assert.Equal(["docs/neu.md"], display.FilePaths);
        Assert.Contains(display.Root, n => n is { Name: "docs", IsFolder: true });
    }

    [Fact]
    public void ApplyLocalChanges_WithoutAddedOrDeletedFiles_KeepsSameTree() =>
        Assert.Same(_tree, WorkspaceService.ApplyLocalChanges(_tree, []));

    [Theory]
    [InlineData("feature/neue-seite", true)]
    [InlineData("fix-123", true)]
    [InlineData("mit leerzeichen", false)]
    [InlineData("a..b", false)]
    [InlineData("ende/", false)]
    [InlineData("name.lock", false)]
    [InlineData(".versteckt", false)]
    [InlineData("main", false)]
    public void ValidateBranchName_FollowsGitRules(string name, bool valid)
    {
        BranchInfo[] existing = [new("main", "c")];
        if (valid)
            Assert.Equal(name, WorkspaceService.ValidateBranchName(name, existing));
        else
            Assert.Throws<InvalidOperationException>(() => WorkspaceService.ValidateBranchName(name, existing));
    }

    [Theory]
    [InlineData("", "e69de29bb2d1d6434b8b29ae775ad8c2e48c5391")]
    [InlineData("hello\n", "ce013625030ba8dba906f756967f9e9ca394464a")]
    public void GitBlobSha_MatchesGitHashObject(string content, string expected) =>
        Assert.Equal(expected, GitBlobSha.Compute(content));
}
