using System.Net;
using CodeSail.Models;
using CodeSail.Services.GitHub;
using CodeSail.Services.Storage;

namespace CodeSail.Services;

/// <param name="AutoMerged">Ohne Überschneidung zusammengeführt, bereit zum Commit.</param>
/// <param name="WithConflicts">Enthalten jetzt Konfliktmarkierungen und müssen im Editor bereinigt werden.</param>
/// <param name="Unresolved">Neu angelegt oder gelöscht auf einer Seite – nicht automatisch zusammenführbar.</param>
public sealed record ConflictResolution(
    IReadOnlyList<CachedFile> AutoMerged,
    IReadOnlyList<CachedFile> WithConflicts,
    IReadOnlyList<string> Unresolved);

public enum BranchUpdateResult
{
    Updated,
    UpToDate,
    Conflict
}

/// <summary>Zusammenführen: lokale Änderungen mit neuen Commits auf GitHub, und Branches untereinander.</summary>
public sealed class MergeService(IGitHubService gitHub, IFileCache cache)
{
    /// <summary>
    /// Führt lokale Fassungen mit dem aktuellen Stand des Branches auf GitHub zusammen (Drei-Wege-Merge mit dem
    /// Stand, von dem aus lokal bearbeitet wurde). Danach basieren die Dateien auf dem neuen Stand von GitHub,
    /// sodass ein erneuter Commit keinen Konflikt mehr meldet.
    /// </summary>
    public async Task<ConflictResolution> ResolveAsync(string owner, string repo, string branch, IReadOnlyList<CachedFile> conflicted)
    {
        var head = await gitHub.GetBranchHeadAsync(owner, repo, branch);
        var remote = (await gitHub.GetTreeAsync(owner, repo, head)).Entries
            .Where(e => !e.IsFolder)
            .ToDictionary(e => e.Path);

        var merged = new List<CachedFile>();
        var withConflicts = new List<CachedFile>();
        var unresolved = new List<string>();

        foreach (var file in conflicted)
        {
            if (file.Deleted || file.IsNew || !remote.TryGetValue(file.Path, out var entry) || entry.Sha is null)
            {
                unresolved.Add(file.Path);
                continue;
            }

            var theirs = WorkspaceService.DecodeText(await gitHub.GetBlobAsync(owner, repo, entry.Sha));
            if (theirs is null)
            {
                unresolved.Add(file.Path);
                continue;
            }

            var result = ThreeWayMerge.Merge(file.OriginalContent, file.Content, theirs, "Deine Änderung", $"GitHub ({branch})");
            var updated = file with
            {
                Content = result.Text,
                OriginalContent = theirs,
                BlobSha = entry.Sha,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await cache.SaveFileAsync(updated);
            (result.Conflicts > 0 ? withConflicts : merged).Add(updated);
        }

        return new ConflictResolution(merged, withConflicts, unresolved);
    }

    /// <summary>Übernimmt die Commits von <paramref name="from"/> (meist der Standard-Branch) in <paramref name="branch"/>.</summary>
    public async Task<BranchUpdateResult> UpdateBranchAsync(string owner, string repo, string branch, string from)
    {
        try
        {
            var sha = await gitHub.MergeBranchAsync(owner, repo, branch, from, $"Merge {from} into {branch}");
            return sha is null ? BranchUpdateResult.UpToDate : BranchUpdateResult.Updated;
        }
        catch (GitHubApiException ex) when (ex.Status == HttpStatusCode.Conflict)
        {
            return BranchUpdateResult.Conflict;
        }
    }
}
