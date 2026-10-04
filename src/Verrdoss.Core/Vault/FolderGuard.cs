namespace Verrdoss.Core;

public static class FolderGuard
{
    public static void EnsureSafe(string folder, string applicationDirectory, string dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(folder))
            throw new VaultException("Indiquez un dossier.");

        var full = Path.GetFullPath(folder);
        if (!Directory.Exists(full))
            throw new VaultException("Ce dossier est introuvable.");

        var root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root) || PathsEqual(full, root))
            throw new VaultException("La racine d'un lecteur ne peut pas être verrouillée.");

        if (IsReparsePoint(full))
            throw new VaultException("Les liens symboliques ne peuvent pas être verrouillés.");

        var windows = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        if (EntryPath.IsInside(full, windows) || EntryPath.IsInside(windows, full))
            throw new VaultException("Les dossiers Windows ne peuvent pas être verrouillés.");

        var app = Path.GetFullPath(applicationDirectory);
        if (EntryPath.IsInside(full, app) || EntryPath.IsInside(app, full))
            throw new VaultException("Le dossier de l'application ne peut pas être verrouillé.");

        var data = Path.GetFullPath(dataDirectory);
        if (EntryPath.IsInside(full, data) || EntryPath.IsInside(data, full))
            throw new VaultException("Le dossier de données de VerrDoss ne peut pas être verrouillé.");
    }

    public static bool IsReparsePoint(string path)
    {
        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }

    private static bool PathsEqual(string left, string right)
    {
        var a = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var b = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return a.Equals(b, StringComparison.OrdinalIgnoreCase);
    }
}
