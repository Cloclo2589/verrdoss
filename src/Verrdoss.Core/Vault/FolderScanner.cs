namespace Verrdoss.Core;

public static class FolderScanner
{
    public static List<VaultItem> Scan(string root)
    {
        var items = new List<VaultItem>();
        Walk(root, root, items);
        return items;
    }

    public static IReadOnlyList<string> FindBusyFiles(string root)
    {
        var busy = new List<string>();
        foreach (var item in Scan(root).Where(i => i.Kind == VaultItemKind.File))
        {
            var full = EntryPath.CombineUnderRoot(root, item.RelativePath);
            try
            {
                using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.None);
            }
            catch (IOException)
            {
                busy.Add(full);
            }
            catch (UnauthorizedAccessException)
            {
                busy.Add(full);
            }
        }
        return busy;
    }

    public static void DeleteTree(string root)
    {
        if (!Directory.Exists(root))
            return;

        try
        {
            DeleteTreeCore(root);
        }
        catch (UnauthorizedAccessException)
        {
            LockedFolder.MakeDeletable(root);
            DeleteTreeCore(root);
        }
    }

    private static void DeleteTreeCore(string root)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
            File.SetAttributes(dir, FileAttributes.Directory);
        File.SetAttributes(root, FileAttributes.Directory);
        Directory.Delete(root, recursive: true);
    }

    public static void HideContainer(string path)
    {
        var attributes = File.GetAttributes(path);
        File.SetAttributes(path, attributes | FileAttributes.Hidden | FileAttributes.System);
    }

    public static void RevealContainer(string path)
    {
        var attributes = File.GetAttributes(path);
        attributes &= ~FileAttributes.Hidden;
        attributes &= ~FileAttributes.System;
        if (attributes == 0)
            attributes = FileAttributes.Normal;
        File.SetAttributes(path, attributes);
    }

    private static void Walk(string root, string current, List<VaultItem> items)
    {
        foreach (var entry in new DirectoryInfo(current).EnumerateFileSystemInfos())
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new VaultException($"Les liens symboliques ne peuvent pas être verrouillés : {entry.FullName}");

            var relative = Path.GetRelativePath(root, entry.FullName).Replace('\\', '/');
            if (entry is DirectoryInfo directory)
            {
                items.Add(new VaultItem
                {
                    RelativePath = relative,
                    Kind = VaultItemKind.Directory,
                    Length = 0,
                    CreatedUtcTicks = directory.CreationTimeUtc.Ticks,
                    ModifiedUtcTicks = directory.LastWriteTimeUtc.Ticks,
                    Attributes = (int)directory.Attributes
                });
                Walk(root, directory.FullName, items);
            }
            else if (entry is FileInfo file)
            {
                items.Add(new VaultItem
                {
                    RelativePath = relative,
                    Kind = VaultItemKind.File,
                    Length = file.Length,
                    CreatedUtcTicks = file.CreationTimeUtc.Ticks,
                    ModifiedUtcTicks = file.LastWriteTimeUtc.Ticks,
                    Attributes = (int)file.Attributes
                });
            }
        }
    }
}
