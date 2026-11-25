using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Trovolve.Desktop.Removal;

/// <summary>
/// The systems' own calls that send files to the Recycle Bin on Windows (the shell's file operation, undoable) and to
/// the Trash on a Mac (Foundation's file manager), asking nothing and showing nothing.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "The operating systems' own Recycle Bin and Trash, which only a run on Windows or a Mac reaches; the tests that run there (RecycleBinTests) call them, and ComputerRecycleBin decides when.")]
internal static class NativeRecycleBin
{
    // As many files as are handed to Windows at once: the list goes as one string.
    private const int Batch = 200;

    /// <summary>Sends the files to the bin of the system this runs on.</summary>
    /// <exception cref="IOException">The system would not take them.</exception>
    /// <exception cref="PlatformNotSupportedException">A system with no bin Trovolve knows.</exception>
    public static void Recycle(IReadOnlyList<string> paths)
    {
        if (OperatingSystem.IsWindows()) Windows(paths);
        else if (OperatingSystem.IsMacOS()) Mac(paths);
        else throw new PlatformNotSupportedException("This computer has no Recycle Bin Trovolve can use.");
    }

    // ---- Windows: SHFileOperation with FOF_ALLOWUNDO, the shell's own Delete.

    private const uint FoDelete = 3;
    private const ushort FofSilent = 0x4, FofNoConfirmation = 0x10, FofAllowUndo = 0x40, FofNoErrorUi = 0x400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOperation
    {
        public IntPtr Window;
        public uint Function;
        public string From;
        public string? To;
        public ushort Flags;
        public int Aborted;
        public IntPtr NameMappings;
        public string? ProgressTitle;
    }

    [DllImport("shell32.dll", EntryPoint = "SHFileOperationW", CharSet = CharSet.Unicode)]
    private static extern int ShellFileOperation(ref ShFileOperation operation);

    private static void Windows(IReadOnlyList<string> paths)
    {
        foreach (var batch in paths.Chunk(Batch))
        {
            // Each path ends with a nothing, and the list with another.
            var operation = new ShFileOperation { Function = FoDelete, From = string.Join('\0', batch) + "\0\0", Flags = (ushort)(FofSilent | FofNoConfirmation | FofAllowUndo | FofNoErrorUi) };
            var error = ShellFileOperation(ref operation);
            if (error != 0) throw new IOException($"Windows would not move them to the Recycle Bin (error {error}).");
            if (operation.Aborted != 0) throw new IOException("Moving them to the Recycle Bin was stopped.");
        }
    }

    // ---- A Mac: -[NSFileManager trashItemAtURL:resultingItemURL:error:], through the Objective-C runtime.

    private const string ObjC = "/usr/lib/libobjc.dylib";

    [DllImport(ObjC, EntryPoint = "objc_getClass")]
    private static extern IntPtr Class([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjC, EntryPoint = "sel_registerName")]
    private static extern IntPtr Selector([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendText(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string text);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern byte SendTrash(IntPtr receiver, IntPtr selector, IntPtr url, IntPtr resulting, out IntPtr error);

    [DllImport(ObjC, EntryPoint = "objc_autoreleasePoolPush")]
    private static extern IntPtr PoolPush();

    [DllImport(ObjC, EntryPoint = "objc_autoreleasePoolPop")]
    private static extern void PoolPop(IntPtr pool);

    private static void Mac(IReadOnlyList<string> paths)
    {
        // Foundation's objects made here are let go of when the pool goes.
        var pool = PoolPush();
        try
        {
            var manager = Send(Class("NSFileManager"), Selector("defaultManager"));
            var trash = Selector("trashItemAtURL:resultingItemURL:error:");
            foreach (var path in paths)
            {
                var text = SendText(Class("NSString"), Selector("stringWithUTF8String:"), path);
                var url = Send(Class("NSURL"), Selector("fileURLWithPath:"), text);
                if (SendTrash(manager, trash, url, IntPtr.Zero, out var error) != 0) continue;
                var said = error == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(Send(Send(error, Selector("localizedDescription")), Selector("UTF8String")));
                throw new IOException($"The Mac would not move {Path.GetFileName(path)} to the Trash: {said ?? "no reason given"}");
            }
        }
        finally
        {
            PoolPop(pool);
        }
    }
}
