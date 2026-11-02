using System.Security.Cryptography;

namespace Embasic.ENote.Emd;

/// <summary>Uses Windows DPAPI in CurrentUser scope, not machine scope.</summary>
public sealed class WindowsDpapiKeyProtector : IFileKeyProtector
{
    public byte[] Protect(ReadOnlySpan<byte> dataKey)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        byte[] input = dataKey.ToArray();
        try { return ProtectedData.Protect(input, null, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(input); }
    }

    public byte[] Unprotect(ReadOnlySpan<byte> wrappedDataKey)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        byte[] input = wrappedDataKey.ToArray();
        try { return ProtectedData.Unprotect(input, null, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(input); }
    }
}
