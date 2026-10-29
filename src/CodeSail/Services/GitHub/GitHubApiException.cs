using System.Net;

namespace CodeSail.Services.GitHub;

/// <summary>Fehlerantwort der GitHub-API bei Aufrufen, die nicht über Octokit laufen.</summary>
public sealed class GitHubApiException(HttpStatusCode status, string message)
    : Exception($"GitHub-API-Fehler {(int)status}: {message}")
{
    public HttpStatusCode Status { get; } = status;

    public string ApiMessage { get; } = message;
}
