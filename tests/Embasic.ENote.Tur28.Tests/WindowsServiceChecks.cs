using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using Embasic.ENote.Emd;
using Embasic.ENote.Storage;
using Microsoft.Win32.SafeHandles;

internal static partial class Program
{
    private const string LocalServiceSid = "S-1-5-19";

    [SupportedOSPlatform("windows")]
    private static async Task<int> WindowsCommand(string[] args)
    {
        string command = args[0], home = args[1], publicDir = args[2], name = args[3];
        string config = Path.Combine(publicDir, "endpoint.json");
        string result = Path.Combine(Path.GetDirectoryName(publicDir)!, "result", "service-result.txt");
        if (command == "setup")
        {
            using var fixture = new Fixture(home);
            using var offline = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var service = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            byte[] reader = RandomNumberGenerator.GetBytes(32), publisher = RandomNumberGenerator.GetBytes(32);
            var keys = VaultAuthorityKeyCustody.Describe(fixture.Workspace, 1, service, reader, publisher);
            var endpoint = new Endpoint(fixture.Workspace, fixture.Root.Id, fixture.ProviderId, fixture.Initial.Stamp,
                WindowsIdentity.GetCurrent().User!.Value, service.ExportSubjectPublicKeyInfo(), reader);
            File.WriteAllText(config, JsonSerializer.Serialize(endpoint));
            var material = new ServiceMaterial(keys, VaultAuthorityKeyCustody.Approve(keys, offline),
                offline.ExportSubjectPublicKeyInfo(), service.ExportPkcs8PrivateKey(), publisher);
            File.WriteAllText(Path.Combine(home, "service.json"), JsonSerializer.Serialize(material));
            return 0;
        }

        var info = JsonSerializer.Deserialize<Endpoint>(File.ReadAllText(config))!;
        if (command == "service")
        {
            return RunService(name, async stop =>
            {
                try
                {
                    Equal(LocalServiceSid, WindowsIdentity.GetCurrent().User!.Value);
                    var material = JsonSerializer.Deserialize<ServiceMaterial>(File.ReadAllText(Path.Combine(home, "service.json")))!;
                    using var custody = VaultAuthorityKeyCustody.Open(material.Keys, info.Workspace, 1,
                        material.Approval, material.ApprovalPublic, material.PrivateKey, info.Reader, material.Publisher);
                    CryptographicOperations.ZeroMemory(material.PrivateKey);
                    CryptographicOperations.ZeroMemory(material.Publisher);
                    using var provider = new VaultTransitionFilePrototype(Path.Combine(home, "authority"), ProviderKey, info.Provider);
                    using var root = LayerKeyVault.Open(File.ReadAllBytes(Path.Combine(home, "root.elkey")),
                        info.Root, LayerKind.Folder, Password);
                    using var publication = Publication(home);
                    using var endpoint = custody.CreateWindowsService(name,
                        new VaultWindowsPipeBoundary(LocalServiceSid, info.ClientSid),
                        new VaultGenerationRecovery(provider, Store(home)), publication, root);
                    await endpoint.ServeOnceAsync(stop);
                    await endpoint.ServeOnceAsync(stop);
                    Equal(info.Initial, provider.ReadTransition(info.Workspace).Current);
                    File.WriteAllText(result, "PASS LocalService identity served requests");
                }
                catch (Exception error)
                {
                    File.WriteAllText(result, error.ToString());
                    throw;
                }
            });
        }

        if (command != "client") return 2;
        Equal(info.ClientSid, WindowsIdentity.GetCurrent().User!.Value);
        if (info.ClientSid == LocalServiceSid) throw new InvalidOperationException("Distinct client identity required.");
        Throws<UnauthorizedAccessException>(() => File.ReadAllBytes(Path.Combine(home, "service.json")));
        Throws<UnauthorizedAccessException>(() => File.Open(Path.Combine(home, "root.elkey"), FileMode.Open, FileAccess.Write).Dispose());
        Console.WriteLine("PASS distinct client denied service private key read and vault file write");
        VerifyPipeInstanceDenied(name);
        Console.WriteLine("PASS client denied pipe instance creation right");
        var client = VaultAuthorityPipeClient.ForWindowsService(name, info.ServicePublic);
        Equal(info.Initial, await client.ObserveAsync(info.Workspace, info.Reader));
        try
        {
            await client.PublishAsync(new VaultGenerationStamp(info.Workspace, 2, new string('A', 64)), info.Reader);
            throw new InvalidOperationException("Reader unexpectedly published.");
        }
        catch (VaultGenerationOutcomeUnknownException) { }
        Console.WriteLine("PASS distinct client observation succeeded and reader credential could not publish");
        return 0;
    }

