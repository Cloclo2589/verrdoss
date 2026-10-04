using System.Security.Cryptography;

namespace Verrdoss.Core;

public static class AesGcmBox
{
    public static void Encrypt(
        byte[] key,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> plaintext,
        ReadOnlySpan<byte> associatedData,
        Span<byte> ciphertext,
        Span<byte> tag)
    {
        using var aes = new AesGcm(key, tag.Length);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
    }

    public static byte[] Decrypt(
        byte[] key,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> ciphertext,
        ReadOnlySpan<byte> tag,
        ReadOnlySpan<byte> associatedData)
    {
        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(key, tag.Length);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
        return plaintext;
    }
}
