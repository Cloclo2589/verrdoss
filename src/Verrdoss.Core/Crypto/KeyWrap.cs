using System.Security.Cryptography;
using System.Text;

namespace Verrdoss.Core;

public static class KeyWrap
{
    public const int NonceLength = 12;
    public const int TagLength = 16;
    public const int DekLength = 32;
    public const int WrapLength = NonceLength + TagLength + DekLength;

    public static byte[] Wrap(byte[] kek, byte[] dek, string label)
    {
        if (dek.Length != DekLength)
            throw new VaultException("Clé de données invalide.");
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var cipher = new byte[DekLength];
        var tag = new byte[TagLength];
        AesGcmBox.Encrypt(kek, nonce, dek, Label(label), cipher, tag);
        var wrap = new byte[WrapLength];
        nonce.CopyTo(wrap, 0);
        tag.CopyTo(wrap, NonceLength);
        cipher.CopyTo(wrap, NonceLength + TagLength);
        return wrap;
    }

    public static byte[]? TryUnwrap(byte[] kek, byte[] wrap, string label)
    {
        if (wrap.Length != WrapLength)
            return null;
        try
        {
            return AesGcmBox.Decrypt(
                kek,
                wrap.AsSpan(0, NonceLength),
                wrap.AsSpan(NonceLength + TagLength, DekLength),
                wrap.AsSpan(NonceLength, TagLength),
                Label(label));
        }
        catch (AuthenticationTagMismatchException)
        {
            return null;
        }
    }

    private static byte[] Label(string label) => Encoding.ASCII.GetBytes(label);
}
