using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Verrdoss.Core;

public static class VaultContainer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static ContainerHeader CreateHeader(byte[] dek, byte[]? masterPassword, byte[]? folderPassword, KdfParameters kdf)
    {
        if (dek.Length != KeyWrap.DekLength)
            throw new VaultException("Clé de données invalide.");
        if ((masterPassword == null || masterPassword.Length == 0) && (folderPassword == null || folderPassword.Length == 0))
            throw new VaultException("Mot de passe du dossier requis.");

        var header = new ContainerHeader
        {
            Kdf = kdf,
            MasterSalt = RandomNumberGenerator.GetBytes(16),
            MasterWrap = new byte[KeyWrap.WrapLength],
            FolderSalt = new byte[16],
            FolderWrap = new byte[KeyWrap.WrapLength],
            Flags = 0
        };

        if (masterPassword is { Length: > 0 })
        {
            var masterKek = Argon2Kdf.Derive(masterPassword, header.MasterSalt, kdf);
            try
            {
                header.MasterWrap = KeyWrap.Wrap(masterKek, dek, "wrap-master");
            }
            finally
            {
                Argon2Kdf.Clear(masterKek);
            }
        }

        if (folderPassword is { Length: > 0 })
        {
            header.Flags = ContainerLayout.FlagHasFolderPassword;
            header.FolderSalt = RandomNumberGenerator.GetBytes(16);
            var folderKek = Argon2Kdf.Derive(folderPassword, header.FolderSalt, kdf);
            try
            {
                header.FolderWrap = KeyWrap.Wrap(folderKek, dek, "wrap-folder");
            }
            finally
            {
                Argon2Kdf.Clear(folderKek);
            }
        }

        return header;
    }

    public static void RewrapMaster(ContainerHeader header, byte[] dek, byte[] newMasterPassword)
    {
        header.MasterSalt = RandomNumberGenerator.GetBytes(16);
        var kek = Argon2Kdf.Derive(newMasterPassword, header.MasterSalt, header.Kdf);
        try
        {
            header.MasterWrap = KeyWrap.Wrap(kek, dek, "wrap-master");
        }
        finally
        {
            Argon2Kdf.Clear(kek);
        }
    }

    public static void SetFolderPassword(ContainerHeader header, byte[] dek, byte[]? folderPassword)
    {
        if (folderPassword is { Length: > 0 })
        {
            header.Flags |= ContainerLayout.FlagHasFolderPassword;
            header.FolderSalt = RandomNumberGenerator.GetBytes(16);
            var kek = Argon2Kdf.Derive(folderPassword, header.FolderSalt, header.Kdf);
            try
            {
                header.FolderWrap = KeyWrap.Wrap(kek, dek, "wrap-folder");
            }
            finally
            {
                Argon2Kdf.Clear(kek);
            }
        }
        else
        {
            header.Flags = (ushort)(header.Flags & ~ContainerLayout.FlagHasFolderPassword);
            header.FolderSalt = new byte[16];
            header.FolderWrap = new byte[KeyWrap.WrapLength];
        }
    }

    public static byte[]? TryUnwrap(ContainerHeader header, byte[] password)
    {
        if (header.HasFolderPassword)
        {
            var folderKek = Argon2Kdf.Derive(password, header.FolderSalt, header.Kdf);
            try
            {
                var dek = KeyWrap.TryUnwrap(folderKek, header.FolderWrap, "wrap-folder");
                if (dek != null)
                    return dek;
            }
            finally
            {
                Argon2Kdf.Clear(folderKek);
            }
        }

        var masterKek = Argon2Kdf.Derive(password, header.MasterSalt, header.Kdf);
        try
        {
            return KeyWrap.TryUnwrap(masterKek, header.MasterWrap, "wrap-master");
        }
        finally
        {
            Argon2Kdf.Clear(masterKek);
        }
    }

    public static void WriteFromDirectory(string path, ContainerHeader header, byte[] dek, string sourceRoot, IReadOnlyList<VaultItem> items)
    {
        Write(path, header, dek, items, item =>
        {
            var full = EntryPath.CombineUnderRoot(sourceRoot, item.RelativePath);
            return new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
        });
    }

    internal static void WriteRaw(string path, ContainerHeader header, byte[] dek, IReadOnlyList<VaultItem> items, Func<VaultItem, Stream> openFile)
    {
        Write(path, header, dek, items, openFile);
    }

    public static ContainerHeader ReadHeader(string path)
    {
        using var stream = OpenRead(path);
        return ReadHeader(stream);
    }

    public static void Verify(string path, byte[] dek)
    {
        using var stream = OpenRead(path);
        var items = ReadItems(stream, dek);
        ReadPayload(stream, dek, items, writeFiles: false, destination: null);
    }

    public static void Restore(string path, string destination, byte[] dek)
    {
        Directory.CreateDirectory(destination);
        using var stream = OpenRead(path);
        var items = ReadItems(stream, dek);
        foreach (var item in items)
            EntryPath.CombineUnderRoot(destination, item.RelativePath);
        ReadPayload(stream, dek, items, writeFiles: true, destination);
        foreach (var item in items.Where(entry => entry.Kind == VaultItemKind.Directory))
            ApplyTimes(EntryPath.CombineUnderRoot(destination, item.RelativePath), item);
    }

    public static void WriteWrapRegion(string path, ContainerHeader header)
    {
        var attributes = File.GetAttributes(path);
        var wasHidden = (attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;
        if (wasHidden)
            FolderScanner.RevealContainer(path);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var prefix = new byte[ContainerLayout.WrapRegionEnd];
            WriteHeaderPrefix(prefix, header);
            stream.Write(prefix, 0, prefix.Length);
            stream.Flush(true);
        }
        finally
        {
            if (wasHidden && File.Exists(path))
                FolderScanner.HideContainer(path);
        }
    }

    public static string Sha256(string path)
    {
        using var sha = SHA256.Create();
        using var stream = OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    private static void Write(string path, ContainerHeader header, byte[] dek, IReadOnlyList<VaultItem> items, Func<VaultItem, Stream> openFile)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        var prefix = new byte[ContainerLayout.WrapRegionEnd];
        WriteHeaderPrefix(prefix, header);
        stream.Write(prefix);

        var metadata = JsonSerializer.SerializeToUtf8Bytes(new MetadataDocument { Entries = items.ToList() }, JsonOptions);
        var metaNonce = RandomNumberGenerator.GetBytes(KeyWrap.NonceLength);
        var metaCipher = new byte[metadata.Length];
        var metaTag = new byte[KeyWrap.TagLength];
        AesGcmBox.Encrypt(dek, metaNonce, metadata, "metadata"u8, metaCipher, metaTag);
        CryptographicOperations.ZeroMemory(metadata);

        stream.Write(metaNonce);
        stream.Write(metaTag);
        WriteInt32(stream, metaCipher.Length);
        stream.Write(metaCipher);

        var fileIndex = 0;
        foreach (var item in items)
        {
            if (item.Kind != VaultItemKind.File)
                continue;

            using var input = openFile(item);
            var buffer = new byte[ContainerLayout.MaxChunkSize];
            if (item.Length == 0)
            {
                WriteChunk(stream, dek, fileIndex, 0, ReadOnlySpan<byte>.Empty);
            }
            else
            {
                var chunkIndex = 0;
                long remaining = item.Length;
                while (remaining > 0)
                {
                    var toRead = (int)Math.Min(buffer.Length, remaining);
                    var read = ReadExact(input, buffer, toRead);
                    if (read != toRead)
                        throw new VaultException("Lecture interrompue pendant le chiffrement.");
                    WriteChunk(stream, dek, fileIndex, chunkIndex, buffer.AsSpan(0, read));
                    CryptographicOperations.ZeroMemory(buffer.AsSpan(0, read));
                    remaining -= read;
                    chunkIndex++;
                }
            }

            fileIndex++;
        }

        stream.Flush(true);
    }

    private static List<VaultItem> ReadItems(Stream stream, byte[] dek)
    {
        stream.Position = ContainerLayout.WrapRegionEnd;
        Span<byte> metaNonce = stackalloc byte[KeyWrap.NonceLength];
        Span<byte> metaTag = stackalloc byte[KeyWrap.TagLength];
        ReadExact(stream, metaNonce);
        ReadExact(stream, metaTag);
        var metaLength = ReadInt32(stream);
        if (metaLength < 2 || metaLength > 64 * 1024 * 1024)
            throw new VaultException("Conteneur endommagé.");
        var metaCipher = new byte[metaLength];
        ReadExact(stream, metaCipher);
        byte[] metadata;
        try
        {
            metadata = AesGcmBox.Decrypt(dek, metaNonce, metaCipher, metaTag, "metadata"u8);
        }
        catch (AuthenticationTagMismatchException)
        {
            throw new VaultException("Conteneur endommagé ou clé incorrecte.");
        }

        MetadataDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<MetadataDocument>(metadata, JsonOptions);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(metadata);
        }

        if (document?.Entries == null)
            throw new VaultException("Conteneur endommagé.");
        return document.Entries;
    }

    private static void ReadPayload(Stream stream, byte[] dek, IReadOnlyList<VaultItem> items, bool writeFiles, string? destination)
    {
        var fileIndex = 0;
        foreach (var item in items)
        {
            if (item.Kind == VaultItemKind.Directory)
            {
                if (writeFiles)
                {
                    var dirPath = EntryPath.CombineUnderRoot(destination!, item.RelativePath);
                    Directory.CreateDirectory(dirPath);
                }
                continue;
            }

            if (item.Kind != VaultItemKind.File)
                throw new VaultException("Conteneur endommagé.");

            string? filePath = null;
            FileStream? output = null;
            try
            {
                if (writeFiles)
                {
                    filePath = EntryPath.CombineUnderRoot(destination!, item.RelativePath);
                    var parent = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(parent))
                        Directory.CreateDirectory(parent);
                    output = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                }

                if (item.Length == 0)
                {
                    var plainLength = ReadChunk(stream, dek, fileIndex, 0, output);
                    if (plainLength != 0)
                        throw new VaultException("Conteneur endommagé.");
                }
                else
                {
                    long remaining = item.Length;
                    var chunkIndex = 0;
                    while (remaining > 0)
                    {
                        var plainLength = ReadChunk(stream, dek, fileIndex, chunkIndex, output);
                        if (plainLength <= 0 || plainLength > remaining || plainLength > ContainerLayout.MaxChunkSize)
                            throw new VaultException("Conteneur endommagé.");
                        remaining -= plainLength;
                        chunkIndex++;
                    }
                }
            }
            finally
            {
                output?.Dispose();
            }

            if (writeFiles && filePath != null)
                ApplyFileMetadata(filePath, item);

            fileIndex++;
        }
    }

    private static void WriteChunk(Stream stream, byte[] dek, int fileIndex, int chunkIndex, ReadOnlySpan<byte> plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(KeyWrap.NonceLength);
        var cipher = new byte[plaintext.Length];
        var tag = new byte[KeyWrap.TagLength];
        Span<byte> aad = stackalloc byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(aad, fileIndex);
        BinaryPrimitives.WriteInt32LittleEndian(aad[4..], chunkIndex);
        AesGcmBox.Encrypt(dek, nonce, plaintext, aad, cipher, tag);
        WriteInt32(stream, plaintext.Length);
        stream.Write(nonce);
        stream.Write(tag);
        if (cipher.Length > 0)
            stream.Write(cipher);
    }

    private static int ReadChunk(Stream stream, byte[] dek, int fileIndex, int chunkIndex, Stream? output)
    {
        var plainLength = ReadInt32(stream);
        if (plainLength < 0 || plainLength > ContainerLayout.MaxChunkSize)
            throw new VaultException("Conteneur endommagé.");
        var nonce = new byte[KeyWrap.NonceLength];
        var tag = new byte[KeyWrap.TagLength];
        ReadExact(stream, nonce);
        ReadExact(stream, tag);
        var cipher = new byte[plainLength];
        if (plainLength > 0)
            ReadExact(stream, cipher);

        Span<byte> aad = stackalloc byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(aad, fileIndex);
        BinaryPrimitives.WriteInt32LittleEndian(aad[4..], chunkIndex);
        byte[] plain;
        try
        {
            plain = AesGcmBox.Decrypt(dek, nonce, cipher, tag, aad);
        }
        catch (AuthenticationTagMismatchException)
        {
            throw new VaultException("Conteneur endommagé ou clé incorrecte.");
        }

        try
        {
            if (plain.Length != plainLength)
                throw new VaultException("Conteneur endommagé.");
            output?.Write(plain);
            return plain.Length;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    private static ContainerHeader ReadHeader(Stream stream)
    {
        var prefix = new byte[ContainerLayout.WrapRegionEnd];
        ReadExact(stream, prefix);
        var magic = Encoding.ASCII.GetString(prefix, 0, ContainerLayout.MagicLength);
        if (magic != ContainerLayout.Magic)
            throw new VaultException("Ce fichier n'est pas un conteneur VerrDoss.");
        var version = BinaryPrimitives.ReadUInt16LittleEndian(prefix.AsSpan(8));
        if (version != ContainerLayout.Version)
            throw new VaultException("Version de conteneur non prise en charge.");

        var header = new ContainerHeader
        {
            Flags = BinaryPrimitives.ReadUInt16LittleEndian(prefix.AsSpan(10)),
            Kdf = new KdfParameters(
                BinaryPrimitives.ReadInt32LittleEndian(prefix.AsSpan(12)),
                BinaryPrimitives.ReadInt32LittleEndian(prefix.AsSpan(16)),
                BinaryPrimitives.ReadInt32LittleEndian(prefix.AsSpan(20)))
        };
        header.MasterSalt = prefix.AsSpan(24, 16).ToArray();
        header.MasterWrap = prefix.AsSpan(40, KeyWrap.WrapLength).ToArray();
        header.FolderSalt = prefix.AsSpan(100, 16).ToArray();
        header.FolderWrap = prefix.AsSpan(116, KeyWrap.WrapLength).ToArray();
        return header;
    }

    private static void WriteHeaderPrefix(byte[] prefix, ContainerHeader header)
    {
        if (prefix.Length < ContainerLayout.WrapRegionEnd)
            throw new VaultException("En-tête invalide.");
        if (header.MasterSalt.Length != 16 || header.FolderSalt.Length != 16)
            throw new VaultException("Sel de chiffrement invalide.");
        if (header.MasterWrap.Length != KeyWrap.WrapLength || header.FolderWrap.Length != KeyWrap.WrapLength)
            throw new VaultException("Enveloppe de clé invalide.");

        Encoding.ASCII.GetBytes(ContainerLayout.Magic).CopyTo(prefix, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(prefix.AsSpan(8), ContainerLayout.Version);
        BinaryPrimitives.WriteUInt16LittleEndian(prefix.AsSpan(10), header.Flags);
        BinaryPrimitives.WriteInt32LittleEndian(prefix.AsSpan(12), header.Kdf.MemoryKiB);
        BinaryPrimitives.WriteInt32LittleEndian(prefix.AsSpan(16), header.Kdf.Iterations);
        BinaryPrimitives.WriteInt32LittleEndian(prefix.AsSpan(20), header.Kdf.Parallelism);
        header.MasterSalt.CopyTo(prefix, 24);
        header.MasterWrap.CopyTo(prefix, 40);
        header.FolderSalt.CopyTo(prefix, 100);
        header.FolderWrap.CopyTo(prefix, 116);
    }

    private static void ApplyFileMetadata(string path, VaultItem item)
    {
        ApplyTimes(path, item);
        var safe = (FileAttributes)item.Attributes & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.Archive);
        if (safe == 0)
            safe = FileAttributes.Normal;
        try
        {
            File.SetAttributes(path, safe);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void ApplyTimes(string path, VaultItem item)
    {
        try
        {
            FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
            info.CreationTimeUtc = SafeTime(item.CreatedUtcTicks);
            info.LastWriteTimeUtc = SafeTime(item.ModifiedUtcTicks);
        }
        catch (ArgumentException)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static DateTime SafeTime(long ticks)
    {
        var minimum = DateTime.FromFileTimeUtc(0).Ticks;
        if (ticks < minimum || ticks > DateTime.MaxValue.Ticks)
            return DateTime.UnixEpoch;
        return new DateTime(ticks, DateTimeKind.Utc);
    }

    private static FileStream OpenRead(string path)
    {
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private static int ReadInt32(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[4];
        ReadExact(stream, buffer);
        return BinaryPrimitives.ReadInt32LittleEndian(buffer);
    }

    private static void ReadExact(Stream stream, Span<byte> buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = stream.Read(buffer[offset..]);
            if (read == 0)
                throw new VaultException("Conteneur tronqué.");
            offset += read;
        }
    }

    private static int ReadExact(Stream stream, byte[] buffer, int count)
    {
        var offset = 0;
        while (offset < count)
        {
            var read = stream.Read(buffer, offset, count - offset);
            if (read == 0)
                return offset;
            offset += read;
        }
        return offset;
    }

    private sealed class MetadataDocument
    {
        public List<VaultItem> Entries { get; set; } = new();
    }
}
