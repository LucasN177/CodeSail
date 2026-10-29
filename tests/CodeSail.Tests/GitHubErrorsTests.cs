using CodeSail.Services.GitHub;
using Octokit;

namespace CodeSail.Tests;

public class GitHubErrorsTests
{
    [Fact]
    public void Describe_AuthorizationError_MentionsInvalidToken() =>
        Assert.Contains("ungültig", GitHubErrors.Describe(new AuthorizationException()));

    [Fact]
    public void Describe_NetworkError_MentionsOffline() =>
        Assert.Contains("offline", GitHubErrors.Describe(new HttpRequestException()));

    [Fact]
    public void Describe_UnknownError_IncludesMessage() =>
        Assert.Contains("kaputt", GitHubErrors.Describe(new InvalidOperationException("kaputt")));
}
