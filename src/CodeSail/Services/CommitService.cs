using CodeSail.Models;
using CodeSail.Services.GitHub;
using CodeSail.Services.Storage;

namespace CodeSail.Services;

/// <param name="NewBranch">Wenn gesetzt, wird ein neuer Branch ab dem aktuellen Stand von <paramref name="Branch"/> angelegt.</param>
/// <param name="OverwriteConflicts">true: auch Dateien committen, die sich auf GitHub inzwischen geändert haben.</param>
public sealed record CommitRequest(
    string Owner,
    string Repo,
    string Branch,
    string Message,
    IReadOnlyList<CachedFile> Changes,
    string? NewBranch = null,
    bool CreatePullRequest = false,
    bool OverwriteConflicts = false);

/// <param name="Conflicts">Pfade, die lokal und auf GitHub geändert wurden. Nur gesetzt, wenn nichts committet wurde.</param>
public sealed record CommitResult(
    string? CommitSha,
    string TargetBranch,
    IReadOnlyList<string> Conflicts,
    string? PullRequestUrl = null)
{
    public bool Succeeded => CommitSha is not null;
}

/// <summary>
/// Committet lokale Änderungen als einen einzigen Commit über die Git-Data-API.
/// Vor dem Commit wird geprüft, ob dieselben Dateien auf GitHub inzwischen geändert wurden.
/// </summary>
public sealed class CommitService(IGitHubService gitHub, IFileCache cache)
{
    private const string DefaultMode = "100644";

    public async Task<CommitResult> CommitAsync(CommitRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            throw new InvalidOperationException("Bitte gib eine Commit-Nachricht ein.");
        if (request.Changes.Count == 0)
            throw new InvalidOperationException("Es gibt keine Änderungen zum Committen.");

        var withMarkers = request.Changes.Where(f => !f.Deleted && ThreeWayMerge.HasConflictMarkers(f.Content)).Select(f => f.Path).ToList();
        if (withMarkers.Count > 0)
            throw new InvalidOperationException(
                $"Noch nicht aufgelöste Konfliktmarkierungen (<<<<<<<) in: {string.Join(", ", withMarkers)}. Bereinige sie zuerst.");

        // Zwei Versuche: Wird der Branch zwischen Prüfung und Push von jemand anderem verschoben,
        // lehnt GitHub das Update ab (kein Fast-Forward) und alles wird gegen den neuen Stand geprüft.
        for (var attempt = 1; ; attempt++)
        {
            var head = await gitHub.GetBranchHeadAsync(request.Owner, request.Repo, request.Branch);
            var snapshot = await gitHub.GetTreeAsync(request.Owner, request.Repo, head);
            var remote = snapshot.Entries.Where(e => !e.IsFolder).ToDictionary(e => e.Path);

            var conflicts = FindConflicts(request.Changes, remote);
            if (conflicts.Count > 0 && !request.OverwriteConflicts)
                return new CommitResult(null, request.Branch, conflicts);

            var changes = ToTreeChanges(request.Changes, remote);
            if (changes.Count == 0)
                throw new InvalidOperationException("Die Änderungen sind auf GitHub bereits vorhanden.");

            var tree = await gitHub.CreateTreeAsync(request.Owner, request.Repo, snapshot.Sha, changes);
            var commit = await gitHub.CreateCommitAsync(request.Owner, request.Repo, request.Message.Trim(), tree, head);

            var target = request.NewBranch ?? request.Branch;
            if (request.NewBranch is not null)
            {
                await gitHub.CreateBranchAsync(request.Owner, request.Repo, request.NewBranch, commit);
            }
            else
            {
                try
                {
                    await gitHub.UpdateBranchAsync(request.Owner, request.Repo, request.Branch, commit);
                }
                catch (GitHubApiException ex) when (ex.Status == System.Net.HttpStatusCode.UnprocessableEntity && attempt < 2)
                {
                    continue;
                }
            }

            string? pullRequest = null;
            if (request.NewBranch is not null && request.CreatePullRequest)
            {
                var (title, body) = SplitMessage(request.Message);
                pullRequest = await gitHub.CreatePullRequestAsync(request.Owner, request.Repo, title, body, request.NewBranch, request.Branch);
            }

            await MarkCommittedAsync(request, target);
            return new CommitResult(commit, target, [], pullRequest);
        }
    }

    /// <summary>Dateien, deren Stand auf GitHub nicht mehr dem Stand entspricht, von dem aus lokal bearbeitet wurde.</summary>
    public static List<string> FindConflicts(IEnumerable<CachedFile> changes, IReadOnlyDictionary<string, TreeEntry> remote) =>
        changes
            .Where(f =>
            {
                var remoteSha = remote.GetValueOrDefault(f.Path).Sha;
                // Eine lokal gelöschte Datei, die auf GitHub auch schon fehlt, ist kein Konflikt
                if (f.Deleted && remoteSha is null)
                    return false;
                return remoteSha != f.BlobSha;
            })
            .Select(f => f.Path)
            .ToList();

    public static List<TreeChange> ToTreeChanges(IEnumerable<CachedFile> changes, IReadOnlyDictionary<string, TreeEntry> remote)
    {
        var result = new List<TreeChange>();
        foreach (var file in changes)
        {
            var existing = remote.GetValueOrDefault(file.Path);
            if (file.Deleted)
            {
                if (existing.Sha is not null)
                    result.Add(new TreeChange(file.Path, existing.Mode ?? DefaultMode, null));
            }
            else
            {
                // Bestehenden Modus übernehmen, damit z. B. ausführbare Skripte ausführbar bleiben
                result.Add(new TreeChange(file.Path, existing.Mode ?? DefaultMode, file.Content));
            }
        }

        return result;
    }

    public static (string Title, string Body) SplitMessage(string message)
    {
        var lines = message.Trim().Split('\n');
        return (lines[0].Trim(), string.Join('\n', lines.Skip(1)).Trim());
    }

    private async Task MarkCommittedAsync(CommitRequest request, string targetBranch)
    {
        foreach (var file in request.Changes)
        {
            // Bei einem neuen Branch gehören die Änderungen jetzt dorthin; der alte Branch ist wieder sauber
            await cache.RemoveFileAsync(file.Key);
            if (file.Deleted)
                continue;

            var key = CacheKeys.File(file.Owner, file.Repo, targetBranch, file.Path);
            await cache.SaveFileAsync(file with
            {
                Key = key,
                Branch = targetBranch,
                OriginalContent = file.Content,
                BlobSha = GitBlobSha.Compute(file.Content),
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
    }
}
