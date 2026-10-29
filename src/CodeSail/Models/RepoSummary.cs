namespace CodeSail.Models;

public sealed record RepoSummary(
    long Id,
    string Owner,
    string Name,
    string DefaultBranch,
    bool IsPrivate,
    string? Description = null)
{
    public string FullName => $"{Owner}/{Name}";
}
