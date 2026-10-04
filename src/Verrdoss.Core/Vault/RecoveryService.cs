namespace Verrdoss.Core;

public static class RecoveryService
{
    public static IReadOnlyList<string> Recover(AppStore store)
    {
        var journal = Journal.Open(store.JournalPath);
        var messages = new List<string>();
        foreach (var entry in journal.Entries.ToList())
        {
            if (entry.Op == "lock")
                RecoverLock(store, entry, messages);
            else if (entry.Op == "unlock")
                RecoverUnlock(store, entry, messages);
            journal.Remove(entry);
        }
        Reconcile(store, messages);
        return messages;
    }

    private static void RecoverLock(AppStore store, JournalEntry entry, List<string> messages)
    {
        TryDeleteFile(entry.PartialPath);
        var name = Path.GetFileName(entry.OriginalPath);
        var containerOk = !string.IsNullOrWhiteSpace(entry.ContainerSha256)
            && File.Exists(entry.ContainerPath)
            && string.Equals(VaultContainer.Sha256(entry.ContainerPath), entry.ContainerSha256, StringComparison.OrdinalIgnoreCase);

        if (entry.Phase == "container-ready" && containerOk)
        {
            if (Directory.Exists(entry.OriginalPath))
                FolderScanner.DeleteTree(entry.OriginalPath);
            Upsert(store, entry, VaultStates.Locked);
            messages.Add($"Verrouillage terminé après interruption : {name}.");
            return;
        }

        if (Directory.Exists(entry.OriginalPath) && File.Exists(entry.ContainerPath))
        {
            messages.Add($"Le dossier et le conteneur existent tous les deux pour {name}. Rien n'a été supprimé.");
            return;
        }

        if (Directory.Exists(entry.OriginalPath))
            messages.Add($"Verrouillage incomplet annulé pour {name}. Le dossier d'origine est conservé.");
        else if (containerOk)
        {
            Upsert(store, entry, VaultStates.Locked);
            messages.Add($"Le dossier {name} est déjà chiffré.");
        }
    }

    private static void RecoverUnlock(AppStore store, JournalEntry entry, List<string> messages)
    {
        var name = Path.GetFileName(entry.OriginalPath);
        var originalExists = Directory.Exists(entry.OriginalPath);
        var restoreExists = !string.IsNullOrWhiteSpace(entry.RestorePath) && Directory.Exists(entry.RestorePath);

        if (entry.Phase == "restored" && originalExists)
        {
            TryDeleteFile(entry.ContainerPath, revealFirst: true);
            if (restoreExists)
                FolderScanner.DeleteTree(entry.RestorePath);
            Upsert(store, entry, VaultStates.Unlocked);
            messages.Add($"Déverrouillage terminé après interruption : {name}.");
            return;
        }

        if (entry.Phase == "restoring" && originalExists && !restoreExists)
        {
            TryDeleteFile(entry.ContainerPath, revealFirst: true);
            Upsert(store, entry, VaultStates.Unlocked);
            messages.Add($"Déverrouillage terminé après interruption : {name}.");
            return;
        }

        if (restoreExists)
            FolderScanner.DeleteTree(entry.RestorePath);

        if (File.Exists(entry.ContainerPath))
        {
            Upsert(store, entry, VaultStates.Locked);
            messages.Add($"Déverrouillage incomplet annulé pour {name}. Le conteneur est conservé.");
        }
        else if (originalExists)
        {
            Upsert(store, entry, VaultStates.Unlocked);
            messages.Add($"Le dossier {name} est déjà en clair.");
        }
        else
        {
            messages.Add($"Dossier introuvable après interruption : {name}.");
        }
    }

    private static void Reconcile(AppStore store, List<string> messages)
    {
        foreach (var vault in store.Data.Vaults)
        {
            var folder = Directory.Exists(vault.OriginalPath);
            var container = File.Exists(vault.ContainerPath);
            if (folder && container)
            {
                if (LockedFolder.IsPlaceholder(vault.OriginalPath))
                {
                    if (vault.State != VaultStates.Locked)
                    {
                        vault.State = VaultStates.Locked;
                        store.Save();
                    }
                }
                else
                {
                    messages.Add($"Le dossier et le conteneur existent tous les deux pour {Path.GetFileName(vault.OriginalPath)}. Rien n'a été supprimé.");
                }
            }
            else if (container && vault.State != VaultStates.Locked)
            {
                vault.State = VaultStates.Locked;
                store.Save();
            }
            else if (folder && vault.State != VaultStates.Unlocked)
            {
                vault.State = VaultStates.Unlocked;
                store.Save();
            }
            else if (!folder && !container)
            {
                messages.Add($"Dossier introuvable : {vault.OriginalPath}.");
            }
        }
    }

    private static void Upsert(AppStore store, JournalEntry entry, string state)
    {
        store.Upsert(new VaultRecord
        {
            Id = string.IsNullOrWhiteSpace(entry.VaultId) ? Guid.NewGuid().ToString("N") : entry.VaultId,
            OriginalPath = entry.OriginalPath,
            ContainerPath = entry.ContainerPath,
            HasFolderPassword = entry.HasFolderPassword,
            State = state
        });
    }

    private static void TryDeleteFile(string? path, bool revealFirst = false)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;
        if (revealFirst)
            FolderScanner.RevealContainer(path);
        File.Delete(path);
    }
}
