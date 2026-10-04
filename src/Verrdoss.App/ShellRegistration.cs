using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Verrdoss.App;

public static class ShellRegistration
{
    public const string PackageName = "VerrDoss.Shell";
    public const string CommandId = "7C4E9A12-3B6D-4F58-9A1E-2D8C6B0F4E77";
    private const string KeyPath = @"Software\Classes\Directory\shell\Verrdoss";

    public static void PrepareLayout(string directory)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0);
        var packageVersion = $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        var assets = Path.Combine(directory, "Assets");
        Directory.CreateDirectory(assets);
        WriteLogo(Path.Combine(assets, "StoreLogo.png"), 50);
        WriteLogo(Path.Combine(assets, "Square44x44Logo.png"), 44);
        WriteLogo(Path.Combine(assets, "Square150x150Logo.png"), 150);
        File.WriteAllText(Path.Combine(directory, "AppxManifest.xml"), Manifest(packageVersion), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public static void Register()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
            return;

        using (var key = Registry.CurrentUser.CreateSubKey(KeyPath))
        {
            key.SetValue("", "Verrouiller avec VerrDoss");
            key.SetValue("Icon", $"\"{exe}\",0");
            using var command = key.CreateSubKey("command");
            command.SetValue("", $"\"{exe}\" --lock \"%1\"");
        }

        using (var appKey = Registry.CurrentUser.CreateSubKey(@"Software\Verrdoss"))
            appKey.SetValue("Exe", exe);
        RegisterModernMenu(exe);
    }

    public static void Unregister()
    {
        Registry.CurrentUser.DeleteSubKeyTree(KeyPath, throwOnMissingSubKey: false);
        RemovePackage();
        using var appKey = Registry.CurrentUser.OpenSubKey(@"Software\Verrdoss", writable: true);
        appKey?.DeleteValue("Exe", throwOnMissingValue: false);
        var marker = MarkerPath();
        if (marker != null && File.Exists(marker))
            File.Delete(marker);
    }

    private static void RegisterModernMenu(string exe)
    {
        var directory = Path.GetDirectoryName(exe);
        if (string.IsNullOrWhiteSpace(directory))
            return;
        var packageFile = Path.Combine(directory, "Verrdoss.Shell.msix");
        var certificate = Path.Combine(directory, "Verrdoss.Shell.cer");
        var root = Path.Combine(directory, "Verrdoss.Root.cer");
        if (!File.Exists(packageFile) || !File.Exists(certificate) || !File.Exists(root))
            return;

        var version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0);
        var packageVersion = $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        var marker = Path.Combine(directory, "shell-package.marker");
        var stamp = packageVersion + "|" + directory;
        if (File.Exists(marker) && File.ReadAllText(marker) == stamp)
            return;

        if (!InstallPackage(packageFile))
        {
            RemovePackage();
            if (!InstallPackage(packageFile))
            {
                TrustMachine(root, certificate);
                if (!InstallPackage(packageFile))
                    return;
            }
        }

        File.WriteAllText(marker, stamp);
        SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
    }

    private static string Manifest(string version) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
        "<Package xmlns=\"http://schemas.microsoft.com/appx/manifest/foundation/windows10\" " +
        "xmlns:uap=\"http://schemas.microsoft.com/appx/manifest/uap/windows10\" " +
        "xmlns:uap10=\"http://schemas.microsoft.com/appx/manifest/uap/windows10/10\" " +
        "xmlns:rescap=\"http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities\" " +
        "xmlns:desktop4=\"http://schemas.microsoft.com/appx/manifest/desktop/windows10/4\" " +
        "xmlns:desktop5=\"http://schemas.microsoft.com/appx/manifest/desktop/windows10/5\" " +
        "xmlns:com=\"http://schemas.microsoft.com/appx/manifest/com/windows10\" " +
        "IgnorableNamespaces=\"uap uap10 rescap desktop4 desktop5 com\">" +
        "<Identity Name=\"" + PackageName + "\" Publisher=\"CN=VerrDoss\" Version=\"" + version + "\" ProcessorArchitecture=\"x64\" />" +
        "<Properties><DisplayName>VerrDoss</DisplayName><PublisherDisplayName>VerrDoss</PublisherDisplayName><Logo>Assets\\StoreLogo.png</Logo></Properties>" +
        "<Resources><Resource Language=\"fr-FR\" /></Resources>" +
        "<Dependencies><TargetDeviceFamily Name=\"Windows.Desktop\" MinVersion=\"10.0.22000.0\" MaxVersionTested=\"10.0.26100.0\" /></Dependencies>" +
        "<Capabilities><rescap:Capability Name=\"runFullTrust\" /><rescap:Capability Name=\"unvirtualizedResources\" /></Capabilities>" +
        "<Applications><Application Id=\"App\" Executable=\"Verrdoss.exe\" uap10:TrustLevel=\"mediumIL\" uap10:RuntimeBehavior=\"win32App\">" +
        "<uap:VisualElements DisplayName=\"VerrDoss\" Description=\"Dossiers chiffrés\" BackgroundColor=\"transparent\" Square150x150Logo=\"Assets\\Square150x150Logo.png\" Square44x44Logo=\"Assets\\Square44x44Logo.png\" AppListEntry=\"none\" />" +
        "<Extensions>" +
        "<com:Extension Category=\"windows.comServer\"><com:ComServer><com:SurrogateServer DisplayName=\"VerrDoss\">" +
        "<com:Class Id=\"" + CommandId + "\" Path=\"Verrdoss.Context.comhost.dll\" ThreadingModel=\"STA\" />" +
        "</com:SurrogateServer></com:ComServer></com:Extension>" +
        "<desktop4:Extension Category=\"windows.fileExplorerContextMenus\"><desktop4:FileExplorerContextMenus>" +
        "<desktop5:ItemType Type=\"Directory\"><desktop5:Verb Id=\"Lock\" Clsid=\"" + CommandId + "\" /></desktop5:ItemType>" +
        "<desktop5:ItemType Type=\"Directory\\Background\"><desktop5:Verb Id=\"LockBackground\" Clsid=\"" + CommandId + "\" /></desktop5:ItemType>" +
        "</desktop4:FileExplorerContextMenus></desktop4:Extension>" +
        "</Extensions></Application></Applications></Package>";

    private static void WriteLogo(string path, int size)
    {
        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            using var brush = new SolidBrush(Color.FromArgb(31, 75, 115));
            var margin = Math.Max(1, size / 25);
            graphics.FillEllipse(brush, margin, margin, size - margin * 2, size - margin * 2);
        }
        bitmap.Save(path, ImageFormat.Png);
    }

    private static string? MarkerPath()
    {
        var exe = Environment.ProcessPath;
        var directory = string.IsNullOrWhiteSpace(exe) ? null : Path.GetDirectoryName(exe);
        return directory == null ? null : Path.Combine(directory, "shell-package.marker");
    }

    private static void RemovePackage()
    {
        RunPowerShell("Get-AppxPackage -Name '" + PackageName + "' | Remove-AppxPackage");
    }

    private static bool InstallPackage(string packageFile)
    {
        var script = "Add-AppxPackage -ForceUpdateFromAnyVersion -Path '" + packageFile.Replace("'", "''") + "'";
        return RunPowerShell(script);
    }

    private static void TrustMachine(string root, string leaf)
    {
        var start = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c certutil -addstore Root \"" + root + "\" & certutil -addstore TrustedPeople \"" + leaf + "\"",
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };
        try
        {
            using var process = Process.Start(start);
            process?.WaitForExit();
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    private static bool RunPowerShell(string command)
    {
        var script = Path.Combine(Path.GetTempPath(), "verrdoss-shell-" + Guid.NewGuid().ToString("N") + ".ps1");
        File.WriteAllText(script, "$ErrorActionPreference = 'Stop'\r\n" + command + "\r\n");
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -File \"" + script + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            using var process = Process.Start(start);
            if (process == null)
                return false;
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        finally
        {
            if (File.Exists(script))
                File.Delete(script);
        }
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
