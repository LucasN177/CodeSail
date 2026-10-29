namespace CodeSail.Models;

public sealed record RepoSummary(long Id, string Owner, string Name, string DefaultBranch, bool IsPrivate);
