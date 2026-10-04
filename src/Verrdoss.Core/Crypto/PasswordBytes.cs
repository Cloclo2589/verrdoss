using System.Security.Cryptography;
using System.Text;

namespace Verrdoss.Core;

public static class PasswordBytes
{
    public static byte[] From(string password)
    {
        var normalized = password.Normalize(NormalizationForm.FormC);
        return Encoding.UTF8.GetBytes(normalized);
    }

    public static void Clear(byte[]? bytes)
    {
        if (bytes is { Length: > 0 })
            CryptographicOperations.ZeroMemory(bytes);
    }
}
