using CodeSail.Models;

namespace CodeSail.Services.GitHub;

public interface IGitHubService
{
    /// <summary>Prüft das Token und liefert den Login des Nutzers.</summary>
    Task<string> GetCurrentUserLoginAsync(CancellationToken ct = default);

    Task<IReadOnlyList<RepoSummary>> GetRepositoriesAsync(CancellationToken ct = default);
}
