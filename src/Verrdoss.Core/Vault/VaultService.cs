using System.Security.Cryptography;

namespace Verrdoss.Core;

public sealed class SessionVault : IDisposable
{
    private bool _disposed;

    public SessionVault(string id, byte[] dek, ContainerHeader header)
    {
        Id = id;
        Dek = dek;
        Header = header;
    }

    public string Id { get; }
    public byte[] Dek { get; }
    public ContainerHeader Header { get; private set; }

    public void ReplaceHeader(ContainerHeader header) => Header = header;

    public void Dispose()
    {
        if (_disposed)
            return;
        Argon2Kdf.Clear(Dek);
        _disposed = true;
    }
}

public sealed class VaultService
{
    private readonly Dictionary<string, SessionVault> _open = new(StringComparer.Ordinal);

    public VaultService(string dataDirectory, string applicationDirectory, KdfParameters? kdf = null)
    {
        Store = AppStore.Open(dataDirectory);
        ApplicationDirectory = applicationDirectory;
        Kdf = kdf ?? KdfParameters.Production;
    }

    public AppStore Store { get; }
    public string ApplicationDirectory { get; }
    public KdfParameters Kdf { get; }
    public string ShellExecutable { get; set; } = Environment.ProcessPath ?? "";
    public string? LockIconPath { get; set; }
    public int OpenCount => _open.Count;

    internal Action<string>? AfterPartialWrittenForTests { get; set; }

    public bool IsOpen(string vaultId) => _open.ContainsKey(vaultId);

    public string Describe(VaultRecord record)
    {
        var folder = Directory.Exists(record.OriginalPath);
        var container = File.Exists(record.ContainerPath);
        var placeholder = folder && LockedFolder.IsPlaceholder(record.OriginalPath);
        if (container && (!folder || placeholder))
            return "Verrouillé";
        if (folder && container)
            return "Conflit";
        if (folder && IsOpen(record.Id))
            return "Déverrouillé";
        if (folder)
            return "Déverrouillé (clé absente)";
        return "Introuvable";
    }

    public IReadOnlyList<string> EnsurePlaceholders()
    {
        var created = new List<string>();
        foreach (var vault in Store.Data.Vaults)
        {
            if (!File.Exists(vault.ContainerPath) || File.Exists(vault.OriginalPath))
                continue;
            try
            {
                if (!Directory.Exists(vault.OriginalPath))
                {
                    LockedFolder.Create(vault.OriginalPath, ShellExecutable, LockIconPath);
                    created.Add(vault.OriginalPath);
                }
                else if (LockedFolder.NeedsRefresh(vault.OriginalPath))
                {
                    LockedFolder.Refresh(vault.OriginalPath, LockIconPath);
                    created.Add(vault.OriginalPath);
                }
            }
            catch (VaultException)
            {
            }
        }
        return created;
    }

    public VaultRecord LockNew(string folder, byte[]? masterPassword, byte[]? folderPassword)
    {
        if (masterPassword is { Length: > 0 })
            EnsureMaster(masterPassword);
        EnsureFolderPassword(folderPassword);
        var full = Path.GetFullPath(folder);
        var existing = Store.FindByOriginalPath(full);
        if (existing != null && IsOpen(existing.Id))
            return LockOpen(existing.Id);
        if (existing != null && File.Exists(existing.ContainerPath) &&
            (!Directory.Exists(existing.OriginalPath) || LockedFolder.IsPlaceholder(existing.OriginalPath)))
            throw new VaultException("Ce dossier est déjà verrouillé.");

        var container = existing?.ContainerPath ?? ContainerPathFor(full);
        if (File.Exists(container))
            throw new VaultException("Un conteneur existe déjà à côté de ce dossier.");

        var id = existing?.Id ?? Guid.NewGuid().ToString("N");
        return LockCore(id, full, container, masterPassword, folderPassword, existingHeader: null, existingDek: null);
    }

    public VaultRecord LockOpen(string vaultId)
    {
        if (!_open.TryGetValue(vaultId, out var session))
            throw new VaultException("La clé de ce dossier n'est plus en mémoire. Saisissez le mot de passe pour le reverrouiller.");
        var record = Require(vaultId);
        if (!Directory.Exists(record.OriginalPath))
            throw new VaultException("Le dossier en clair est introuvable.");
        if (File.Exists(record.ContainerPath))
            throw new VaultException("Le conteneur existe déjà.");

        var locked = LockCore(record.Id, record.OriginalPath, record.ContainerPath, masterPassword: null, folderPassword: null, session.Header, session.Dek);
        _open.Remove(vaultId);
        session.Dispose();
        return locked;
    }

