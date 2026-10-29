namespace CodeSail.Models;

/// <summary>Knoten im Datei-Baum eines Repositorys. <see cref="Path"/> ist relativ zur Repo-Wurzel.</summary>
public sealed class FileNode(string name, string path, bool isFolder)
{
    public string Name { get; } = name;
    public string Path { get; } = path;
    public bool IsFolder { get; } = isFolder;
    public List<FileNode> Children { get; } = [];
}

public sealed record RepoTree(string Sha, IReadOnlyList<FileNode> Root, IReadOnlyList<string> FilePaths, bool Truncated);
