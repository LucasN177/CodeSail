namespace CodeSail.Models;

public enum PullRequestState
{
    Open,
    Closed,
    Merged
}

public enum PullRequestFilter
{
    Open,
    Closed,
    All
}

public enum MergeMethod
{
    Merge,
    Squash,
    Rebase
}

/// <param name="HeadRepo">"owner/name" des Head-Branches; bei Forks ein anderes Repository.</param>
public sealed record PullRequestSummary(
    int Number,
    string Title,
    PullRequestState State,
    bool Draft,
    string Author,
    string Head,
    string HeadRepo,
    string Base,
    DateTimeOffset UpdatedAt,
    string HtmlUrl);

/// <param name="Mergeable">null, solange GitHub das noch berechnet.</param>
public sealed record PullRequestDetails(
    PullRequestSummary Summary,
    string Body,
    string HeadSha,
    bool? Mergeable,
    string MergeableState,
    int Commits,
    int Additions,
    int Deletions,
    int ChangedFiles)
{
    /// <summary>Kann ohne Konflikte gemergt werden (Branch-Schutzregeln prüft GitHub beim Mergen).</summary>
    public bool CanMerge => Summary.State == PullRequestState.Open && !Summary.Draft && Mergeable == true;
}

/// <param name="Status">added, modified, removed, renamed, …</param>
/// <param name="Patch">Unified Diff; fehlt bei Binärdateien und sehr großen Änderungen.</param>
public sealed record PullRequestFile(string Path, string Status, int Additions, int Deletions, string? Patch, string? PreviousPath);

public sealed record NewPullRequest(string Title, string Body, string Head, string Base, bool Draft);