    public VaultRecord RelockWithSecret(string vaultId, byte[] password)
    {
        var record = Require(vaultId);
        if (IsOpen(vaultId))
            return LockOpen(vaultId);
        if (record.Header == null)
            throw new VaultException("Le mot de passe de secours est requis pour reverrouiller ce dossier.");
        if (!Directory.Exists(record.OriginalPath))
            throw new VaultException("Le dossier en clair est introuvable.");
        if (File.Exists(record.ContainerPath))
            throw new VaultException("Le conteneur existe déjà.");

        var header = record.Header.ToHeader();
        var dek = VaultContainer.TryUnwrap(header, password) ?? throw new VaultException("Mot de passe incorrect.");
        try
        {
            return LockCore(record.Id, record.OriginalPath, record.ContainerPath, masterPassword: null, folderPassword: null, header, dek);
        }
        finally
        {
            Argon2Kdf.Clear(dek);
        }
    }

    public VaultRecord Relock(string vaultId, byte[]? masterPassword, byte[]? folderPassword)
    {
        if (masterPassword is { Length: > 0 })
            EnsureMaster(masterPassword);
        EnsureFolderPassword(folderPassword);
        var record = Require(vaultId);
        if (IsOpen(vaultId))
            return LockOpen(vaultId);
        if (!Directory.Exists(record.OriginalPath))
            throw new VaultException("Le dossier en clair est introuvable.");
        if (File.Exists(record.ContainerPath))
            throw new VaultException("Le conteneur existe déjà.");
        return LockCore(record.Id, record.OriginalPath, record.ContainerPath, masterPassword, folderPassword, existingHeader: null, existingDek: null);
    }

    public IReadOnlyList<string> LockAllInSession()
    {
        var errors = new List<string>();
        foreach (var id in _open.Keys.ToList())
        {
            var name = Path.GetFileName(Require(id).OriginalPath);
            try
            {
                LockOpen(id);
            }
            catch (Exception ex)
            {
                errors.Add($"{name} : {ex.Message}");
            }
        }
        return errors;
    }

    public IReadOnlyList<VaultRecord> UnlockedWithoutKey()
    {
        return Store.Data.Vaults
            .Where(vault => Directory.Exists(vault.OriginalPath) && !File.Exists(vault.ContainerPath) && !IsOpen(vault.Id))
            .ToList();
    }

    public VaultRecord Unlock(string vaultId, byte[] password)
    {
        var record = Require(vaultId);
        if (IsOpen(vaultId) || (Directory.Exists(record.OriginalPath) && !File.Exists(record.ContainerPath)))
            throw new VaultException("Ce dossier est déjà déverrouillé.");
        if (!File.Exists(record.ContainerPath))
            throw new VaultException("Conteneur introuvable.");

        var header = VaultContainer.ReadHeader(record.ContainerPath);
        var dek = VaultContainer.TryUnwrap(header, password);
        if (dek == null)
        {
            if (!header.MasterWrap.Any(value => value != 0) && Store.CheckMaster(password))
                throw new VaultException("Ce dossier n'a pas encore le mot de passe de secours. Ouvrez-le une fois avec son mot de passe, puis verrouillez-le : le secours fonctionnera ensuite.");
            throw new VaultException("Mot de passe incorrect.");
        }

        var parent = Path.GetDirectoryName(record.OriginalPath) ?? throw new VaultException("Emplacement invalide.");
        var restore = Path.Combine(parent, Path.GetFileName(record.OriginalPath) + ".verrdoss.restore");
        if (Directory.Exists(restore))
            FolderScanner.DeleteTree(restore);

        var journal = Journal.Open(Store.JournalPath);
        var entry = new JournalEntry
        {
            Op = "unlock",
            Phase = "restoring",
            VaultId = record.Id,
            OriginalPath = record.OriginalPath,
            ContainerPath = record.ContainerPath,
            RestorePath = restore,
            HasFolderPassword = header.HasFolderPassword
        };
        journal.Replace(entry);

        var restored = false;
        var stored = false;
        try
        {
            VaultContainer.Restore(record.ContainerPath, restore, dek);
            if (LockedFolder.IsPlaceholder(record.OriginalPath))
                LockedFolder.Remove(record.OriginalPath);
            if (Directory.Exists(record.OriginalPath))
                throw new VaultException("Le dossier d'origine contient autre chose que le cadenas VerrDoss. Rien n'a été écrasé.");
            Directory.Move(restore, record.OriginalPath);
            restored = true;
            entry.Phase = "restored";
            journal.Replace(entry);
            FolderScanner.RevealContainer(record.ContainerPath);
            File.Delete(record.ContainerPath);
            record.State = VaultStates.Unlocked;
            record.HasFolderPassword = header.HasFolderPassword;
            record.Header = HeaderSnapshot.From(header);
            Store.Upsert(record);
            _open[record.Id] = new SessionVault(record.Id, dek, header);
            stored = true;
            journal.Remove(entry);
            return record;
        }
        catch (VaultException)
        {
            CleanupFailedUnlock(restored, stored, dek, restore, journal, entry);
            throw;
        }
        catch (Exception ex)
        {
            CleanupFailedUnlock(restored, stored, dek, restore, journal, entry);
            throw new VaultException("Le déverrouillage a échoué. Le conteneur est conservé.", ex);
        }
    }

