using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;

namespace CodeSail.Services.Preview;

/// <summary>Eingebettete Datei aus dem Repository (Bild, Stylesheet, Skript).</summary>
public sealed record PreviewAsset(string Path, byte[] Content)
{
    public string MimeType => PreviewBuilder.MimeType(Path);

    public string Text => Encoding.UTF8.GetString(Content);

    public string DataUrl => $"data:{MimeType};base64,{Convert.ToBase64String(Content)}";
}

/// <summary>
/// Baut das Dokument für die Vorschau. Es läuft in einem iframe ohne "allow-same-origin",
/// daher können Seiten aus dem Repository weder auf Token noch auf den Cache der App zugreifen.
/// Dateien aus dem Repository werden direkt eingebettet, weil der iframe sie nicht selbst laden kann.
/// </summary>
public static partial class PreviewBuilder
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public static PreviewKind KindOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".md" or ".markdown" or ".mdx" => PreviewKind.Markdown,
        ".html" or ".htm" => PreviewKind.Html,
        _ => PreviewKind.None
    };

    public static string RenderMarkdown(string markdown) => Markdown.ToHtml(markdown, Pipeline);

    /// <summary>Verweise in src/href, die auf Dateien im Repository zeigen könnten (keine URLs, Anker oder data:).</summary>
    public static IReadOnlyList<string> FindLocalReferences(string html) =>
        TagRegex().Matches(html)
            .Select(m => AttributeRegex().Match(m.Value))
            .Where(a => a.Success)
            .Select(a => WebUtility.HtmlDecode(AttributeValue(a)))
            .Where(IsLocalReference)
            .Distinct()
            .ToList();

    /// <summary>Löst einen Verweis relativ zur Datei auf; "/" beginnt an der Repo-Wurzel. Null, wenn er aus dem Repo hinausführt.</summary>
    public static string? ResolvePath(string fromFile, string reference)
    {
        var target = reference.Split('#', '?')[0];
        if (target.Length == 0)
            return null;

        var segments = new List<string>();
        if (!target.StartsWith('/'))
        {
            var folder = fromFile.Contains('/') ? fromFile[..fromFile.LastIndexOf('/')] : "";
            segments.AddRange(folder.Split('/', StringSplitOptions.RemoveEmptyEntries));
        }

        foreach (var part in Uri.UnescapeDataString(target).Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".")
                continue;
            if (part == "..")
            {
                if (segments.Count == 0)
                    return null;
                segments.RemoveAt(segments.Count - 1);
                continue;
            }
            segments.Add(part);
        }

        return segments.Count == 0 ? null : string.Join('/', segments);
    }

    /// <summary>
    /// Ersetzt Verweise auf Repo-Dateien: Stylesheets und Skripte werden eingebettet,
    /// alles andere (Bilder, Medien) als data:-URL eingesetzt. Nicht gefundene Verweise bleiben stehen.
    /// </summary>
    public static string EmbedAssets(string html, IReadOnlyDictionary<string, PreviewAsset> assets) =>
        TagRegex().Replace(html, tag =>
        {
            var attribute = AttributeRegex().Match(tag.Value);
            if (!attribute.Success || !assets.TryGetValue(WebUtility.HtmlDecode(AttributeValue(attribute)), out var asset))
                return tag.Value;

            var name = tag.Groups["tag"].Value.ToLowerInvariant();
            if (name == "link" && StylesheetRegex().IsMatch(tag.Value))
                return $"<style data-source=\"{WebUtility.HtmlEncode(asset.Path)}\">\n{asset.Text}\n</style>";

            if (name == "script")
            {
                // Das schließende </script> des Originals bleibt erhalten; im Code selbst darf keines vorkommen
                var withoutSrc = tag.Value.Remove(attribute.Index, attribute.Length);
                return withoutSrc + asset.Text.Replace("</script", "<\\/script", StringComparison.OrdinalIgnoreCase);
            }

            return tag.Value.Remove(attribute.Index, attribute.Length)
                .Insert(attribute.Index, $"{attribute.Groups["name"].Value}=\"{asset.DataUrl}\"");
        });

    /// <summary>Komplettes Dokument für den iframe, inklusive Skript, das die Scrollposition über Neuaufbauten hinweg hält.</summary>
    public static string BuildDocument(string html, PreviewKind kind, string title)
    {
        if (kind == PreviewKind.Markdown)
        {
            return $"""
                <!DOCTYPE html>
                <html lang="de"><head><meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <title>{WebUtility.HtmlEncode(title)}</title>
                <base target="_blank">
                <style>{MarkdownCss}</style>
                </head><body><article class="markdown-body">
                {html}
                </article>{ScrollScript}</body></html>
                """;
        }

        // Eigene HTML-Seiten bleiben unverändert; nur das Scroll-Skript kommt ans Ende
        var close = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return close >= 0 ? html.Insert(close, ScrollScript) : html + ScrollScript;
    }

    public static string MimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".avif" => "image/avif",
        ".svg" => "image/svg+xml",
        ".ico" => "image/x-icon",
        ".bmp" => "image/bmp",
        ".css" => "text/css",
        ".js" or ".mjs" => "text/javascript",
        ".json" => "application/json",
        ".mp4" => "video/mp4",
        ".webm" => "video/webm",
        ".mp3" => "audio/mpeg",
        ".wav" => "audio/wav",
        ".ogg" => "audio/ogg",
        ".woff" => "font/woff",
        ".woff2" => "font/woff2",
        ".ttf" => "font/ttf",
        _ => "application/octet-stream"
    };

    private static bool IsLocalReference(string value) =>
        value.Length > 0
        && !value.StartsWith('#')
        && !value.StartsWith("//", StringComparison.Ordinal)
        && !SchemeRegex().IsMatch(value);

    private static string AttributeValue(Match attribute) =>
        attribute.Groups["dq"].Success ? attribute.Groups["dq"].Value : attribute.Groups["sq"].Value;

    // Teilt dem Elternfenster die Scrollposition mit und stellt sie nach dem Neuaufbau wieder her (siehe preview.js)
    private const string ScrollScript = """
        <script>
        (() => {
          addEventListener("message", e => { if (e.data && typeof e.data.codesailScroll === "number") scrollTo(0, e.data.codesailScroll); });
          let t; addEventListener("scroll", () => { clearTimeout(t); t = setTimeout(() => parent.postMessage({ codesailScroll: scrollY }, "*"), 100); });
          parent.postMessage({ codesailReady: true }, "*");
          // Höhe melden, damit eingebettete Texte (z. B. PR-Beschreibungen) ohne eigenen Scrollbalken auskommen
          const h = () => parent.postMessage({ codesailHeight: document.documentElement.scrollHeight }, "*");
          addEventListener("load", h); new ResizeObserver(h).observe(document.documentElement);
        })();
        </script>
        """;

    private const string MarkdownCss = """
        :root { color-scheme: dark; }
        body { margin: 0; background: #1e1e1e; color: #d4d4d4; font: 16px/1.6 -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif; }
        .markdown-body { max-width: 860px; margin: 0 auto; padding: 24px 28px 64px; overflow-wrap: break-word; }
        h1, h2, h3, h4, h5, h6 { color: #f0f0f0; line-height: 1.25; margin: 1.5em 0 .6em; }
        h1, h2 { padding-bottom: .3em; border-bottom: 1px solid #3c3c3c; }
        h1 { font-size: 2em; } h2 { font-size: 1.5em; } h3 { font-size: 1.25em; }
        a { color: #7dd3fc; }
        code, pre { font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; font-size: .9em; }
        code { background: #2d2d2d; padding: .15em .4em; border-radius: 4px; }
        pre { background: #252526; padding: 14px 16px; border-radius: 6px; overflow-x: auto; }
        pre code { background: none; padding: 0; }
        blockquote { margin: 0; padding: 0 1em; color: #a0a0a0; border-left: 4px solid #3c3c3c; }
        table { border-collapse: collapse; display: block; overflow-x: auto; }
        th, td { border: 1px solid #3c3c3c; padding: 6px 13px; }
        tr:nth-child(2n) { background: #252526; }
        img { max-width: 100%; }
        hr { border: 0; border-top: 1px solid #3c3c3c; margin: 24px 0; }
        ul.contains-task-list { list-style: none; padding-left: 1.2em; }
        """;

    [GeneratedRegex("""<(?<tag>img|source|video|audio|link|script)\b[^>]*>""", RegexOptions.IgnoreCase)]
    private static partial Regex TagRegex();

    [GeneratedRegex("""\b(?<name>src|href)\s*=\s*(?:"(?<dq>[^"]*)"|'(?<sq>[^']*)')""", RegexOptions.IgnoreCase)]
    private static partial Regex AttributeRegex();

    [GeneratedRegex("""\brel\s*=\s*["']?[^"'>]*stylesheet""", RegexOptions.IgnoreCase)]
    private static partial Regex StylesheetRegex();

    [GeneratedRegex("^[a-zA-Z][a-zA-Z0-9+.-]*:")]
    private static partial Regex SchemeRegex();
}

public enum PreviewKind
{
    None,
    Markdown,
    Html
}
