namespace CodeSail.Services;

public enum BinaryKind
{
    Image,
    Pdf,
    Other
}

/// <summary>Erkennt Dateiarten an der Endung (Bilder, PDFs, sonstige Binärdateien).</summary>
public static class FileKinds
{
    public static BinaryKind KindOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".avif" or ".bmp" or ".ico" => BinaryKind.Image,
        ".pdf" => BinaryKind.Pdf,
        _ => BinaryKind.Other
    };

    /// <summary>Dateien, die gar nicht erst als Text geladen werden (SVG bleibt Text und damit bearbeitbar).</summary>
    public static bool IsKnownBinary(string path) =>
        KindOf(path) != BinaryKind.Other
        || Path.GetExtension(path).ToLowerInvariant() is ".zip" or ".gz" or ".tar" or ".7z" or ".rar" or ".exe" or ".dll"
            or ".so" or ".dylib" or ".woff" or ".woff2" or ".ttf" or ".otf" or ".eot" or ".mp3" or ".mp4" or ".mov" or ".wav"
            or ".jar" or ".class" or ".pdb" or ".nupkg" or ".psd" or ".sqlite" or ".db";
}