    public void AttachRecovery(byte[] recoveryPassword)
    {
        if (!Store.CheckMaster(recoveryPassword))
            return;
        foreach (var pair in _open.ToList())
        {
            if (pair.Value.Header.MasterWrap.Any(value => value != 0))
                continue;

            VaultContainer.RewrapMaster(pair.Value.Header, pair.Value.Dek, recoveryPassword);
            var record = Require(pair.Key);
            record.Header = HeaderSnapshot.From(pair.Value.Header);
            Store.Upsert(record);
        }
    }

    public void RemoveProtection(string vaultId)
    {
        var record = Require(vaultId);
        if (File.Exists(record.ContainerPath) || !Directory.Exists(record.OriginalPath))
            throw new VaultException("Déverrouillez le dossier avant de retirer la protection.");
        if (_open.Remove(vaultId, out var session))
            session.Dispose();
        Store.Remove(vaultId);
    }

    public void SetFolderPassword(string vaultId, byte[]? passwordForUnwrap, byte[]? newFolderPassword)
    {
        EnsureFolderPassword(newFolderPassword);
        var record = Require(vaultId);
        if (_open.TryGetValue(vaultId, out var session))
        {
            VaultContainer.SetFolderPassword(session.Header, session.Dek, newFolderPassword);
            record.HasFolderPassword = session.Header.HasFolderPassword;
            record.State = VaultStates.Unlocked;
            Store.Upsert(record);
            return;
        }

        if (!File.Exists(record.ContainerPath))
            throw new VaultException("Conteneur introuvable. Déverrouillez le dossier pour changer son mot de passe.");
        if (passwordForUnwrap == null)
            throw new VaultException("Mot de passe requis.");

        var header = VaultContainer.ReadHeader(record.ContainerPath);
        var dek = VaultContainer.TryUnwrap(header, passwordForUnwrap) ?? throw new VaultException("Mot de passe incorrect.");
        try
        {
            VaultContainer.SetFolderPassword(header, dek, newFolderPassword);
            VaultContainer.WriteWrapRegion(record.ContainerPath, header);
            record.HasFolderPassword = header.HasFolderPassword;
            record.State = VaultStates.Locked;
            Store.Upsert(record);
        }
        finally
        {
            Argon2Kdf.Clear(dek);
        }
    }

    public void ChangeMasterPassword(byte[] currentPassword, byte[] newPassword)
    {
        if (!Store.CheckMaster(currentPassword))
            throw new VaultException("Mot de passe maître incorrect.");
        if (newPassword.Length < 8)
            throw new VaultException("Le mot de passe maître doit contenir au moins 8 caractères.");

        var rewritten = new List<(string Path, ContainerHeader Previous)>();
        var openPrevious = _open.ToDictionary(pair => pair.Key, pair => pair.Value.Header.Clone());
        try
        {
            foreach (var vault in Store.Data.Vaults.Where(candidate => File.Exists(candidate.ContainerPath)))
            {
                var header = VaultContainer.ReadHeader(vault.ContainerPath);
                var previous = header.Clone();
                var dek = VaultContainer.TryUnwrap(header, currentPassword);
                if (dek == null)
                    continue;
                try
                {
                    VaultContainer.RewrapMaster(header, dek, newPassword);
                    VaultContainer.WriteWrapRegion(vault.ContainerPath, header);
                    rewritten.Add((vault.ContainerPath, previous));
                }
                finally
                {
                    Argon2Kdf.Clear(dek);
                }
            }

            foreach (var pair in _open)
            {
                VaultContainer.RewrapMaster(pair.Value.Header, pair.Value.Dek, newPassword);
                var openRecord = Require(pair.Key);
                openRecord.Header = HeaderSnapshot.From(pair.Value.Header);
                Store.Upsert(openRecord);
            }

            Store.SetMaster(newPassword, Kdf);
        }
        catch
        {
            foreach (var (path, previous) in rewritten)
            {
                try
                {
                    VaultContainer.WriteWrapRegion(path, previous);
                }
                catch (IOException)
                {
                }
            }

            foreach (var pair in openPrevious)
            {
                if (_open.TryGetValue(pair.Key, out var session))
                    session.ReplaceHeader(pair.Value);
            }

            throw;
        }
    }

