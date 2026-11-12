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
        bool shutdown = args.Contains("--tur35", StringComparer.Ordinal);
        bool lifecycle = shutdown || args.Contains("--tur34", StringComparer.Ordinal);
        bool rotated = lifecycle || args.Contains("--tur33", StringComparer.Ordinal);
        bool wrapped = rotated || args.Contains("--tur32", StringComparer.Ordinal);
        bool hardened = wrapped || args.Contains("--hardened", StringComparer.Ordinal);
        if (hardened) args = args.Where(x => x is not ("--hardened" or "--tur32" or "--tur33" or "--tur34" or "--tur35")).ToArray();
        string mode = args[0], home = args[1], shared = args[2], name = args[3];
        string serviceSid = args[4], clientSid = args[5];
        string result = Path.Combine(Path.GetDirectoryName(shared)!, "result");
        string configuration = Path.Combine(shared, "cng-endpoint.json");
        string PublisherFile(int epoch) => Path.Combine(home, $"publisher-{epoch}.bin");
        string ReaderFile(string directory, int epoch) => Path.Combine(directory, $"reader-{epoch}.bin");
        string KeyName(int epoch) => name + "-key-" + epoch;
        string RoleKey(bool publisher, int epoch = 1) => name + (publisher ? "-role-pub" : "-role-read") +
            (rotated ? "-" + epoch : "");
        byte[] ReadRole(bool publisher, string path, int epoch = 1) => wrapped
            ? VaultWindowsRoleCustody.Unwrap(RoleKey(publisher, epoch), path)
            : File.ReadAllBytes(path);
        if (mode == "cng-cleanup")
        {
            var keys = wrapped ? new[] { KeyName(1), KeyName(2), RoleKey(false), RoleKey(true) } : [KeyName(1), KeyName(2)];
            if (rotated) keys = [.. keys, RoleKey(false, 2), RoleKey(true, 2)];
            foreach (string keyName in keys)
                if (CngKey.Exists(keyName, CngProvider.MicrosoftSoftwareKeyStorageProvider, CngKeyOpenOptions.MachineKey))
                {
                    using var key = CngKey.Open(keyName, CngProvider.MicrosoftSoftwareKeyStorageProvider,
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
            byte[] readerRecipient = wrapped ? CreateRoleRecipient(RoleKey(false), clientSid) : [];
            byte[] publisherRecipient = wrapped ? CreateRoleRecipient(RoleKey(true), publisherSid) : [];
            using var replacement = LayerSecret.Regenerate(fixture.Child);
            var candidate = fixture.Prepare(replacement).Manifest.Stamp;
            var first = Provision(1);
            if (rotated)
            {
                readerRecipient = CreateRoleRecipient(RoleKey(false, 2), clientSid);
                publisherRecipient = CreateRoleRecipient(RoleKey(true, 2), publisherSid);
            }
            var second = Provision(2);
            VaultAuthorityKeyCustody.VerifySuccessor(first.Keys, second.Keys);
            var info = new CngEndpoint(fixture.Workspace, fixture.Root.Id, fixture.ProviderId,
                fixture.Enrolled!.Stamp, candidate, clientSid, publisherSid, offline.ExportSubjectPublicKeyInfo(), first, second);
            File.WriteAllText(configuration, JsonSerializer.Serialize(info));
            return 0;

            byte[] CreateRoleRecipient(string keyName, string recipientSid)
            {
                var parameters = new CngKeyCreationParameters
                {
                    Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider,
                    KeyCreationOptions = CngKeyCreationOptions.MachineKey,
                    ExportPolicy = CngExportPolicies.None,
                    KeyUsage = CngKeyUsages.Decryption
                };
                parameters.Parameters.Add(new CngProperty("Length", BitConverter.GetBytes(2048), CngPropertyOptions.None));
                using var key = CngKey.Create(CngAlgorithm.Rsa, keyName, parameters);
                SetCngKeyAcl(key, serviceSid, publisherSid, recipientSid);
                using var recipient = new RSACng(key);
                Throws<CryptographicException>(() => recipient.ExportPkcs8PrivateKey());
                return recipient.ExportSubjectPublicKeyInfo();
            }

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
                File.WriteAllBytes(PublisherFile(epoch), wrapped ? VaultWindowsRoleCustody.Wrap(publisherRecipient, publisher) : publisher);
                CryptographicOperations.ZeroMemory(publisher);
                if (hardened)
                {
                    byte[] payload = wrapped ? VaultWindowsRoleCustody.Wrap(readerRecipient, reader) : reader;
                    File.WriteAllBytes(ReaderFile(home, epoch), payload);
                    if (epoch == 1) File.WriteAllBytes(ReaderFile(shared, epoch), payload);
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
                        if (wrapped)
                            Throws<CryptographicException>(() => OpenRecipient(RoleKey(false)));
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
            int epoch = phase == 0 || phase == 3 ? 2 : phase;
            return RunService(name, async stop =>
            {
                bool rollback = rotated && phase == 1 && File.Exists(Path.Combine(shared, "selected-epoch"));
                string receipt = Path.Combine(result, rollback ? "rollback-blocked.txt" :
                    phase == 0 ? "bad-key.txt" : phase == 3 ? "restart.txt" : $"epoch-{epoch}.txt");
                try
                {
                    RequireServiceToken(serviceSid);
                    if (hardened) VaultWindowsServiceProcessBoundary.RestrictCurrentProcess(serviceSid);
                    var config = JsonSerializer.Deserialize<CngEndpoint>(File.ReadAllText(configuration))!;
                    if (rollback)
                    {
                        Equal("2", File.ReadAllText(Path.Combine(shared, "selected-epoch")));
                        Throws<CryptographicException>(() => VaultAuthorityKeyCustody.OpenWindowsKey(config.First.Keys,
                            config.Workspace, 2, config.First.Approval, config.ApprovalPublic, KeyName(1), [], []));
                        WriteFixtureReceipt(receipt, "PASS stale service rejected after new epoch selection");
                        return;
                    }
                    var active = epoch == 1 ? config.First : config.Second;
                    byte[] publisher = ReadRole(true, PublisherFile(epoch), epoch);
                    byte[] reader = active.Reader ?? ReadRole(false, ReaderFile(home, epoch), epoch);
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
                            WriteFixtureReceipt(receipt, "PASS wrong KSP rejected before endpoint creation");
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
                    IExternalVaultTransitionAnchor anchor = shutdown && phase == 2
                        ? new StopAfterCommitAnchor(provider, result, stop)
                        : provider;
                    using var endpoint = custody.CreateWindowsService(name,
                        new VaultWindowsPipeBoundary(serviceSid, config.ClientSid, LocalServiceSid, config.PublisherSid),
                        new VaultGenerationRecovery(anchor, Store(home)), publication, root);
                    if (epoch == 1)
                    {
                        if (hardened)
                            WriteFixtureReceipt(Path.Combine(result, "epoch-1.pid"),
                                Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        await endpoint.ServeOnceAsync(stop);
                        if (lifecycle)
                        {
                            try { await endpoint.ServeOnceAsync(stop); }
                            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
                            if (!stop.IsCancellationRequested)
                                throw new InvalidOperationException("The endpoint completed without a stop request.");
                            WriteFixtureReceipt(Path.Combine(result, "stop-pending.txt"), "PASS cancellation reached endpoint");
                            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                            WaitForSignal(shared, "epoch-1.release", deadline.Token);
                            Equal(config.Initial, provider.ReadTransition(config.Workspace).Current);
                            WriteFixtureReceipt(receipt, "PASS native SCM stop drained the endpoint");
                            return;
                        }
                        string stopRequest = Path.Combine(shared, "epoch-1.stop");
                        while (!File.Exists(stopRequest)) await Task.Delay(100, stop);
                        Equal("retire-epoch-1", File.ReadAllText(stopRequest));
                        Equal(config.Initial, provider.ReadTransition(config.Workspace).Current);
                    }
                    else
                    {
                        int requests = phase == 3 ? 1 : hardened ? 5 : 4;
                        try
                        {
                            for (int i = 0; i < requests; i++) await endpoint.ServeOnceAsync(stop);
                        }
                        catch (OperationCanceledException) when (shutdown && phase == 2 && stop.IsCancellationRequested)
                        {
                            var observed = provider.ReadTransition(config.Workspace).Current;
                            if (observed == config.Initial && File.Exists(Path.Combine(shared, "reader-2.pending")) &&
                                !File.Exists(ReaderFile(shared, 2)))
                            {
                                WriteFixtureReceipt(Path.Combine(result, "distribution-paused.txt"), "PASS partial envelope remained inactive");
                                return;
                            }
                            Equal(config.Candidate, observed);
                            WriteFixtureReceipt(receipt, "PASS selection persisted despite missing reply");
                            return;
                        }
                        if (shutdown && phase == 2)
                            throw new InvalidOperationException("Expected STOP before the publication reply.");
                        Equal(config.Candidate, provider.ReadTransition(config.Workspace).Current);
                    }
                    WriteFixtureReceipt(receipt, epoch == 1 ? "PASS protected retirement observed" : "PASS");
                }
                catch (Exception error)
                {
                    WriteFixtureReceipt(receipt, error.ToString());
                    throw;
                }
            }, TimeSpan.FromMinutes(3), reportStopPending: lifecycle);
        }

        if (mode == "cng-stage-interrupted" && lifecycle)
        {
            byte[] envelope = File.ReadAllBytes(ReaderFile(home, 2));
            using (var stage = new FileStream(Path.Combine(shared, "reader-2.pending"), FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stage.Write(envelope.AsSpan(0, 17));
                stage.Flush(flushToDisk: true);
            }
            WriteFixtureReceipt(Path.Combine(result, "distribution-interrupted.txt"), "PASS partial envelope staged");
            await Task.Delay(TimeSpan.FromMinutes(1));
            return 2;
        }

        var config = JsonSerializer.Deserialize<CngEndpoint>(File.ReadAllText(configuration))!;
        var firstClient = VaultAuthorityPipeClient.ForWindowsService(name, config.First.ServicePublic);
        if (mode == "cng-publish")
        {
            using var identity = WindowsIdentity.GetCurrent();
            Equal(config.PublisherSid, identity.User!.Value);
            var publish = VaultAuthorityPipeClient.ForWindowsService(name, config.Second.ServicePublic);
            if (rotated) Throws<CryptographicException>(() => ReadRole(true, PublisherFile(2), 1));
            byte[] key = ReadRole(true, PublisherFile(2), 2);
            try
            {
                if (shutdown)
                {
                    try
                    {
                        _ = await publish.PublishAsync(config.Candidate, key);
                        throw new InvalidOperationException("The stopped endpoint returned a publication receipt.");
                    }
                    catch (VaultGenerationOutcomeUnknownException)
                    {
                        Console.WriteLine("PASS client received no verified publication receipt after STOP");
                    }
                }
                else
                {
                    Equal(VaultGenerationAdvanceState.Committed, await publish.PublishAsync(config.Candidate, key));
                    Console.WriteLine("PASS separate publisher obtained a signed receipt for the exact candidate");
                }
            }
            finally { CryptographicOperations.ZeroMemory(key); }
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
        if (wrapped) Throws<CryptographicException>(() => OpenRecipient(RoleKey(true, 2)));
        if (rotated) Throws<UnauthorizedAccessException>(() =>
            File.Create(Path.Combine(shared, "unauthorized-distributor.pending")).Dispose());
        byte[] firstReader = config.First.Reader ?? ReadRole(false, ReaderFile(shared, 1));
        if (mode == "cng-wait-request" && shutdown)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            using var pipe = VaultWindowsPipeBoundary.CreateClient(name);
            await pipe.ConnectAsync(deadline.Token);
            await pipe.ReadExactlyAsync(new byte[VaultAuthorityPipeProtocol.ChallengeSize], deadline.Token);
            await pipe.WriteAsync(new byte[] { 1 }, deadline.Token);
            Console.WriteLine("READY partial request");
            Console.Out.Flush();
            Equal(0, await pipe.ReadAsync(new byte[1], deadline.Token));
            Console.WriteLine("PASS STOP closed the incomplete request without a response");
        }
        else if (mode == "cng-before")
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
        else if (mode == "cng-reject-envelope")
        {
            Throws<CryptographicException>(() => ReadRole(false, ReaderFile(shared, 2), 2));
            Console.WriteLine("PASS corrupt recipient envelope rejected before IPC");
        }
        else if (mode == "cng-after")
        {
            if (rotated)
                Throws<CryptographicException>(() => ReadRole(false, ReaderFile(shared, 2), 1));
            byte[] nextReader = config.Second.Reader ?? ReadRole(false, ReaderFile(shared, 2), 2);
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
        else if (mode == "cng-confirm" && lifecycle)
        {
            byte[] nextReader = ReadRole(false, ReaderFile(shared, 2), 2);
            try
            {
                var next = VaultAuthorityKeyCustody.AdvanceWindowsClientPin(name, config.First.Keys, config.Second.Keys, 2,
                    config.Second.Approval, config.ApprovalPublic, config.Second.ServicePublic, nextReader);
                Equal(config.Candidate, await next.ObserveAsync(config.Workspace, nextReader));
                Console.WriteLine("PASS restarted selected service returned the persisted generation");
            }
            finally { CryptographicOperations.ZeroMemory(nextReader); }
        }
        else return 2;
        return 0;
    }

    private sealed class StopAfterCommitAnchor(VaultTransitionFilePrototype inner, string result, CancellationToken stop)
        : IExternalVaultTransitionAnchor
    {
        public VaultTransitionState? ReadTransition(Guid workspaceId) => inner.ReadTransition(workspaceId);
        public void ReserveTransition(VaultRetryIntent intent) => inner.ReserveTransition(intent);

        public void CommitTransition(VaultRetryIntent intent)
        {
            inner.CommitTransition(intent);
            WriteFixtureReceipt(Path.Combine(result, "publication-committed.txt"), "PASS exact selection committed before reply");
            if (!stop.WaitHandle.WaitOne(TimeSpan.FromSeconds(20)))
                throw new TimeoutException("Native STOP did not arrive at the publication checkpoint.");
        }
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
        try { test(); WriteFixtureReceipt(path, "PASS"); }
        catch (Exception error) { WriteFixtureReceipt(path, error.ToString()); throw; }
    }

    [SupportedOSPlatform("windows")]
    private static void OpenRecipient(string keyName)
    {
        using var key = CngKey.Open(keyName, CngProvider.MicrosoftSoftwareKeyStorageProvider,
            CngKeyOpenOptions.MachineKey | CngKeyOpenOptions.Silent);
    }

    private static void WriteFixtureReceipt(string path, string value)
    {
        string staged = path + ".writing";
        File.WriteAllText(staged, value);
        File.Move(staged, path, overwrite: true);
    }

    [SupportedOSPlatform("windows")]
    private static void SetCngKeyAcl(CngKey key, string serviceSid, string provisioningSid, string? recipientSid = null)
    {
        string recipient = recipientSid is null || recipientSid == provisioningSid ? "" : $"(A;;GR;;;{recipientSid})";
        var descriptor = new RawSecurityDescriptor($"D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GA;;;{provisioningSid})(A;;GR;;;{serviceSid})" + recipient);
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
