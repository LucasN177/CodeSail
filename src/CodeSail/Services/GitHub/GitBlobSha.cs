using System.Security.Cryptography;
using System.Text;

namespace CodeSail.Services.GitHub;

/// <summary>Berechnet die Blob-SHA, die Git für einen Dateiinhalt vergibt ("git hash-object").</summary>
public static class GitBlobSha
{
    public static string Compute(string content)
    {
        var body = Encoding.UTF8.GetBytes(content);
        var header = Encoding.ASCII.GetBytes($"blob {body.Length}\0");
        return Convert.ToHexStringLower(SHA1.HashData([.. header, .. body]));
    }
}