    public static string ContainerPathFor(string folder)
    {
        var full = Path.GetFullPath(folder);
        var parent = Path.GetDirectoryName(full) ?? throw new VaultException("Emplacement invalide.");
        return Path.Combine(parent, Path.GetFileName(full) + ContainerLayout.Extension);
    }

    private VaultRecord LockCore(
        string id,
        string folder,
        string containerPath,
        byte[]? masterPassword,
        byte[]? folderPassword,
        ContainerHeader? existingHeader,
        byte[]? existingDek)
    {
        FolderGuard.EnsureSafe(folder, ApplicationDirectory, Store.DataDirectory);
        var busy = FolderScanner.FindBusyFiles(folder);
        if (busy.Count > 0)
        {
            var shown = string.Join(Environment.NewLine, busy.Take(12));
            var extra = busy.Count > 12 ? $"{Environment.NewLine}… et {busy.Count - 12} autre(s)." : "";
            throw new VaultException("Des fichiers sont ouverts par un autre programme :" + Environment.NewLine + shown + extra);
        }

        var items = FolderScanner.Scan(folder);
        byte[] dek;
        ContainerHeader header;
        var ownsDek = false;
        if (existingDek != null && existingHeader != null)
        {
            dek = existingDek;
            header = existingHeader.Clone();
        }
        else
        {
            dek = RandomNumberGenerator.GetBytes(KeyWrap.DekLength);
            ownsDek = true;
            header = VaultContainer.CreateHeader(dek, masterPassword, folderPassword, Kdf);
        }

        var partial = containerPath + ContainerLayout.PartialSuffix;
        var journal = Journal.Open(Store.JournalPath);
        var entry = new JournalEntry
        {
            Op = "lock",
            Phase = "writing",
            VaultId = id,
            OriginalPath = Path.GetFullPath(folder),
            ContainerPath = containerPath,
            PartialPath = partial,
            HasFolderPassword = header.HasFolderPassword
        };
        journal.Replace(entry);

        var committed = false;
        try
        {
            if (File.Exists(partial))
                File.Delete(partial);
            VaultContainer.WriteFromDirectory(partial, header, dek, folder, items);
            AfterPartialWrittenForTests?.Invoke(partial);
            VaultContainer.Verify(partial, dek);
            File.Move(partial, containerPath, overwrite: false);
            FolderScanner.HideContainer(containerPath);
            entry.ContainerSha256 = VaultContainer.Sha256(containerPath);
            entry.Phase = "container-ready";
            journal.Replace(entry);
            committed = true;
            FolderScanner.DeleteTree(folder);
            var record = new VaultRecord
            {
                Id = id,
                OriginalPath = Path.GetFullPath(folder),
                ContainerPath = containerPath,
                HasFolderPassword = header.HasFolderPassword,
                State = VaultStates.Locked
            };
            Store.Upsert(record);
            journal.Remove(entry);
            LockedFolder.Create(record.OriginalPath, ShellExecutable, LockIconPath);
            return record;
        }
        catch (VaultException)
        {
            if (!committed)
                AbortLock(partial, journal, entry);
            throw;
        }
        catch (Exception ex)
        {
            if (!committed)
            {
                AbortLock(partial, journal, entry);
                throw new VaultException("Le verrouillage a échoué. Le dossier d'origine est conservé.", ex);
            }

            throw new VaultException("Le conteneur est valide, mais la suppression du dossier en clair n'a pas abouti. Relancez VerrDoss pour terminer.", ex);
        }
        finally
        {
            if (ownsDek)
                Argon2Kdf.Clear(dek);
        }
    }

    private static void AbortLock(string partial, Journal journal, JournalEntry entry)
    {
        if (File.Exists(partial))
        {
            File.SetAttributes(partial, FileAttributes.Normal);
            File.Delete(partial);
        }
        journal.Remove(entry);
    }

    private static void CleanupFailedUnlock(bool restored, bool stored, byte[] dek, string restore, Journal journal, JournalEntry entry)
    {
        if (!restored)
        {
            if (Directory.Exists(restore))
                FolderScanner.DeleteTree(restore);
            journal.Remove(entry);
        }
        if (!stored)
            Argon2Kdf.Clear(dek);
    }

    private void EnsureMaster(byte[] masterPassword)
    {
        if (!Store.HasMaster)
            throw new VaultException("Créez d'abord un mot de passe maître.");
        if (!Store.CheckMaster(masterPassword))
            throw new VaultException("Mot de passe maître incorrect.");
    }

    private static void EnsureFolderPassword(byte[]? folderPassword)
    {
        if (folderPassword is { Length: > 0 and < 4 })
            throw new VaultException("Le mot de passe du dossier doit contenir au moins 4 caractères.");
    }

    private VaultRecord Require(string vaultId)
    {
        return Store.FindById(vaultId) ?? throw new VaultException("Dossier protégé introuvable.");
    }
}
