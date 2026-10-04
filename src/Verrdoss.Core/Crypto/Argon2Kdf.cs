using System.Security.Cryptography;
using Isopoh.Cryptography.Argon2;

namespace Verrdoss.Core;

public static class Argon2Kdf
{
    public static byte[] Derive(byte[] password, byte[] salt, KdfParameters parameters)
    {
        var config = new Argon2Config
        {
            Type = Argon2Type.HybridAddressing,
            Version = Argon2Version.Nineteen,
            TimeCost = parameters.Iterations,
            MemoryCost = parameters.MemoryKiB,
            Lanes = parameters.Parallelism,
            Threads = parameters.Parallelism,
            Password = password,
            Salt = salt,
            HashLength = 32
        };

        using var argon = new Argon2(config);
        using var hash = argon.Hash();
        var result = new byte[hash.Buffer.Length];
        Buffer.BlockCopy(hash.Buffer, 0, result, 0, result.Length);
        return result;
    }

    public static void Clear(byte[]? buffer)
    {
        if (buffer is { Length: > 0 })
            CryptographicOperations.ZeroMemory(buffer);
    }
}
