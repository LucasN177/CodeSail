using CodeSail.Services.GitHub;

namespace CodeSail.Models;

/// <summary>Knoten im Datei-Baum eines Repositorys. <see cref="Path"/> ist relativ zur Repo-Wurzel.</summary>
public sealed class FileNode(string name, string path, bool isFolder)
{
    public string Name { get; } = name;
    public string Path { get; } = path;
    public bool IsFolder { get; } = isFolder;
    public List<FileNode> Children { get; } = [];
}

/// <param name="BlobShas">Pfad → Blob-SHA jeder Datei, um geänderte Dateien zu erkennen.</param>
public sealed record RepoTree(
    string Sha,
    IReadOnlyList<FileNode> Root,
    IReadOnlyList<string> FilePaths,
    IReadOnlyDictionary<string, string> BlobShas,
    bool Truncated)
{
    public static RepoTree FromEntries(string sha, IReadOnlyCollection<TreeEntry> entries, bool truncated)
    {
        var files = entries.Where(e => !e.IsFolder).ToList();
        return new RepoTree(
            sha,
            TreeBuilder.Build(entries),
            files.Select(e => e.Path).ToList(),
            files.Where(e => e.Sha is not null).ToDictionary(e => e.Path, e => e.Sha!),
            truncated);
    }
}
