namespace Verrdoss.Core;

public static class ContainerLayout
{
    public const string Magic = "VERRDOSS";
    public const ushort Version = 1;
    public const int MaxChunkSize = 1024 * 1024;
    public const ushort FlagHasFolderPassword = 1;

    public const int MagicLength = 8;
    public const int FixedHeaderSize = 208;
    public const int WrapRegionEnd = 176;

    public const string Extension = ".verrdoss";
    public const string PartialSuffix = ".partial";
}
