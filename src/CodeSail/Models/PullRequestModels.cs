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

public enum ConversationKind
{
    Comment,
    Review,
    LineComment
}

/// <summary>Beitrag in der Unterhaltung eines Pull Requests.</summary>
/// <param name="ReviewState">Bei Reviews: APPROVED, CHANGES_REQUESTED, COMMENTED, DISMISSED.</param>
/// <param name="Path">Bei Zeilenkommentaren: Datei und Zeile.</param>
public sealed record ConversationItem(
    ConversationKind Kind,
    string Author,
    string Body,
    DateTimeOffset Date,
    string? ReviewState = null,
    string? Path = null,
    int? Line = null);

public enum ReviewAction
{
    Comment,
    Approve,
    RequestChanges
}

public enum CheckState
{
    Success,
    Pending,
    Failure,
    Neutral
}

/// <summary>Einzelner Status-Check bzw. Check-Run (CI) eines Commits.</summary>
public sealed record CheckItem(string Name, CheckState State, string? Description, string? Url);

/// <param name="MissingPermission">Mindestens eine Quelle war mit diesem Token nicht lesbar (Rechte fehlen).</param>
public sealed record CiStatus(IReadOnlyList<CheckItem> Items, bool MissingPermission)
{
    /// <summary>Gesamtzustand wie auf GitHub: ein Fehler genügt, sonst "läuft", sonst Erfolg.</summary>
    public CheckState? Overall =>
        Items.Count == 0 ? null
        : Items.Any(i => i.State == CheckState.Failure) ? CheckState.Failure
        : Items.Any(i => i.State == CheckState.Pending) ? CheckState.Pending
        : CheckState.Success;
}
