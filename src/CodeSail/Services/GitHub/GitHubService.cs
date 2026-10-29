using CodeSail.Models;
using CodeSail.Services.Storage;
using Octokit;
using Octokit.Internal;

namespace CodeSail.Services.GitHub;

public sealed class GitHubService(ITokenStore tokenStore) : IGitHubService
{
    private static readonly ProductHeaderValue Product = new("CodeSail");

    public async Task<string> GetCurrentUserLoginAsync(CancellationToken ct = default)
    {
        var client = await CreateClientAsync();
        var user = await client.User.Current();
        return user.Login;
    }

    public async Task<IReadOnlyList<RepoSummary>> GetRepositoriesAsync(CancellationToken ct = default)
    {
        var client = await CreateClientAsync();
        var repos = await client.Repository.GetAllForCurrent(new RepositoryRequest { Sort = RepositorySort.Updated });
        return repos
            .Select(r => new RepoSummary(r.Id, r.Owner.Login, r.Name, r.DefaultBranch, r.Private))
            .ToList();
    }

    private async Task<GitHubClient> CreateClientAsync()
    {
        var token = await tokenStore.GetTokenAsync()
            ?? throw new InvalidOperationException("Kein GitHub-Token hinterlegt.");

        // Octokits Standard-Handler setzt Optionen (z. B. Proxy/Decompression), die in WebAssembly
        // nicht unterstützt werden. Ein schlichter HttpClientHandler nutzt intern die Browser-fetch-API.
        var connection = new Connection(
            Product,
            GitHubClient.GitHubApiUrl,
            new InMemoryCredentialStore(new Credentials(token)),
            new HttpClientAdapter(() => new HttpClientHandler()),
            new SimpleJsonSerializer());

        return new GitHubClient(connection);
    }
}
