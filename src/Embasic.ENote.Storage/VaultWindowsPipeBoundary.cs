using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Embasic.ENote.Storage;

/// <summary>Explicit Windows service endpoint. The host must protect its files and secrets separately.</summary>
[SupportedOSPlatform("windows")]
public sealed class VaultWindowsPipeBoundary
{
    private readonly SecurityIdentifier _service;
    private readonly SecurityIdentifier _client;
    private const PipeAccessRights ClientRights = PipeAccessRights.ReadData | PipeAccessRights.WriteData |
        PipeAccessRights.ReadAttributes | PipeAccessRights.Synchronize;

    public VaultWindowsPipeBoundary(string serviceSid, string clientSid)
    {
        _service = new SecurityIdentifier(serviceSid);
        _client = new SecurityIdentifier(clientSid);
        if (_service == _client) throw new ArgumentException("Service and client SIDs must differ.");
    }

    internal NamedPipeServerStream CreateServer(string name)
    {
        ValidateName(name);
        var security = new PipeSecurity();
        security.SetOwner(_service);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new PipeAccessRule(_service, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(_client, ClientRights, AccessControlType.Allow));
        byte[] descriptor = security.GetSecurityDescriptorBinaryForm();
        GCHandle pin = GCHandle.Alloc(descriptor, GCHandleType.Pinned);
        try
        {
            var attributes = new SecurityAttributes
            {
                Length = Marshal.SizeOf<SecurityAttributes>(),
                Descriptor = pin.AddrOfPinnedObject()
            };
            const uint duplexOverlappedFirstInstance = 0x40080003;
            const uint rejectRemoteClients = 0x8;
            var handle = CreateNamedPipeW(@"\\.\pipe\" + name, duplexOverlappedFirstInstance,
                rejectRemoteClients, 1, 4096, 4096, 0, ref attributes);
            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastPInvokeError();
                handle.Dispose();
                throw new IOException("Unable to create the protected Windows pipe.", new Win32Exception(error));
            }
            try { return new NamedPipeServerStream(PipeDirection.InOut, true, false, handle); }
            catch { handle.Dispose(); throw; }
        }
        finally { pin.Free(); }
    }

    internal static NamedPipeClientStream CreateClient(string name)
    {
        ValidateName(name);
        return new NamedPipeClientStream(".", name, ClientRights, PipeOptions.Asynchronous,
            TokenImpersonationLevel.Identification, HandleInheritability.None);
    }

    private static void ValidateName(string name)
    {
        if (name.Length is < 1 or > 128 || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            throw new ArgumentException("Invalid Windows service pipe name.", nameof(name));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public IntPtr Descriptor;
        public int InheritHandle;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafePipeHandle CreateNamedPipeW(string name, uint mode, uint pipeMode, uint instances,
        uint outBuffer, uint inBuffer, uint timeout, ref SecurityAttributes attributes);
}
