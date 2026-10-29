namespace CodeSail.Models;

public sealed record RateLimitInfo(int Limit, int Remaining, DateTimeOffset ResetAt);
