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
        HttpRequestException => "Keine Verbindung zu GitHub. Bist du offline?",
        _ => $"Unerwarteter Fehler: {ex.Message}"
    };
}
