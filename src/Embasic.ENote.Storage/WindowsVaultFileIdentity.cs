using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Embasic.ENote.Storage;

/// <summary>
/// Windows volume serial and opaque 128-bit identifier for a file object.
/// Obtain the expected value during independently trusted enrollment.
/// </summary>
public readonly record struct WindowsVaultFileIdentity(
    ulong VolumeSerialNumber, ulong FileIdPart1, ulong FileIdPart2)
{
    internal bool IsUsable => FileIdPart1 != 0 || FileIdPart2 != 0;

    public static WindowsVaultFileIdentity Read(SafeFileHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Vault file identity requires Windows.");
        if (!GetFileInformationByHandleEx(handle, FileIdInfoClass, out var info,
                (uint)Marshal.SizeOf<NativeFileIdInfo>()))
            throw new IOException("Could not read the vault file identity.",
                new Win32Exception(Marshal.GetLastPInvokeError()));

        var identity = new WindowsVaultFileIdentity(info.VolumeSerialNumber, info.FileIdPart1, info.FileIdPart2);
        if (!identity.IsUsable)
            throw new IOException("The filesystem returned an unusable vault file identity.");
        return identity;
    }

    private const int FileIdInfoClass = 18;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileIdInfo
    {
        public ulong VolumeSerialNumber;
        public ulong FileIdPart1;
        public ulong FileIdPart2;
    }

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle handle, int fileInformationClass, out NativeFileIdInfo information, uint size);
}
