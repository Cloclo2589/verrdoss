using System.Runtime.InteropServices;

namespace Verrdoss.App;

public static class ShellNotify
{
    private const uint UpdateItem = 0x00002000;
    private const uint UpdateDirectory = 0x00001000;
    private const uint PathFlag = 0x0005;
    private const uint Flush = 0x1000;

    public static void Refresh(string path)
    {
        SHChangeNotify(UpdateItem, PathFlag | Flush, path, IntPtr.Zero);
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent))
            SHChangeNotify(UpdateDirectory, PathFlag | Flush, parent, IntPtr.Zero);
        SHChangeNotify(0x00000800, PathFlag | Flush, path, IntPtr.Zero);
    }

    public static void RefreshLocked(string folder)
    {
        Refresh(folder);
        var icon = Path.Combine(folder, "verrdoss.ico");
        if (File.Exists(icon))
            SHChangeNotify(UpdateItem, PathFlag | Flush, icon, IntPtr.Zero);
        RefreshOpenViews(folder);
    }

    private static void RefreshOpenViews(string folder)
    {
        var parent = Path.GetDirectoryName(folder);
        if (string.IsNullOrEmpty(parent))
            return;
        if (IsDesktop(parent))
            RefreshDesktopView();

        try
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
            foreach (dynamic window in shell.Windows())
            {
                string url;
                try { url = (string)window.LocationURL; }
                catch { continue; }
                var location = ToFilePath(url);
                if (location == null || !SamePath(location, parent))
                    continue;
                try { window.Refresh(); }
                catch { }
            }
        }
        catch
        {
        }
    }

    private static bool IsDesktop(string path)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var known = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        return SamePath(path, desktop) || SamePath(path, known);
    }

    private static string? ToFilePath(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return null;
        try
        {
            return new Uri(url).LocalPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    private static bool SamePath(string left, string right)
    {
        try
        {
            var a = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var b = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void RefreshIcons()
    {
        SHChangeNotify(0x08000000, Flush, null, IntPtr.Zero);
        RefreshDesktopView();
    }

    private static void RefreshDesktopView()
    {
        var desktop = FindWindow("Progman", null);
        var view = FindWindowEx(desktop, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (view == IntPtr.Zero)
        {
            var worker = IntPtr.Zero;
            while (true)
            {
                worker = FindWindowEx(IntPtr.Zero, worker, "WorkerW", null);
                if (worker == IntPtr.Zero)
                    break;
                view = FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (view != IntPtr.Zero)
                    break;
            }
        }

        var list = view == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(view, IntPtr.Zero, "SysListView32", null);
        if (list != IntPtr.Zero)
            PostMessage(list, 0x0100, (IntPtr)0x74, IntPtr.Zero);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr child, string? className, string? windowName);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(uint eventId, uint flags, string? item1, IntPtr item2);
}
