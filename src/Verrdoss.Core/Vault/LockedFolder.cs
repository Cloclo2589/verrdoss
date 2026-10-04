#pragma warning disable CA1416
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace Verrdoss.Core;

public static class LockedFolder
{
    public const string Marker = "VerrdossLock=1";

    private static readonly string[] AllowedNames = ["desktop.ini", "target.lnk", "verrdoss.ico"];

    public static bool IsPlaceholder(string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
                return false;
            var ini = Path.Combine(folder, "desktop.ini");
            if (!File.Exists(ini))
                return false;
            var text = File.ReadAllText(ini);
            if (!text.Contains(Marker, StringComparison.Ordinal))
                return false;
            foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
            {
                var name = Path.GetFileName(entry);
                if (!AllowedNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static void Create(string folder, string executable, string? iconPath)
    {
        if (string.IsNullOrWhiteSpace(executable))
            throw new VaultException("Le programme VerrDoss est introuvable pour le double-clic.");
        if (IsPlaceholder(folder))
            return;
        if (Directory.Exists(folder) || File.Exists(folder))
            throw new VaultException("Impossible d'afficher le dossier verrouillé : l'emplacement est déjà occupé.");

        try
        {
            Directory.CreateDirectory(folder);
            WriteAppearance(folder, iconPath);
            Protect(folder);
        }
        catch (VaultException)
        {
            TryDeletePartial(folder);
            throw;
        }
        catch (Exception ex)
        {
            TryDeletePartial(folder);
            throw new VaultException("Le dossier est chiffré, mais l'icône cadenas n'a pas pu être affichée.", ex);
        }
    }

    public static bool NeedsRefresh(string folder)
    {
        if (!IsPlaceholder(folder))
            return false;
        try
        {
            if (File.Exists(Path.Combine(folder, "target.lnk")))
                return true;
            return File.ReadAllText(Path.Combine(folder, "desktop.ini")).Contains("CLSID2", StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static void Refresh(string folder, string? iconPath)
    {
        MakeDeletable(folder);
        WriteAppearance(folder, iconPath);
        Protect(folder);
    }

    public static void Remove(string folder)
    {
        if (!Directory.Exists(folder))
            return;
        FolderScanner.DeleteTree(folder);
    }

    public static void MakeDeletable(string root)
    {
        if (!Directory.Exists(root))
            return;

        List<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories).ToList();
        }
        catch (IOException)
        {
            entries = new List<string>();
        }
        catch (UnauthorizedAccessException)
        {
            entries = new List<string>();
        }

        entries.Add(root);
        foreach (var entry in entries)
        {
            try
            {
                if (Directory.Exists(entry))
                    GrantFullControl(new DirectoryInfo(entry));
                else if (File.Exists(entry))
                    GrantFullControl(new FileInfo(entry));
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException)
            {
            }

            try
            {
                File.SetAttributes(entry, Directory.Exists(entry) ? FileAttributes.Directory : FileAttributes.Normal);
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException)
            {
            }
        }
    }

    private static void Protect(string folder)
    {
        var user = WindowsIdentity.GetCurrent().User ?? throw new VaultException("Utilisateur Windows introuvable.");
        var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var blocked = FileSystemRights.WriteData
            | FileSystemRights.CreateFiles
            | FileSystemRights.CreateDirectories
            | FileSystemRights.AppendData
            | FileSystemRights.Delete
            | FileSystemRights.DeleteSubdirectoriesAndFiles
            | FileSystemRights.WriteAttributes
            | FileSystemRights.WriteExtendedAttributes;
        var inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

        var directory = new DirectorySecurity();
        directory.SetAccessRuleProtection(true, false);
        directory.AddAccessRule(Allow(system, FileSystemRights.FullControl, inherit));
        directory.AddAccessRule(Allow(admins, FileSystemRights.FullControl, inherit));
        directory.AddAccessRule(Allow(user, FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize | FileSystemRights.ReadPermissions | FileSystemRights.ChangePermissions, inherit));
        directory.AddAccessRule(Deny(users, blocked, inherit));
        directory.AddAccessRule(Deny(user, blocked, inherit));
        new DirectoryInfo(folder).SetAccessControl(directory);

        foreach (var file in Directory.EnumerateFiles(folder))
        {
            var security = new FileSecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(Allow(system, FileSystemRights.FullControl, InheritanceFlags.None));
            security.AddAccessRule(Allow(admins, FileSystemRights.FullControl, InheritanceFlags.None));
            security.AddAccessRule(Allow(user, FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize | FileSystemRights.ReadPermissions | FileSystemRights.ChangePermissions, InheritanceFlags.None));
            security.AddAccessRule(Deny(users, blocked, InheritanceFlags.None));
            security.AddAccessRule(Deny(user, blocked, InheritanceFlags.None));
            new FileInfo(file).SetAccessControl(security);
        }
    }

    private static FileSystemAccessRule Allow(IdentityReference identity, FileSystemRights rights, InheritanceFlags inherit)
        => new(identity, rights, inherit, PropagationFlags.None, AccessControlType.Allow);

    private static FileSystemAccessRule Deny(IdentityReference identity, FileSystemRights rights, InheritanceFlags inherit)
        => new(identity, rights, inherit, PropagationFlags.None, AccessControlType.Deny);

    private static void GrantFullControl(FileSystemInfo info)
    {
        var user = WindowsIdentity.GetCurrent().User;
        if (user == null)
            return;
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var inherit = info is DirectoryInfo ? InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit : InheritanceFlags.None;
        FileSystemSecurity security = info is DirectoryInfo ? new DirectorySecurity() : new FileSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(Allow(user, FileSystemRights.FullControl, inherit));
        security.AddAccessRule(Allow(system, FileSystemRights.FullControl, inherit));
        security.AddAccessRule(Allow(admins, FileSystemRights.FullControl, inherit));
        if (info is DirectoryInfo directory)
            directory.SetAccessControl((DirectorySecurity)security);
        else
            ((FileInfo)info).SetAccessControl((FileSecurity)security);
    }

    private static void WriteAppearance(string folder, string? iconPath)
    {
        var icon = Path.Combine(folder, "verrdoss.ico");
        if (File.Exists(icon))
            File.SetAttributes(icon, FileAttributes.Normal);
        if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
            File.Copy(iconPath, icon, overwrite: true);
        else if (!File.Exists(icon))
            File.WriteAllBytes(icon, FallbackIcon());

        var iniPath = Path.Combine(folder, "desktop.ini");
        if (File.Exists(iniPath))
            File.SetAttributes(iniPath, FileAttributes.Normal);
        var ini = new StringBuilder();
        ini.AppendLine("[.ShellClassInfo]");
        ini.AppendLine(Marker);
        ini.AppendLine("ConfirmFileOp=0");
        ini.AppendLine("IconResource=" + icon + ",0");
        ini.AppendLine("InfoTip=Dossier verrouillé par VerrDoss. L'ouverture demande le mot de passe.");
        File.WriteAllText(iniPath, ini.ToString(), new UnicodeEncoding(false, true));

        var lnk = Path.Combine(folder, "target.lnk");
        if (File.Exists(lnk))
        {
            File.SetAttributes(lnk, FileAttributes.Normal);
            File.Delete(lnk);
        }

        File.SetAttributes(iniPath, FileAttributes.Hidden | FileAttributes.System);
        File.SetAttributes(icon, FileAttributes.Hidden | FileAttributes.System);
        File.SetAttributes(folder, FileAttributes.Directory | FileAttributes.ReadOnly);
    }

    private static void TryDeletePartial(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
                FolderScanner.DeleteTree(folder);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static byte[] FallbackIcon()
    {
        const int size = 16;
        var rows = new[]
        {
            "................",
            "................",
            ".....####.......",
            "....#....#......",
            "....#....#......",
            "....#....#......",
            "...########.....",
            "...########.....",
            "...###..###.....",
            "...###..###.....",
            "...####.###.....",
            "...########.....",
            "...########.....",
            "................",
            "................",
            "................"
        };
        var pixels = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var mark = rows[y][x];
                if (mark == '.')
                    continue;
                var offset = (y * size + x) * 4;
                if (mark == '#')
                {
                    pixels[offset] = 115;
                    pixels[offset + 1] = 75;
                    pixels[offset + 2] = 31;
                    pixels[offset + 3] = 255;
                }
                else
                {
                    pixels[offset] = 255;
                    pixels[offset + 1] = 255;
                    pixels[offset + 2] = 255;
                    pixels[offset + 3] = 255;
                }
            }
        }

        var xor = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
            Buffer.BlockCopy(pixels, (size - 1 - y) * size * 4, xor, y * size * 4, size * 4);
        var andMask = new byte[size * 4];
        var imageSize = 40 + xor.Length + andMask.Length;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)1);
        writer.Write((byte)size);
        writer.Write((byte)size);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(imageSize);
        writer.Write(22);
        writer.Write(40);
        writer.Write(size);
        writer.Write(size * 2);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(0);
        writer.Write(xor.Length + andMask.Length);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(xor);
        writer.Write(andMask);
        return stream.ToArray();
    }
}
