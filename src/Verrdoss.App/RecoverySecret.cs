using System.Security.Cryptography;

namespace Verrdoss.App;

public static class RecoverySecret
{
    private const string FileName = "recovery.bin";

    public static void Save(string dataDirectory, byte[] password)
    {
        Directory.CreateDirectory(dataDirectory);
        var protectedBytes = ProtectedData.Protect(password, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(Path.Combine(dataDirectory, FileName), protectedBytes);
    }

    public static byte[]? Load(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, FileName);
        if (!File.Exists(path))
            return null;
        try
        {
            return ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
