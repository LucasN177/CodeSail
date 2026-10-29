using CodeSail.Models;
using CodeSail.Services.GitHub;
using CodeSail.Services.Storage;

namespace CodeSail.Tests;

internal sealed class FakeTokenStore : ITokenStore
{
    public string? Token { get; set; }
    public bool Persistent { get; private set; }

    public ValueTask<string?> GetTokenAsync() => ValueTask.FromResult(Token);

    public ValueTask SetTokenAsync(string token, bool persistent)
    {
        Token = token;
        Persistent = persistent;
        return ValueTask.CompletedTask;
    }

    public ValueTask ClearAsync()
    {
        Token = null;
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeGitHubService : IGitHubService
{
    /// <summary>Gültiges Token → Login. Unbekannte Tokens lösen <see cref="LoginError"/> aus.</summary>
    public Dictionary<string, string> Users { get; } = [];
    public Exception LoginError { get; set; } = new Octokit.AuthorizationException();
    public FakeTokenStore TokenStore { get; }

    /// <summary>Blob-SHA → Inhalt. Ist <see cref="NetworkError"/> gesetzt, schlagen alle Abrufe fehl.</summary>
    public Dictionary<string, byte[]> Blobs { get; } = [];
    public List<TreeEntry> TreeEntries { get; } = [];
    public Exception? NetworkError { get; set; }
    public int BlobRequests { get; private set; }

    public FakeGitHubService(FakeTokenStore? tokenStore = null) => TokenStore = tokenStore ?? new FakeTokenStore();

    public RateLimitInfo? RateLimit => null;

    public event Action? RateLimitChanged { add { } remove { } }

    public Task<string> GetCurrentUserLoginAsync() =>
        TokenStore.Token is { } token && Users.TryGetValue(token, out var login)
            ? Task.FromResult(login)
            : Task.FromException<string>(LoginError);

    public Task<IReadOnlyList<RepoSummary>> GetRepositoriesAsync() =>
        Respond<IReadOnlyList<RepoSummary>>([]);

    public Task<RepoSummary> GetRepositoryAsync(string owner, string name) =>
        Respond(new RepoSummary(1, owner, name, "main", false));

    public Task<IReadOnlyList<BranchInfo>> GetBranchesAsync(string owner, string name) =>
        Respond<IReadOnlyList<BranchInfo>>([new BranchInfo("main", "abc")]);

    public Task<TreeSnapshot> GetTreeAsync(string owner, string name, string commitSha) =>
        Respond(new TreeSnapshot("tree-" + commitSha, TreeEntries.ToList(), false));

    public Task<byte[]> GetBlobAsync(string owner, string name, string blobSha)
    {
        BlobRequests++;
        return Respond(Blobs[blobSha]);
    }

    private Task<T> Respond<T>(T value) =>
        NetworkError is null ? Task.FromResult(value) : Task.FromException<T>(NetworkError);
}

internal sealed class FakeFileCache : IFileCache
{
    public Dictionary<string, CachedFile> Files { get; } = [];
    public Dictionary<string, CachedTree> Trees { get; } = [];
    public Dictionary<string, CachedRepo> Repos { get; } = [];

    public Task<CachedFile?> GetFileAsync(string key) => Task.FromResult(Files.GetValueOrDefault(key));

    public Task SaveFileAsync(CachedFile file)
    {
        Files[file.Key] = file;
        return Task.CompletedTask;
    }

    public Task RemoveFileAsync(string key)
    {
        Files.Remove(key);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CachedFile>> GetFilesAsync(string keyPrefix) =>
        Task.FromResult<IReadOnlyList<CachedFile>>(
            Files.Values.Where(f => f.Key.StartsWith(keyPrefix, StringComparison.Ordinal)).ToList());

    public Task<CachedTree?> GetTreeAsync(string key) => Task.FromResult(Trees.GetValueOrDefault(key));

    public Task SaveTreeAsync(CachedTree tree)
    {
        Trees[tree.Key] = tree;
        return Task.CompletedTask;
    }

    public Task<CachedRepo?> GetRepoAsync(string key) => Task.FromResult(Repos.GetValueOrDefault(key));

    public Task SaveRepoAsync(CachedRepo repo)
    {
        Repos[repo.Key] = repo;
        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        Files.Clear();
        Trees.Clear();
        Repos.Clear();
        return Task.CompletedTask;
    }

    public Task<bool> RequestPersistenceAsync() => Task.FromResult(true);

    public Task<long?> EstimateUsageAsync() => Task.FromResult<long?>(null);
}
