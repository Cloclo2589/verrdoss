namespace Verrdoss.Core;

public sealed class MasterRecord
{
    public int MemoryKiB { get; set; }
    public int Iterations { get; set; }
    public int Parallelism { get; set; }
    public string Salt { get; set; } = "";
    public string Hash { get; set; } = "";
}

public sealed class VaultRecord
{
    public string Id { get; set; } = "";
    public string OriginalPath { get; set; } = "";
    public string ContainerPath { get; set; } = "";
    public bool HasFolderPassword { get; set; }
    public string State { get; set; } = VaultStates.Locked;
}

public static class VaultStates
{
    public const string Locked = "locked";
    public const string Unlocked = "unlocked";
}

public sealed class StoreData
{
    public int Version { get; set; } = 1;
    public int IdleMinutes { get; set; } = IdlePolicy.DefaultMinutes;
    public MasterRecord? Master { get; set; }
    public List<VaultRecord> Vaults { get; set; } = new();
}

public sealed class JournalEntry
{
    public string Op { get; set; } = "";
    public string Phase { get; set; } = "";
    public string VaultId { get; set; } = "";
    public string OriginalPath { get; set; } = "";
    public string ContainerPath { get; set; } = "";
    public string PartialPath { get; set; } = "";
    public string RestorePath { get; set; } = "";
    public string ContainerSha256 { get; set; } = "";
    public bool HasFolderPassword { get; set; }
}

public sealed class JournalFile
{
    public List<JournalEntry> Entries { get; set; } = new();
}
