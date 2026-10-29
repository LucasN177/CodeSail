namespace CodeSail.Services;

public static class TimeFormat
{
    /// <summary>Kurze relative Angabe wie "vor 5 Min." oder "vor 3 Tagen"; ältere Daten als Datum.</summary>
    public static string Relative(DateTimeOffset time, DateTimeOffset now)
    {
        var age = now - time;
        return age switch
        {
            { TotalMinutes: < 1 } => "gerade eben",
            { TotalHours: < 1 } => $"vor {(int)age.TotalMinutes} Min.",
            { TotalDays: < 1 } => $"vor {(int)age.TotalHours} Std.",
            { TotalDays: < 2 } => "gestern",
            { TotalDays: < 30 } => $"vor {(int)age.TotalDays} Tagen",
            _ => time.ToLocalTime().ToString("dd.MM.yyyy")
        };
    }
}
