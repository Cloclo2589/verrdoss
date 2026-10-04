namespace Verrdoss.Core;

public enum VaultItemKind
{
    Directory = 1,
    File = 2
}

public sealed class VaultItem
{
    public string RelativePath { get; set; } = "";
    public VaultItemKind Kind { get; set; }
    public long Length { get; set; }
    public long CreatedUtcTicks { get; set; }
    public long ModifiedUtcTicks { get; set; }
    public int Attributes { get; set; }
}

public sealed class ContainerHeader
{
    public KdfParameters Kdf { get; set; } = KdfParameters.Fast;
    public ushort Flags { get; set; }
    public byte[] MasterSalt { get; set; } = new byte[16];
    public byte[] MasterWrap { get; set; } = new byte[KeyWrap.WrapLength];
    public byte[] FolderSalt { get; set; } = new byte[16];
    public byte[] FolderWrap { get; set; } = new byte[KeyWrap.WrapLength];

    public bool HasFolderPassword => (Flags & ContainerLayout.FlagHasFolderPassword) != 0;

    public ContainerHeader Clone()
    {
        return new ContainerHeader
        {
            Kdf = Kdf,
            Flags = Flags,
            MasterSalt = (byte[])MasterSalt.Clone(),
            MasterWrap = (byte[])MasterWrap.Clone(),
            FolderSalt = (byte[])FolderSalt.Clone(),
            FolderWrap = (byte[])FolderWrap.Clone()
        };
    }
}
