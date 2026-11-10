using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using Embasic.ENote.Emd;
using Embasic.ENote.Storage;
using Microsoft.Win32.SafeHandles;

internal static partial class Program
{
    [SupportedOSPlatform("windows")]
    private static async Task<int> WindowsCngCommand(string[] args)
    {
        bool hardened = args.Contains("--hardened", StringComparer.Ordinal);
        if (hardened) args = args.Where(x => x != "--hardened").ToArray();
        string mode = args[0], home = args[1], shared = args[2], name = args[3];
        string serviceSid = args[4], clientSid = args[5];
        string result = Path.Combine(Path.GetDirectoryName(shared)!, "result");
        string configuration = Path.Combine(shared, "cng-endpoint.json");
        string PublisherFile(int epoch) => Path.Combine(home, $"publisher-{epoch}.bin");
        string ReaderFile(string directory, int epoch) => Path.Combine(directory, $"reader-{epoch}.bin");
        string KeyName(int epoch) => name + "-key-" + epoch;
        if (mode == "cng-cleanup")
        {
            foreach (int epoch in new[] { 1, 2 })
                if (CngKey.Exists(KeyName(epoch), CngProvider.MicrosoftSoftwareKeyStorageProvider, CngKeyOpenOptions.MachineKey))
                {
                    using var key = CngKey.Open(KeyName(epoch), CngProvider.MicrosoftSoftwareKeyStorageProvider,
                        CngKeyOpenOptions.MachineKey);
                    key.Delete();
                }
            return 0;
        }
        if (mode == "cng-setup")
        {
            using var fixture = new Fixture(home);
            using var offline = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var identity = WindowsIdentity.GetCurrent();
            string publisherSid = identity.User!.Value;
            using var replacement = LayerSecret.Regenerate(fixture.Child);
            var candidate = fixture.Prepare(replacement).Manifest.Stamp;
            var first = Provision(1);
            var second = Provision(2);
            VaultAuthorityKeyCustody.VerifySuccessor(first.Keys, second.Keys);
            var info = new CngEndpoint(fixture.Workspace, fixture.Root.Id, fixture.ProviderId,
                fixture.Enrolled!.Stamp, candidate, clientSid, publisherSid, offline.ExportSubjectPublicKeyInfo(), first, second);
            File.WriteAllText(configuration, JsonSerializer.Serialize(info));
            return 0;

            CngEpoch Provision(int epoch)
            {
                using var key = CngKey.Create(CngAlgorithm.ECDsaP256, KeyName(epoch), new CngKeyCreationParameters
                {
                    Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider,
                    KeyCreationOptions = CngKeyCreationOptions.MachineKey,
                    ExportPolicy = CngExportPolicies.None,
                    KeyUsage = CngKeyUsages.Signing
                });
                SetCngKeyAcl(key, serviceSid, publisherSid);
                using var signer = new ECDsaCng(key);
                Throws<CryptographicException>(() => signer.ExportPkcs8PrivateKey());
                byte[] reader = RandomNumberGenerator.GetBytes(32), publisher = RandomNumberGenerator.GetBytes(32);
                var keys = VaultAuthorityKeyCustody.Describe(fixture.Workspace, epoch, signer, reader, publisher);
                File.WriteAllBytes(PublisherFile(epoch), publisher);
                CryptographicOperations.ZeroMemory(publisher);
                if (hardened)
                {
                    File.WriteAllBytes(ReaderFile(home, epoch), reader);
                    if (epoch == 1) File.WriteAllBytes(ReaderFile(shared, epoch), reader);
                    CryptographicOperations.ZeroMemory(reader);
                }
                return new(keys, signer.ExportSubjectPublicKeyInfo(), hardened ? null : reader, VaultAuthorityKeyCustody.Approve(keys, offline));
            }
        }

        if (mode == "cng-peer")
        {
            string peerName = args[6], peerSid = args[7];
            return RunService(peerName, ignoredCancellation =>
            {
                RunWithReceipt(Path.Combine(result, "peer.txt"), () =>
                {
                    RequireServiceToken(peerSid);
                    using var id = WindowsIdentity.GetCurrent();
                    if (new WindowsPrincipal(id).IsInRole(new SecurityIdentifier(serviceSid)))
                        throw new InvalidOperationException("Peer unexpectedly holds the protected service SID.");
                    Throws<UnauthorizedAccessException>(() => File.ReadAllBytes(Path.Combine(home, "root.elkey")));
                    Throws<UnauthorizedAccessException>(() => File.ReadAllBytes(configuration));
                    Throws<CryptographicException>(() =>
                    {
                        using var key = CngKey.Open(KeyName(1), CngProvider.MicrosoftSoftwareKeyStorageProvider,
                            CngKeyOpenOptions.MachineKey | CngKeyOpenOptions.Silent);
                        using var signer = new ECDsaCng(key);
                        _ = signer.SignData("untrusted peer"u8, HashAlgorithmName.SHA256);
                    });
                    if (hardened)
                    {
                        Throws<UnauthorizedAccessException>(() => File.ReadAllBytes(ReaderFile(home, 2)));
                        VerifyPeerProcessRightsDenied(int.Parse(args[8], System.Globalization.CultureInfo.InvariantCulture));
                    }
                    VerifyPipeInstanceDenied(name);
                });
                return Task.CompletedTask;
            });
        }

        if (mode == "cng-service")
        {
            int phase = int.Parse(args[6], System.Globalization.CultureInfo.InvariantCulture);
            int epoch = phase == 0 ? 2 : phase;
            return RunService(name, async stop =>
            {
                string receipt = Path.Combine(result, phase == 0 ? "bad-key.txt" : $"epoch-{epoch}.txt");
                try
                {
                    RequireServiceToken(serviceSid);
                    if (hardened) VaultWindowsServiceProcessBoundary.RestrictCurrentProcess(serviceSid);
                    var config = JsonSerializer.Deserialize<CngEndpoint>(File.ReadAllText(configuration))!;
                    var active = epoch == 1 ? config.First : config.Second;
                    byte[] publisher = File.ReadAllBytes(PublisherFile(epoch));
                    byte[] reader = active.Reader ?? File.ReadAllBytes(ReaderFile(home, epoch));
                    if (phase == 0)
                    {
                        try
                        {
                            using var wrong = VaultAuthorityKeyCustody.OpenWindowsKey(active.Keys, config.Workspace, epoch,
                                active.Approval, config.ApprovalPublic, KeyName(1), reader, publisher);
                            throw new InvalidOperationException("An obsolete KSP key opened for the selected epoch.");
                        }
                        catch (CryptographicException)
                        {
                            File.WriteAllText(receipt, "PASS wrong KSP rejected before endpoint creation");
                            return;
                        }
                        finally
                        {
                            CryptographicOperations.ZeroMemory(reader);
                            CryptographicOperations.ZeroMemory(publisher);
                        }
                    }
                    using var custody = VaultAuthorityKeyCustody.OpenWindowsKey(active.Keys, config.Workspace, epoch,
                        active.Approval, config.ApprovalPublic, KeyName(epoch), reader, publisher);
                    if (epoch == 2)
                        Throws<CryptographicException>(() => VaultAuthorityKeyCustody.OpenWindowsKey(active.Keys,
                            config.Workspace, epoch, active.Approval, config.ApprovalPublic, KeyName(1), reader, publisher));
                    CryptographicOperations.ZeroMemory(reader);
                    CryptographicOperations.ZeroMemory(publisher);
                    using var provider = new VaultTransitionFilePrototype(Path.Combine(home, "authority"), ProviderKey, config.Provider);
                    using var root = LayerKeyVault.Open(File.ReadAllBytes(Path.Combine(home, "root.elkey")),
                        config.Root, LayerKind.Folder, Password);
                    using var publication = Publication(home);
                    using var endpoint = custody.CreateWindowsService(name,
                        new VaultWindowsPipeBoundary(serviceSid, config.ClientSid, LocalServiceSid, config.PublisherSid),
                        new VaultGenerationRecovery(provider, Store(home)), publication, root);
                    if (epoch == 1)
                    {
                        if (hardened)
                            File.WriteAllText(Path.Combine(result, "epoch-1.pid"),
                                Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        await endpoint.ServeOnceAsync(stop);
                        string stopRequest = Path.Combine(shared, "epoch-1.stop");
                        while (!File.Exists(stopRequest)) await Task.Delay(100, stop);
                        Equal("retire-epoch-1", File.ReadAllText(stopRequest));
                        Equal(config.Initial, provider.ReadTransition(config.Workspace).Current);
                    }
                    else
                    {
                        int requests = hardened ? 5 : 4;
                        for (int i = 0; i < requests; i++) await endpoint.ServeOnceAsync(stop);
                        Equal(config.Candidate, provider.ReadTransition(config.Workspace).Current);
                    }
                    File.WriteAllText(receipt, epoch == 1 ? "PASS protected retirement observed" : "PASS");
                }
                catch (Exception error)
                {
                    File.WriteAllText(receipt, error.ToString());
                    throw;
                }
            }, TimeSpan.FromMinutes(3));
        }

        var config = JsonSerializer.Deserialize<CngEndpoint>(File.ReadAllText(configuration))!;
        var firstClient = VaultAuthorityPipeClient.ForWindowsService(name, config.First.ServicePublic);
        if (mode == "cng-publish")
        {
            using var identity = WindowsIdentity.GetCurrent();
            Equal(config.PublisherSid, identity.User!.Value);
            var publish = VaultAuthorityPipeClient.ForWindowsService(name, config.Second.ServicePublic);
            byte[] key = File.ReadAllBytes(PublisherFile(2));
            try { Equal(VaultGenerationAdvanceState.Committed, await publish.PublishAsync(config.Candidate, key)); }
            finally { CryptographicOperations.ZeroMemory(key); }
            Console.WriteLine("PASS separate publisher obtained a signed receipt for the exact candidate");
            return 0;
        }

        using (var identity = WindowsIdentity.GetCurrent())
        {
            Equal(config.ClientSid, identity.User!.Value);
            if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                throw new InvalidOperationException("Expected an ordinary reader account.");
        }
        Throws<UnauthorizedAccessException>(() => File.ReadAllBytes(PublisherFile(2)));
        if (hardened)
        {
            Equal<byte[]?>(null, config.First.Reader);
            Equal<byte[]?>(null, config.Second.Reader);
            Throws<UnauthorizedAccessException>(() => File.Open(ReaderFile(shared, 1), FileMode.Open, FileAccess.Write).Dispose());
            Throws<UnauthorizedAccessException>(() => File.ReadAllBytes(ReaderFile(home, 2)));
        }
        byte[] firstReader = config.First.Reader ?? File.ReadAllBytes(ReaderFile(shared, 1));
        if (mode == "cng-before")
        {
            Throws<CryptographicException>(() => VaultAuthorityKeyCustody.AdvanceWindowsClientPin(name,
                config.First.Keys, config.Second.Keys, 1, config.Second.Approval, config.ApprovalPublic,
                config.Second.ServicePublic, config.Second.Reader ?? firstReader));
            Equal(config.Initial, await firstClient.ObserveAsync(config.Workspace, firstReader));
            Console.WriteLine("PASS an unselected pin update left the first selected client usable");
        }
        else if (mode == "cng-offline")
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            try
            {
                _ = await firstClient.ObserveAsync(config.Workspace, firstReader, timeout.Token);
                throw new InvalidOperationException("The stopped service returned an authenticated result.");
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
            Console.WriteLine("PASS service stop was unavailable, with no authenticated fallback");
        }
        else if (mode == "cng-pin-gap")
        {
            Throws<FileNotFoundException>(() => File.ReadAllBytes(ReaderFile(shared, 2)));
            try
            {
                _ = await firstClient.ObserveAsync(config.Workspace, firstReader);
                throw new InvalidOperationException("The interrupted distribution accepted the old reader.");
            }
            catch (EndOfStreamException) { }
            Console.WriteLine("PASS missing next credential had no authenticated fallback");
        }
        else if (mode == "cng-after")
        {
            byte[] nextReader = config.Second.Reader ?? File.ReadAllBytes(ReaderFile(shared, 2));
            var nextClient = VaultAuthorityKeyCustody.AdvanceWindowsClientPin(name, config.First.Keys, config.Second.Keys, 2,
                config.Second.Approval, config.ApprovalPublic, config.Second.ServicePublic, nextReader);
            try
            {
                _ = await nextClient.ObserveAsync(config.Workspace, firstReader);
                throw new InvalidOperationException("Old role key was accepted by the new process.");
            }
            catch (EndOfStreamException) { }
            try
            {
                _ = await firstClient.ObserveAsync(config.Workspace, nextReader);
                throw new InvalidOperationException("The stale client pin accepted the new signer.");
            }
            catch (CryptographicException) { }
            Equal(config.Initial, await nextClient.ObserveAsync(config.Workspace, nextReader));
            Console.WriteLine("PASS old role and pin rejected, approved next pin observed unchanged selection");
        }
        else return 2;
        return 0;
    }

    [SupportedOSPlatform("windows")]
    private static void RequireServiceToken(string expectedSid)
    {
        using var identity = WindowsIdentity.GetCurrent();
        Equal(LocalServiceSid, identity.User!.Value);
        if (!new WindowsPrincipal(identity).IsInRole(new SecurityIdentifier(expectedSid)))
            throw new InvalidOperationException("Service token is missing its dedicated SID.");
    }

    private static void RunWithReceipt(string path, Action test)
    {
        try { test(); File.WriteAllText(path, "PASS"); }
        catch (Exception error) { File.WriteAllText(path, error.ToString()); throw; }
    }

    [SupportedOSPlatform("windows")]
    private static void SetCngKeyAcl(CngKey key, string serviceSid, string provisioningSid)
    {
        var descriptor = new RawSecurityDescriptor($"D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GA;;;{provisioningSid})(A;;GR;;;{serviceSid})");
        byte[] bytes = new byte[descriptor.BinaryLength];
        descriptor.GetBinaryForm(bytes, 0);
        using var handle = key.Handle;
        const uint daclSecurityInformation = 4;
        const uint persist = 0x80000000;
        int error = NCryptSetProperty(handle, "Security Descr", bytes, bytes.Length, daclSecurityInformation | persist);
        if (error != 0) throw new Win32Exception(error, "Unable to restrict the persisted CNG key.");
    }

    [SupportedOSPlatform("windows")]
    private static void VerifyPeerProcessRightsDenied(int target)
    {
        if (target == Environment.ProcessId) throw new InvalidOperationException("Expected a different process.");
        // CREATE_THREAD, VM_OPERATION, VM_READ, VM_WRITE, DUP_HANDLE and WRITE_DAC.
        foreach (uint right in new uint[] { 0x2, 0x8, 0x10, 0x20, 0x40, 0x40000 })
        {
            IntPtr handle = OpenProcess(right, false, target);
            if (handle != IntPtr.Zero)
            {
                CloseHandle(handle);
                throw new InvalidOperationException($"Peer opened the service process with access 0x{right:X}.");
            }
            Equal(5, Marshal.GetLastPInvokeError());
        }
        Console.WriteLine("PASS same-account peer denied six process handle rights");
    }

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int processId);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("ncrypt.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int NCryptSetProperty(SafeNCryptKeyHandle key, string property, byte[] input, int size, uint flags);

    private sealed record CngEpoch(VaultAuthorityKeyEpoch Keys, byte[] ServicePublic, byte[]? Reader, byte[] Approval);
    private sealed record CngEndpoint(Guid Workspace, Guid Root, Guid Provider, VaultGenerationStamp Initial,
        VaultGenerationStamp Candidate, string ClientSid, string PublisherSid, byte[] ApprovalPublic, CngEpoch First, CngEpoch Second);
}
