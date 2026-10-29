namespace CodeSail.Models;

/// <param name="Protected">Branch-Schutzregeln aktiv (z. B. kein direktes Pushen oder Löschen).</param>
public sealed record BranchInfo(string Name, string CommitSha, bool Protected = false);

/// <summary>Vergleich zweier Branches: wie viele Commits <c>Head</c> vor bzw. hinter <c>Base</c> liegt.</summary>
public sealed record BranchComparison(
    string Base,
    string Head,
    int AheadBy,
    int BehindBy,
    IReadOnlyList<CommitSummary> Commits,
    IReadOnlyList<string> ChangedFiles);

public sealed record CommitSummary(string Sha, string Message, string Author, DateTimeOffset? Date)
{
    public string ShortSha => Sha.Length > 7 ? Sha[..7] : Sha;

    public string Title => Message.Split('\n')[0];
}
