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

    public string? Login { get; set; }

    public ValueTask<string?> GetLoginAsync() => ValueTask.FromResult(Login);

    public ValueTask SetLoginAsync(string login)
    {
        Login = login;
        return ValueTask.CompletedTask;
    }

    public ValueTask ClearAsync()
    {
        Token = null;
        Login = null;
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

    public event Action? Unauthorized;

    /// <summary>Simuliert, dass GitHub das Token bei einem beliebigen Aufruf ablehnt.</summary>
    public void RaiseUnauthorized() => Unauthorized?.Invoke();

    public Task<string> GetCurrentUserLoginAsync() =>
        TokenStore.Token is { } token && Users.TryGetValue(token, out var login)
            ? Task.FromResult(login)
            : Task.FromException<string>(LoginError);

    public List<RepoSummary> Repositories { get; } = [];

    public Task<IReadOnlyList<RepoSummary>> GetRepositoriesAsync() =>
        Respond<IReadOnlyList<RepoSummary>>(Repositories.ToList());

    public List<(string Path, string Content, string Message)> FirstFiles { get; } = [];

    public Task CreateFirstFileAsync(string owner, string name, string path, string content, string message)
    {
        FirstFiles.Add((path, content, message));
        return Respond(0);
    }

    public Task<RepoSummary> GetRepositoryAsync(string owner, string name) =>
        Respond(new RepoSummary(1, owner, name, "main", false));

    public Task<IReadOnlyList<BranchInfo>> GetBranchesAsync(string owner, string name) =>
        Respond<IReadOnlyList<BranchInfo>>([new BranchInfo("main", "abc")]);

    public Task<TreeSnapshot> GetTreeAsync(string owner, string name, string commitSha)
    {
        if (!Commits.TryGetValue(commitSha, out var files))
            return Respond(new TreeSnapshot("tree-" + commitSha, TreeEntries.ToList(), false));

        // Simuliertes Repository: Baum aus dem Dateistand des Commits
        var treeSha = "tree-" + commitSha;
        _trees[treeSha] = files;
        var entries = files.Select(f => new TreeEntry(f.Key, false, GitBlobSha.Compute(f.Value.Content), f.Value.Mode)).ToList();
        return Respond(new TreeSnapshot(treeSha, entries, false));
    }

    public Task<byte[]> GetBlobAsync(string owner, string name, string blobSha)
    {
        BlobRequests++;
        return Respond(Blobs[blobSha]);
    }

    // --- Simuliertes Repository für Commit-Tests ---

    /// <summary>Branch → Commit-SHA.</summary>
    public Dictionary<string, string> Heads { get; } = [];

    /// <summary>Commit-SHA → Dateistand (Pfad → Inhalt und Modus).</summary>
    public Dictionary<string, Dictionary<string, (string Content, string Mode)>> Commits { get; } = [];

    public Dictionary<string, string> Messages { get; } = [];
    public List<IReadOnlyList<TreeChange>> CreatedTrees { get; } = [];
    public List<(string Title, string Body, string Head, string Base)> PullRequests { get; } = [];

    /// <summary>Wird direkt vor dem Verschieben eines Branches ausgeführt, z. B. um einen fremden Push zu simulieren.</summary>
    public Action? BeforeUpdateBranch { get; set; }

    private readonly Dictionary<string, Dictionary<string, (string Content, string Mode)>> _trees = [];
    private readonly Dictionary<string, string> _parents = [];
    private int _counter;

    /// <summary>Legt einen Commit direkt auf dem Branch an, als hätte jemand anderes gepusht.</summary>
    public string Push(string branch, Dictionary<string, string> changes, string mode = "100644")
    {
        var files = Heads.TryGetValue(branch, out var head) ? new(Commits[head]) : new Dictionary<string, (string, string)>();
        foreach (var (path, content) in changes)
            files[path] = (content, mode);

        var sha = $"seed{++_counter}";
        Commits[sha] = files;
        if (head is not null)
            _parents[sha] = head;
        Heads[branch] = sha;
        return sha;
    }

    public Task<string> GetBranchHeadAsync(string owner, string name, string branch) => Respond(Heads[branch]);

    public Task<string> CreateTreeAsync(string owner, string name, string baseTreeSha, IReadOnlyList<TreeChange> changes)
    {
        CreatedTrees.Add(changes);
        var files = new Dictionary<string, (string Content, string Mode)>(_trees[baseTreeSha]);
        foreach (var change in changes)
        {
            if (change.Content is null)
                files.Remove(change.Path);
            else
                files[change.Path] = (change.Content, change.Mode);
        }

        var sha = $"tree{++_counter}";
        _trees[sha] = files;
        return Respond(sha);
    }

    public Task<string> CreateCommitAsync(string owner, string name, string message, string treeSha, string parentSha)
    {
        var sha = $"commit{++_counter}";
        Commits[sha] = _trees[treeSha];
        _parents[sha] = parentSha;
        Messages[sha] = message;
        return Respond(sha);
    }

    public Task UpdateBranchAsync(string owner, string name, string branch, string commitSha)
    {
        BeforeUpdateBranch?.Invoke();
        BeforeUpdateBranch = null;
        if (_parents.GetValueOrDefault(commitSha) != Heads[branch])
            return Task.FromException(new GitHubApiException(System.Net.HttpStatusCode.UnprocessableEntity, "Update is not a fast forward"));

        Heads[branch] = commitSha;
        return Task.CompletedTask;
    }

    public Task CreateBranchAsync(string owner, string name, string branch, string commitSha)
    {
        if (Heads.ContainsKey(branch))
            return Task.FromException(new GitHubApiException(System.Net.HttpStatusCode.UnprocessableEntity, "Reference already exists"));

        Heads[branch] = commitSha;
        return Respond(0);
    }

    public List<bool> PullRequestDrafts { get; } = [];

    public Task<string> CreatePullRequestAsync(string owner, string name, string title, string body, string head, string baseBranch, bool draft = false)
    {
        PullRequests.Add((title, body, head, baseBranch));
        PullRequestDrafts.Add(draft);
        return Respond($"https://github.com/{owner}/{name}/pull/{PullRequests.Count}");
    }

    public List<string> DeletedBranches { get; } = [];

    /// <summary>Simuliert z. B. einen geschützten Branch, dessen Löschen GitHub ablehnt.</summary>
    public Exception? DeleteBranchError { get; set; }

    public Task DeleteBranchAsync(string owner, string name, string branch)
    {
        if (DeleteBranchError is not null)
            return Task.FromException(DeleteBranchError);
        DeletedBranches.Add(branch);
        Heads.Remove(branch);
        return Respond(0);
    }

    public Task RenameBranchAsync(string owner, string name, string branch, string newName)
    {
        if (Heads.Remove(branch, out var sha))
            Heads[newName] = sha;
        return Respond(0);
    }

    public BranchComparison? Comparison { get; set; }

    public Task<BranchComparison> CompareAsync(string owner, string name, string baseBranch, string headBranch) =>
        Respond(Comparison ?? new BranchComparison(baseBranch, headBranch, 0, 0, [], []));

    public List<PullRequestSummary> PullRequestList { get; } = [];
    public Dictionary<int, PullRequestDetails> PullRequestDetailsByNumber { get; } = [];
    public List<(int Number, MergeMethod Method, string Sha)> Merges { get; } = [];
    public List<(int Number, bool Open)> StateChanges { get; } = [];

    public Task<IReadOnlyList<PullRequestSummary>> GetPullRequestsAsync(string owner, string name, PullRequestFilter filter) =>
        Respond<IReadOnlyList<PullRequestSummary>>(PullRequestList
            .Where(p => filter == PullRequestFilter.All || (filter == PullRequestFilter.Open) == (p.State == PullRequestState.Open))
            .ToList());

    public Task<PullRequestDetails> GetPullRequestAsync(string owner, string name, int number) =>
        Respond(PullRequestDetailsByNumber[number]);

    public Task<IReadOnlyList<PullRequestFile>> GetPullRequestFilesAsync(string owner, string name, int number) =>
        Respond<IReadOnlyList<PullRequestFile>>([]);

    public Task MergePullRequestAsync(string owner, string name, int number, MergeMethod method, string headSha)
    {
        Merges.Add((number, method, headSha));
        return Respond(0);
    }

    public Task SetPullRequestOpenAsync(string owner, string name, int number, bool open)
    {
        StateChanges.Add((number, open));
        return Respond(0);
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

    public CachedRepoList? RepoList { get; set; }

    public Task<CachedRepoList?> GetRepoListAsync() => Task.FromResult(RepoList);

    public Task SaveRepoListAsync(CachedRepoList list)
    {
        RepoList = list;
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
