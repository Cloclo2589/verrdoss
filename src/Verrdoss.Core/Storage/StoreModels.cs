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
    public HeaderSnapshot? Header { get; set; }
}

public sealed class HeaderSnapshot
{
    public int MemoryKiB { get; set; }
    public int Iterations { get; set; }
    public int Parallelism { get; set; }
    public ushort Flags { get; set; }
    public string MasterSalt { get; set; } = "";
    public string MasterWrap { get; set; } = "";
    public string FolderSalt { get; set; } = "";
    public string FolderWrap { get; set; } = "";

    public static HeaderSnapshot From(ContainerHeader header)
    {
        return new HeaderSnapshot
        {
            MemoryKiB = header.Kdf.MemoryKiB,
            Iterations = header.Kdf.Iterations,
            Parallelism = header.Kdf.Parallelism,
            Flags = header.Flags,
            MasterSalt = Convert.ToBase64String(header.MasterSalt),
            MasterWrap = Convert.ToBase64String(header.MasterWrap),
            FolderSalt = Convert.ToBase64String(header.FolderSalt),
            FolderWrap = Convert.ToBase64String(header.FolderWrap)
        };
    }

    public ContainerHeader ToHeader()
    {
        return new ContainerHeader
        {
            Kdf = new KdfParameters(MemoryKiB, Iterations, Parallelism),
            Flags = Flags,
            MasterSalt = Convert.FromBase64String(MasterSalt),
            MasterWrap = Convert.FromBase64String(MasterWrap),
            FolderSalt = Convert.FromBase64String(FolderSalt),
            FolderWrap = Convert.FromBase64String(FolderWrap)
        };
    }
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
