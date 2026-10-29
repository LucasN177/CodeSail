using CodeSail.Models;
using CodeSail.Services.GitHub;

namespace CodeSail.Services;

/// <summary>Pull Requests eines Repositorys: auflisten, anlegen, mergen, schließen.</summary>
public sealed class PullRequestService(IGitHubService gitHub)
{
    public Task<IReadOnlyList<PullRequestSummary>> ListAsync(string owner, string repo, PullRequestFilter filter) =>
        gitHub.GetPullRequestsAsync(owner, repo, filter);

    public Task<PullRequestDetails> GetAsync(string owner, string repo, int number) =>
        gitHub.GetPullRequestAsync(owner, repo, number);

    public Task<IReadOnlyList<PullRequestFile>> GetFilesAsync(string owner, string repo, int number) =>
        gitHub.GetPullRequestFilesAsync(owner, repo, number);

    /// <summary>Legt den Pull Request an und liefert seine URL.</summary>
    public Task<string> CreateAsync(string owner, string repo, NewPullRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            throw new InvalidOperationException("Bitte gib einen Titel ein.");
        if (request.Head == request.Base)
            throw new InvalidOperationException("Quell- und Ziel-Branch müssen verschieden sein.");

        return gitHub.CreatePullRequestAsync(owner, repo, request.Title.Trim(), request.Body.Trim(), request.Head, request.Base, request.Draft);
    }

    /// <summary>
    /// Mergt den Pull Request. Hat sich der Branch seit dem Laden der Details verändert, lehnt GitHub ab
    /// (Schutz über die Head-SHA). Optional wird der Quell-Branch danach gelöscht, aber nur im selben Repository.
    /// </summary>
    /// <returns>true, wenn der Quell-Branch gelöscht wurde.</returns>
    public async Task<bool> MergeAsync(string owner, string repo, PullRequestDetails pr, MergeMethod method, bool deleteBranch)
    {
        if (pr.Summary.State != PullRequestState.Open)
            throw new InvalidOperationException("Nur offene Pull Requests können gemergt werden.");

        await gitHub.MergePullRequestAsync(owner, repo, pr.Summary.Number, method, pr.HeadSha);

        if (!deleteBranch || !IsSameRepository(owner, repo, pr.Summary))
            return false;

        try
        {
            await gitHub.DeleteBranchAsync(owner, repo, pr.Summary.Head);
            return true;
        }
        catch (GitHubApiException)
        {
            // Geschützte Branches oder fehlende Rechte: Der Merge selbst war trotzdem erfolgreich
            return false;
        }
    }

    public Task SetOpenAsync(string owner, string repo, int number, bool open) =>
        gitHub.SetPullRequestOpenAsync(owner, repo, number, open);

    public Task UpdateAsync(string owner, string repo, int number, string title, string body)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new InvalidOperationException("Bitte gib einen Titel ein.");
        return gitHub.UpdatePullRequestAsync(owner, repo, number, title.Trim(), body.Trim());
    }

    public Task<IReadOnlyList<ConversationItem>> GetConversationAsync(string owner, string repo, int number) =>
        gitHub.GetConversationAsync(owner, repo, number);

    public Task<CiStatus> GetCiStatusAsync(string owner, string repo, string sha) =>
        gitHub.GetCiStatusAsync(owner, repo, sha);

    public Task CommentAsync(string owner, string repo, int number, string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            throw new InvalidOperationException("Der Kommentar ist leer.");
        return gitHub.AddCommentAsync(owner, repo, number, body.Trim());
    }

    /// <summary>Reicht ein Review ein. Für "Änderungen anfordern" verlangt GitHub eine Begründung.</summary>
    public Task ReviewAsync(string owner, string repo, int number, ReviewAction action, string body)
    {
        if (action != ReviewAction.Approve && string.IsNullOrWhiteSpace(body))
            throw new InvalidOperationException("Bitte beschreibe, was geändert werden soll.");
        return gitHub.SubmitReviewAsync(owner, repo, number, action, body.Trim());
    }

    /// <summary>Pull Requests aus Forks haben ihren Branch in einem anderen Repository.</summary>
    public static bool IsSameRepository(string owner, string repo, PullRequestSummary pr) =>
        string.Equals(pr.HeadRepo, $"{owner}/{repo}", StringComparison.OrdinalIgnoreCase);
}
