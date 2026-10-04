using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Verrdoss.Context;

[ComVisible(true)]
[Guid("7C4E9A12-3B6D-4F58-9A1E-2D8C6B0F4E77")]
[ClassInterface(ClassInterfaceType.None)]
public sealed class LockCommand : IExplorerCommand
{
    public int GetTitle(IShellItemArray? items, out string? title)
    {
        title = "Verrouiller avec VerrDoss";
        return 0;
    }

    public int GetIcon(IShellItemArray? items, out string? icon)
    {
        var exe = ExePath();
        icon = exe == null ? null : exe + ",0";
        return 0;
    }

    public int GetToolTip(IShellItemArray? items, out string? tip)
    {
        tip = "Chiffrer ce dossier avec VerrDoss";
        return 0;
    }

    public int GetCanonicalName(out Guid command)
    {
        command = new Guid("7C4E9A12-3B6D-4F58-9A1E-2D8C6B0F4E77");
        return 0;
    }

    public int GetState(IShellItemArray? items, bool okToBeSlow, out uint state)
    {
        state = 0;
        return 0;
    }

    public int Invoke(IShellItemArray? items, IntPtr bindContext)
    {
        var exe = ExePath();
        if (exe == null || items == null || items.GetCount(out var count) != 0)
            return 0;
        for (uint i = 0; i < count; i++)
        {
            if (items.GetItemAt(i, out var item) != 0 || item == null)
                continue;
            if (item.GetDisplayName(0x80058000, out var path) != 0 || string.IsNullOrWhiteSpace(path))
                continue;
            var start = new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true };
            start.ArgumentList.Add("--lock");
            start.ArgumentList.Add(path);
            System.Diagnostics.Process.Start(start);
        }
        return 0;
    }

    public int GetFlags(out uint flags)
    {
        flags = 0;
        return 0;
    }

    public int EnumSubCommands(out IntPtr commands)
    {
        commands = IntPtr.Zero;
        return unchecked((int)0x80004001);
    }

    private static string? ExePath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Verrdoss");
        if (key?.GetValue("Exe") is string registered && File.Exists(registered))
            return registered;
        var directory = Path.GetDirectoryName(typeof(LockCommand).Assembly.Location);
        if (string.IsNullOrWhiteSpace(directory))
            return null;
        var exe = Path.Combine(directory, "Verrdoss.exe");
        return File.Exists(exe) ? exe : null;
    }
}

[ComImport]
[Guid("A08CE4D0-FA25-44AB-B57C-C7B1C323E0B9")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IExplorerCommand
{
    [PreserveSig]
    int GetTitle(IShellItemArray? items, [MarshalAs(UnmanagedType.LPWStr)] out string? title);

    [PreserveSig]
    int GetIcon(IShellItemArray? items, [MarshalAs(UnmanagedType.LPWStr)] out string? icon);

    [PreserveSig]
    int GetToolTip(IShellItemArray? items, [MarshalAs(UnmanagedType.LPWStr)] out string? tip);

    [PreserveSig]
    int GetCanonicalName(out Guid command);

    [PreserveSig]
    int GetState(IShellItemArray? items, [MarshalAs(UnmanagedType.Bool)] bool okToBeSlow, out uint state);

    [PreserveSig]
    int Invoke(IShellItemArray? items, IntPtr bindContext);

    [PreserveSig]
    int GetFlags(out uint flags);

    [PreserveSig]
    int EnumSubCommands(out IntPtr commands);
}

[ComImport]
[Guid("B63EA76D-1F85-456F-A19C-48159EFA858B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellItemArray
{
    [PreserveSig]
    int BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid interfaceId, out IntPtr result);

    [PreserveSig]
    int GetPropertyStore(int flags, ref Guid interfaceId, out IntPtr result);

    [PreserveSig]
    int GetPropertyDescriptionList(ref Guid key, ref Guid interfaceId, out IntPtr result);

    [PreserveSig]
    int GetAttributes(int flags, uint mask, out uint attributes);

    [PreserveSig]
    int GetCount(out uint count);

    [PreserveSig]
    int GetItemAt(uint index, out IShellItem? item);

    [PreserveSig]
    int EnumItems(out IntPtr enumerator);
}

[ComImport]
[Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellItem
{
    [PreserveSig]
    int BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid interfaceId, out IntPtr result);

    [PreserveSig]
    int GetParent(out IShellItem? parent);

    [PreserveSig]
    int GetDisplayName(uint nameType, [MarshalAs(UnmanagedType.LPWStr)] out string? name);

    [PreserveSig]
    int GetAttributes(uint mask, out uint attributes);

    [PreserveSig]
    int Compare(IShellItem other, uint hint, out int order);
}
