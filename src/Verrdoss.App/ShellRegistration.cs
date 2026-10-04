using Microsoft.Win32;

namespace Verrdoss.App;

public static class ShellRegistration
{
    private const string KeyPath = @"Software\Classes\Directory\shell\Verrdoss";

    public static void Register()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
            return;

        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        key.SetValue("", "Verrouiller avec VerrDoss");
        key.SetValue("Icon", $"\"{exe}\",0");
        using var command = key.CreateSubKey("command");
        command.SetValue("", $"\"{exe}\" --lock \"%1\"");
    }

    public static void Unregister()
    {
        Registry.CurrentUser.DeleteSubKeyTree(KeyPath, throwOnMissingSubKey: false);
    }
}
