using System.Text;
using CodeSail.Models;
using CodeSail.Services;
using CodeSail.Services.GitHub;

namespace CodeSail.Tests;

public class WorkspaceServiceTests
{
    private const string Owner = "Octo";
    private const string Repo = "Demo";

    private readonly FakeGitHubService _gitHub = new();
    private readonly FakeFileCache _cache = new();
    private readonly WorkspaceService _workspace;

    public WorkspaceServiceTests()
    {
        _gitHub.Blobs["sha1"] = Encoding.UTF8.GetBytes("v1");
        _gitHub.Blobs["sha2"] = Encoding.UTF8.GetBytes("v2");
        _workspace = new WorkspaceService(_gitHub, _cache);
    }

    private Task<CachedFile> Open(string? sha, string path = "src/a.cs") =>
        _workspace.OpenFileAsync(Owner, Repo, "main", path, sha);

    [Fact]
    public async Task OpenFile_LoadsBlobAndCachesIt()
    {
        var file = await Open("sha1");

        Assert.Equal("v1", file.Content);
        Assert.False(file.IsDirty);
        Assert.Equal("octo/demo@main:src/a.cs", file.Key);
        Assert.Same(file, _cache.Files[file.Key]);
    }

    [Fact]
    public async Task OpenFile_WithUnchangedSha_UsesCacheWithoutRequest()
    {
        await Open("sha1");

        await Open("sha1");

        Assert.Equal(1, _gitHub.BlobRequests);
    }

    [Fact]
    public async Task OpenFile_WhenRemoteChangedAndNoLocalEdits_RefreshesFromGitHub()
    {
        await Open("sha1");

        var file = await Open("sha2");

        Assert.Equal("v2", file.Content);
        Assert.Equal("sha2", file.BlobSha);
    }

    [Fact]
    public async Task OpenFile_WhenRemoteChangedButLocallyEdited_KeepsLocalVersion()
    {
        await _workspace.SaveContentAsync(await Open("sha1"), "lokal");

        var file = await Open("sha2");

        Assert.Equal("lokal", file.Content);
        Assert.Equal("sha1", file.BlobSha);
        Assert.True(file.IsDirty);
    }

    [Fact]
    public async Task OpenFile_WhenOffline_FallsBackToCache()
    {
        await Open("sha1");
        _gitHub.NetworkError = new HttpRequestException();

        var file = await Open("sha2");

        Assert.Equal("v1", file.Content);
    }

    [Fact]
    public async Task OpenFile_WhenOfflineWithoutCache_Throws()
    {
        _gitHub.NetworkError = new HttpRequestException();

        await Assert.ThrowsAsync<HttpRequestException>(() => Open("sha1"));
    }

    [Fact]
    public async Task OpenFile_BinaryContent_IsRejected()
    {
        _gitHub.Blobs["png"] = [0x89, 0x50, 0x4E, 0x47, 0x00, 0x01];

        await Assert.ThrowsAsync<NotSupportedException>(() => Open("png", "logo.png"));
    }

    [Fact]
    public async Task SaveAndDiscard_TrackDirtyState()
    {
        var file = await Open("sha1");

        var edited = await _workspace.SaveContentAsync(file, "neu");
        Assert.True(edited.IsDirty);
        Assert.Equal(1, await _workspace.CountDirtyFilesAsync());

        var discarded = await _workspace.DiscardChangesAsync(edited);
        Assert.False(discarded!.IsDirty);
        Assert.Equal("v1", _cache.Files[file.Key].Content);
    }

    [Fact]
    public async Task GetDirtyFiles_ReturnsOnlyEditedFilesOfThatBranch()
    {
        await _workspace.SaveContentAsync(await Open("sha1", "a.cs"), "x");
        await Open("sha2", "b.cs");
        var other = await _workspace.OpenFileAsync(Owner, Repo, "dev", "a.cs", "sha1");
        await _workspace.SaveContentAsync(other, "y");

        var dirty = await _workspace.GetDirtyFilesAsync(Owner, Repo, "main");

        Assert.Equal("a.cs", Assert.Single(dirty).Path);
    }

    [Fact]
    public async Task LoadTree_WhenOffline_UsesCachedTree()
    {
        _gitHub.TreeEntries.Add(new TreeEntry("README.md", false, "sha1"));
        var branch = new BranchInfo("main", "c1");
        await _workspace.LoadTreeAsync(Owner, Repo, branch);
        _gitHub.NetworkError = new HttpRequestException();

        var loaded = await _workspace.LoadTreeAsync(Owner, Repo, branch);

        Assert.True(loaded.FromCache);
        Assert.Equal("sha1", loaded.Value.BlobShas["README.md"]);
    }

    [Fact]
    public async Task LoadBranches_WhenOfflineWithoutCache_Throws()
    {
        _gitHub.NetworkError = new HttpRequestException();

        await Assert.ThrowsAsync<HttpRequestException>(() => _workspace.LoadBranchesAsync(Owner, Repo));
    }

    [Fact]
    public async Task LoadBranches_WhenOffline_UsesCache()
    {
        await _workspace.LoadBranchesAsync(Owner, Repo);
        _gitHub.NetworkError = new TaskCanceledException();

        var loaded = await _workspace.LoadBranchesAsync(Owner, Repo);

        Assert.True(loaded.FromCache);
        Assert.Equal("main", loaded.Value.DefaultBranch);
    }

    [Fact]
    public async Task LoadRepositories_WhenOffline_UsesLastList()
    {
        _gitHub.Repositories.Add(new RepoSummary(1, Owner, Repo, "main", false));
        await _workspace.LoadRepositoriesAsync();
        _gitHub.NetworkError = new HttpRequestException();

        var loaded = await _workspace.LoadRepositoriesAsync();

        Assert.True(loaded.FromCache);
        Assert.Equal("Octo/Demo", Assert.Single(loaded.Value).FullName);
    }

    [Fact]
    public async Task InitializeRepository_CreatesReadme()
    {
        await _workspace.InitializeRepositoryAsync(Owner, Repo);

        Assert.Equal(("README.md", "# Demo\n", "Initial commit"), Assert.Single(_gitHub.FirstFiles));
    }

    [Fact]
    public void DecodeText_KeepsByteOrderMark() =>
        Assert.Equal("﻿hi", WorkspaceService.DecodeText([0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i']));
}
