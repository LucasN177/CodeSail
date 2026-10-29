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

    public FakeGitHubService(FakeTokenStore tokenStore) => TokenStore = tokenStore;

    public RateLimitInfo? RateLimit => null;

    public event Action? RateLimitChanged { add { } remove { } }

    public Task<string> GetCurrentUserLoginAsync() =>
        TokenStore.Token is { } token && Users.TryGetValue(token, out var login)
            ? Task.FromResult(login)
            : Task.FromException<string>(LoginError);

    public Task<IReadOnlyList<RepoSummary>> GetRepositoriesAsync() =>
        Task.FromResult<IReadOnlyList<RepoSummary>>([]);

    public Task<RepoSummary> GetRepositoryAsync(string owner, string name) =>
        Task.FromResult(new RepoSummary(1, owner, name, "main", false));

    public Task<IReadOnlyList<BranchInfo>> GetBranchesAsync(string owner, string name) =>
        Task.FromResult<IReadOnlyList<BranchInfo>>([new BranchInfo("main", "abc")]);

    public Task<RepoTree> GetTreeAsync(string owner, string name, string commitSha) =>
        Task.FromResult(new RepoTree(commitSha, [], [], false));
}