    [SupportedOSPlatform("windows")]
    private static void VerifyPipeInstanceDenied(string name)
    {
        string path = @"\\.\pipe\" + name;
        if (!SpinWait.SpinUntil(() => WaitNamedPipeW(path, 100), TimeSpan.FromSeconds(20)))
            throw new TimeoutException("Pipe ACL probe never reached the service.");
        const uint duplexOverlapped = 0x40000003;
        const uint rejectRemoteClients = 8;
        using var instance = CreateNamedPipeW(path, duplexOverlapped, rejectRemoteClients, 1,
            4096, 4096, 0, IntPtr.Zero);
        if (!instance.IsInvalid) throw new InvalidOperationException("Pipe instance creation was unexpectedly granted.");
        int error = Marshal.GetLastPInvokeError();
        if (error != 5) throw new Win32Exception(error, "Expected access denied when creating another pipe instance.");
    }

    [SupportedOSPlatform("windows")]
    private static int RunService(string name, Func<CancellationToken, Task> run)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        Handler handler = control => { if (control == 1) stop.Cancel(); };
        int code = 0;
        ServiceMain main = (_, _) =>
        {
            IntPtr handle = RegisterServiceCtrlHandlerW(name, handler);
            if (handle == IntPtr.Zero) { code = 3; return; }
            var status = new ServiceStatus { Type = 0x10, State = 4, Accepted = 1 };
            if (!SetServiceStatus(handle, ref status)) { code = 4; return; }
            try { run(stop.Token).GetAwaiter().GetResult(); }
            catch { code = 5; status.ExitCode = 1064; }
            finally { status.State = 1; status.Accepted = 0; SetServiceStatus(handle, ref status); }
        };
        var entries = new ServiceEntry[] { new() { Name = name, Main = Marshal.GetFunctionPointerForDelegate(main) }, new() };
        if (!StartServiceCtrlDispatcherW(entries)) code = 6;
        GC.KeepAlive(main);
        GC.KeepAlive(handler);
        return code;
    }

    private sealed record Endpoint(Guid Workspace, Guid Root, Guid Provider, VaultGenerationStamp Initial,
        string ClientSid, byte[] ServicePublic, byte[] Reader);

    private sealed record ServiceMaterial(VaultAuthorityKeyEpoch Keys, byte[] Approval, byte[] ApprovalPublic,
        byte[] PrivateKey, byte[] Publisher);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void ServiceMain(int argc, IntPtr argv);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void Handler(int control);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ServiceEntry
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string? Name;
        public IntPtr Main;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public int Type, State, Accepted, ExitCode, ServiceExitCode, Checkpoint, WaitHint;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartServiceCtrlDispatcherW([In] ServiceEntry[] table);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr RegisterServiceCtrlHandlerW(string name, Handler handler);

    [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetServiceStatus(IntPtr handle, ref ServiceStatus status);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WaitNamedPipeW(string name, uint timeout);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafePipeHandle CreateNamedPipeW(string name, uint mode, uint pipeMode, uint instances,
        uint outBuffer, uint inBuffer, uint timeout, IntPtr security);
}
