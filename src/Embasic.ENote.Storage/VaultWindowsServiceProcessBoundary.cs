using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Embasic.ENote.Storage;

/// <summary>Opt-in process DACL for a dedicated Windows service host before it loads vault credentials.</summary>
[SupportedOSPlatform("windows")]
public static class VaultWindowsServiceProcessBoundary
{
    public static void RestrictCurrentProcess(string dedicatedServiceSid)
    {
        var serviceSid = new SecurityIdentifier(dedicatedServiceSid);
        using var identity = WindowsIdentity.GetCurrent();
        if (!serviceSid.Value.StartsWith("S-1-5-80-", StringComparison.Ordinal) ||
            !new WindowsPrincipal(identity).IsInRole(serviceSid))
            throw new UnauthorizedAccessException("The dedicated service SID must be in the process token.");

        // OWNER RIGHTS suppresses the implicit WRITE_DAC granted to the shared logon-account owner.
        var descriptor = new RawSecurityDescriptor($"D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GA;;;{serviceSid.Value})(A;;RC;;;S-1-3-4)");
        byte[] bytes = new byte[descriptor.BinaryLength];
        descriptor.GetBinaryForm(bytes, 0);
        const uint daclSecurityInformation = 4;
        if (!SetKernelObjectSecurity(GetCurrentProcess(), daclSecurityInformation, bytes))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Unable to restrict the service process object.");
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetKernelObjectSecurity(IntPtr handle, uint securityInformation, byte[] descriptor);
}
