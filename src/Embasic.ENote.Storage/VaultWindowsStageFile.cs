using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Embasic.ENote.Storage;

[SupportedOSPlatform("windows")]
internal static class VaultWindowsStageFile
{
    internal static FileStream Open(string path, bool allowDelete)
    {
        const uint read = 0x80000000;
        const uint delete = 0x00010000;
        const uint openExisting = 3;
        const uint openReparsePoint = 0x00200000;
        var handle = CreateFileW(VaultAtomicFileDirectory.WindowsPath(path), read | (allowDelete ? delete : 0), 0, IntPtr.Zero, openExisting,
            openReparsePoint, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            if (error == 2) throw new FileNotFoundException("Live stage is unavailable.", path);
            throw new IOException("Could not open the live stage exclusively.", new Win32Exception(error));
        }

        try
        {
            VerifyOrdinary(handle);
            return new FileStream(handle, FileAccess.Read);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    internal static void VerifyOrdinary(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var info))
            throw new IOException("Could not inspect the live stage handle.", new Win32Exception(Marshal.GetLastPInvokeError()));
        if ((info.Attributes & ((uint)FileAttributes.Directory | (uint)FileAttributes.ReparsePoint)) != 0 || info.Links != 1)
            throw new IOException("A live stage must be an ordinary file with one link.");
    }

    internal static void MarkForDeletion(SafeFileHandle handle)
    {
        byte delete = 1;
        if (!SetFileInformationByHandle(handle, 4, ref delete, sizeof(byte)))
            throw new IOException("Could not unlink the inspected live stage.", new Win32Exception(Marshal.GetLastPInvokeError()));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInfo
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security,
        uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInfo info);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int kind, ref byte value, int size);
}
