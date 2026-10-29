using System.Net;
using Octokit;

namespace CodeSail.Services.GitHub;

/// <summary>Übersetzt Fehler der GitHub-API in verständliche Meldungen für die Oberfläche.</summary>
public static class GitHubErrors
{
    public static string Describe(Exception ex) => ex switch
    {
        AuthorizationException => "Das Token ist ungültig oder abgelaufen.",
        // RateLimitExceededException erbt von ForbiddenException und muss davor stehen
        RateLimitExceededException rate =>
            $"GitHub-API-Limit erreicht. Wieder verfügbar ab {rate.Reset.ToLocalTime():HH:mm} Uhr.",
        ForbiddenException =>
            "Zugriff verweigert. Prüfe die Berechtigungen des Tokens (Contents: Read and write).",
        NotFoundException => "Nicht gefunden oder das Token hat keinen Zugriff darauf.",
        GitHubApiException { Status: HttpStatusCode.Unauthorized } => "Das Token ist ungültig oder abgelaufen.",
        GitHubApiException { Status: HttpStatusCode.Forbidden } =>
            "Zugriff verweigert. Das Token braucht für diesen Schritt das Recht Contents: Read and write.",
        GitHubApiException { Status: HttpStatusCode.NotFound } =>
            "Nicht gefunden oder das Token hat keinen Schreibzugriff auf dieses Repository.",
        GitHubApiException api => $"GitHub hat die Anfrage abgelehnt: {api.ApiMessage}",
        HttpRequestException or TaskCanceledException =>
            "Keine Verbindung zu GitHub und keine Offline-Kopie vorhanden. Bist du offline?",
        NotSupportedException or InvalidOperationException => ex.Message,
        _ => $"Unerwarteter Fehler: {ex.Message}"
    };
}
