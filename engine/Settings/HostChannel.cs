namespace NazarCapture.Engine.Settings;

/// <summary>The host's answer to a settings request (contract.json, host.copydata.replies); 0 = no answer.</summary>
internal enum HostReply
{
    NoAnswer = 0,
    Ok = 1,
    Invalid = 2,
    Taken = 3,
    Duplicate = 4,
    SaveFailed = 5,
    FolderUnusable = 6,
    AutostartFailed = 7,
}

/// <summary>
/// What the settings window needs from the host: the host stays the only writer of settings.ini, registers the hotkeys
/// and writes the autostart entry. The window reads the file itself and asks the host for every change.
/// </summary>
internal interface IHostChannel
{
    /// <summary>Applies and saves one setting (key as in settings.ini, value as stored).</summary>
    HostReply Set(string key, string value);

    /// <summary>Whether <paramref name="combo"/> could be the hotkey of <paramref name="key"/> (free, taken, invalid, duplicate).</summary>
    HostReply CheckHotkey(string key, string combo);

    /// <summary>Releases both hotkeys while one is being recorded (true), registers them again (false).</summary>
    void SuspendHotkeys(bool suspend);

    /// <summary>Which hotkeys are registered right now (contract probe bits); null when the host does not answer.</summary>
    (bool Screenshot, bool Recording)? Probe();
}

/// <summary>
/// The real channel: WM_COPYDATA to the host window (contract.json, host.copydata), SendMessageTimeout so a hung host
/// cannot freeze the window. PROBE is the host's existing probe message.
/// </summary>
internal sealed class HostChannel : IHostChannel
{
    internal const int SetRequest = 1, CheckHotkeyRequest = 2, SuspendHotkeysRequest = 3;
    internal const int ProbeMessage = 0x8000 + 10;
    internal const int ProbeScreenshotBit = 2, ProbeRecordingBit = 4;
    internal const char Separator = '\t';
    const uint TimeoutMs = 3000;

    static IntPtr HostWindow() => NativeMethods.FindWindow(CaptureSettings.HostWindowClass, null);

    public HostReply Set(string key, string value) => Send(SetRequest, key + Separator + value);

    public HostReply CheckHotkey(string key, string combo) => Send(CheckHotkeyRequest, key + Separator + combo);

    public void SuspendHotkeys(bool suspend) => Send(SuspendHotkeysRequest, suspend ? "1" : "0");

    public (bool Screenshot, bool Recording)? Probe()
    {
        IntPtr host = HostWindow();
        if (host == IntPtr.Zero) return null;
        if (NativeMethods.SendMessageTimeout(host, ProbeMessage, IntPtr.Zero, IntPtr.Zero, NativeMethods.SMTO_ABORTIFHUNG, TimeoutMs, out IntPtr result) == IntPtr.Zero) return null;
        long bits = result.ToInt64();
        return ((bits & ProbeScreenshotBit) != 0, (bits & ProbeRecordingBit) != 0);
    }

    /// <summary>The payload as the host reads it: UTF-16LE without terminator.</summary>
    internal static byte[] Payload(string text) => Encoding.Unicode.GetBytes(text);

    static HostReply Send(int request, string text)
    {
        IntPtr host = HostWindow();
        if (host == IntPtr.Zero) return HostReply.NoAnswer;
        byte[] bytes = Payload(text);
        var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        IntPtr data = IntPtr.Zero;
        try
        {
            var copy = new NativeMethods.COPYDATASTRUCT { dwData = new IntPtr(request), cbData = bytes.Length, lpData = pinned.AddrOfPinnedObject() };
            data = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.COPYDATASTRUCT>());
            Marshal.StructureToPtr(copy, data, false);
            if (NativeMethods.SendMessageTimeout(host, NativeMethods.WM_COPYDATA, IntPtr.Zero, data, NativeMethods.SMTO_ABORTIFHUNG, TimeoutMs, out IntPtr result) == IntPtr.Zero)
                return HostReply.NoAnswer;
            int reply = (int)result.ToInt64();
            return Enum.IsDefined(typeof(HostReply), reply) ? (HostReply)reply : HostReply.NoAnswer;
        }
        finally
        {
            if (data != IntPtr.Zero) Marshal.FreeHGlobal(data);
            pinned.Free();
        }
    }
}

/// <summary>Picks a folder with the system's folder dialog (IFileOpenDialog with FOS_PICKFOLDERS).</summary>
internal static class FolderPicker
{
    public static string? Pick(Window owner, string initial)
    {
        IFileOpenDialog? dialog = null;
        try
        {
            dialog = (IFileOpenDialog)new FileOpenDialogClass();
            dialog.GetOptions(out uint options);
            dialog.SetOptions(options | FosPickFolders | FosForceFileSystem | FosPathMustExist);
            if (Directory.Exists(initial) && SHCreateItemFromParsingName(initial, IntPtr.Zero, typeof(IShellItem).GUID, out IShellItem folder) == 0)
                dialog.SetFolder(folder);
            IntPtr hwnd = new WindowInteropHelper(owner).Handle;
            if (dialog.Show(hwnd) != 0) return null;   // cancelled
            dialog.GetResult(out IShellItem item);
            item.GetDisplayName(SigdnFileSysPath, out IntPtr path);
            try { return Marshal.PtrToStringUni(path); }
            finally { Marshal.FreeCoTaskMem(path); }
        }
        catch (COMException ex)
        {
            ErrorLog.Write("Picking a folder", ex);
            return null;
        }
        finally
        {
            if (dialog != null) Marshal.ReleaseComObject(dialog);
        }
    }

    const uint FosPickFolders = 0x20, FosForceFileSystem = 0x40, FosPathMustExist = 0x800;
    const uint SigdnFileSysPath = 0x80058000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IShellItem item);

    [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
    class FileOpenDialogClass { }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItem
    {
        void BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid riid, out IntPtr result);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint form, out IntPtr name);
        void GetAttributes(uint mask, out uint attributes);
        void Compare(IShellItem other, uint hint, out int order);
    }

    [ComImport, Guid("d57c7288-d4ad-4768-be02-9d969532d960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IFileOpenDialog
    {
        [PreserveSig] int Show(IntPtr parent);
        void SetFileTypes(uint count, IntPtr filters);
        void SetFileTypeIndex(uint index);
        void GetFileTypeIndex(out uint index);
        void Advise(IntPtr events, out uint cookie);
        void Unadvise(uint cookie);
        void SetOptions(uint options);
        void GetOptions(out uint options);
        void SetDefaultFolder(IShellItem folder);
        void SetFolder(IShellItem folder);
        void GetFolder(out IShellItem folder);
        void GetCurrentSelection(out IShellItem item);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        void GetResult(out IShellItem item);
        void AddPlace(IShellItem item, int place);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
        void Close(int result);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr filter);
        void GetResults(out IntPtr items);
        void GetSelectedItems(out IntPtr items);
    }
}
