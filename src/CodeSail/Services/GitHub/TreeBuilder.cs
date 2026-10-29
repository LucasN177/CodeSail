using CodeSail.Models;

namespace CodeSail.Services.GitHub;

public readonly record struct TreeEntry(string Path, bool IsFolder);

/// <summary>Baut aus der flachen Liste der Git-Trees-API eine verschachtelte Ordnerstruktur.</summary>
public static class TreeBuilder
{
    public static List<FileNode> Build(IEnumerable<TreeEntry> entries)
    {
        var root = new FileNode("", "", isFolder: true);
        var folders = new Dictionary<string, FileNode> { [""] = root };

        // Kürzere Pfade zuerst, damit Elternordner vor ihren Kindern existieren
        foreach (var entry in entries.OrderBy(e => e.Path.Count(c => c == '/')))
        {
            if (entry.IsFolder && folders.ContainsKey(entry.Path))
                continue;

            var slash = entry.Path.LastIndexOf('/');
            var parent = GetOrCreateFolder(slash < 0 ? "" : entry.Path[..slash], folders);
            var node = new FileNode(entry.Path[(slash + 1)..], entry.Path, entry.IsFolder);

            if (entry.IsFolder)
                folders[entry.Path] = node;
            parent.Children.Add(node);
        }

        Sort(root);
        return root.Children;
    }

    private static FileNode GetOrCreateFolder(string path, Dictionary<string, FileNode> folders)
    {
        if (folders.TryGetValue(path, out var folder))
            return folder;

        // Fehlt ein Elternordner (z. B. bei abgeschnittenen Bäumen), wird er nachträglich angelegt
        var slash = path.LastIndexOf('/');
        var parent = GetOrCreateFolder(slash < 0 ? "" : path[..slash], folders);
        folder = new FileNode(path[(slash + 1)..], path, isFolder: true);
        parent.Children.Add(folder);
        folders[path] = folder;
        return folder;
    }

    private static void Sort(FileNode folder)
    {
        folder.Children.Sort((a, b) => a.IsFolder != b.IsFolder
            ? (a.IsFolder ? -1 : 1)
            : StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));

        foreach (var child in folder.Children.Where(c => c.IsFolder))
            Sort(child);
    }
}
