namespace Verrdoss.Core;

public static class EntryPath
{
    public static string NormalizeRelative(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative))
            throw new VaultException("Chemin de fichier vide dans le conteneur.");

        var replaced = relative.Replace('\\', '/').Trim();
        if (replaced.Contains(':') || replaced.StartsWith('/') || replaced.StartsWith("//"))
            throw new VaultException("Chemin absolu refusé dans le conteneur.");

        var parts = replaced.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(part => part == "." || part == ".."))
            throw new VaultException("Chemin qui sort du dossier refusé.");

        return string.Join('/', parts);
    }

    public static string CombineUnderRoot(string root, string relative)
    {
        var normalized = NormalizeRelative(relative);
        var rootFull = Path.GetFullPath(root);
        var combined = Path.GetFullPath(Path.Combine(rootFull, normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsInside(combined, rootFull))
            throw new VaultException("Chemin qui sort du dossier refusé.");
        return combined;
    }

    public static bool IsInside(string path, string parent)
    {
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var root = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (full.Equals(root, StringComparison.OrdinalIgnoreCase))
            return true;
        return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
