using System.Security.Cryptography;
using Embasic.ENote.Emd;
using Embasic.ENote.Storage;

internal static partial class Program
{
    private static void ProviderChecks(Fixture f)
    {
        Check("process-written reservation survives a new provider and commits an exact receipt", () =>
        {
            string folder = f.Folder();
            using (f.Open(folder, provision: true)) { }
            string signal = Path.Combine(folder, "signal");
            using var child = StartChild("reserve-with-signal", folder, f.Intent, signal);
            Equal(0, WaitForChild(child));
            using (var second = f.Open(folder))
            {
                Equal(f.Intent, second.ReadTransition(f.Workspace).Pending);
                second.CommitTransition(f.Intent);
                second.CommitTransition(f.Intent);
            }
            using var third = f.Open(folder);
            Equal(new VaultTransitionState(f.Next.Stamp, LastCommitted: f.Intent), third.ReadTransition(f.Workspace));
        });
        Check("two child processes racing for one slot leave exactly one durable reservation", () =>
        {
            string folder = f.Folder();
            using var provider = f.Open(folder, provision: true);
            string start = Path.Combine(folder, "start");
            using var first = StartChild("reserve-after-signal", folder, f.Intent, start);
            using var second = StartChild("reserve-after-signal", folder, f.RivalIntent, start);
            File.WriteAllText(start, "start");
            int a = WaitForChild(first), b = WaitForChild(second);
            if (!((a == 0 && b == 3) || (a == 3 && b == 0)))
                throw new InvalidOperationException($"Expected one winner, got {a} and {b}.");
            Equal(a == 0 ? f.Intent : f.RivalIntent, provider.ReadTransition(f.Workspace).Pending);
        });
        Check("a distinct provider process cannot reserve while the key use fence is held", () =>
        {
            string folder = f.Folder();
            using var provider = f.Open(folder, provision: true);
            string signal = Path.Combine(folder, "signal");
            ProcessWithFence();
            void ProcessWithFence()
            {
                System.Diagnostics.Process child;
                using (provider.EnterKeyUse(f.Initial.Stamp))
                {
                    child = StartChild("reserve-with-signal", folder, f.Intent, signal);
                    WaitForSignal(signal);
                    Equal(false, child.WaitForExit(150));
                }
                using (child) Equal(0, WaitForChild(child));
                Equal(f.Intent, provider.ReadTransition(f.Workspace).Pending);
            }
        });
        Check("terminating the holder process releases the cooperative fence", () =>
        {
            string folder = f.Folder();
            using var provider = f.Open(folder, provision: true);
            string signal = Path.Combine(folder, "hold");
            using var child = StartChild("hold", folder, f.Intent, signal);
            try { WaitForSignal(signal); }
            finally { if (!child.HasExited) child.Kill(); WaitForChild(child); }
            provider.ReserveTransition(f.Intent);
            Equal(f.Intent, provider.ReadTransition(f.Workspace).Pending);
        });
        Check("provider refuses absent state, missing marker and re-provision with marker retained", () =>
        {
            string folder = f.Folder();
            using var provider = f.Open(folder, provision: true);
            File.Delete(StateFile(folder));
            Throws<FileNotFoundException>(() => provider.ReadTransition(f.Workspace));
            Throws<IOException>(() => provider.Provision(f.Initial.Stamp));
            File.Delete(Path.Combine(folder, $"{f.Workspace:N}.lock"));
            Throws<FileNotFoundException>(() => provider.ReadTransition(f.Workspace));
        });
        Check("provider refuses wrong HMAC key, modified and oversized state", () =>
        {
            string folder = f.Folder();
            using var provider = f.Open(folder, provision: true);
            using var wrong = new VaultTransitionFilePrototype(folder, new byte[32]);
            Throws<CryptographicException>(() => wrong.ReadTransition(f.Workspace));
            string path = StateFile(folder);
            byte[] bytes = File.ReadAllBytes(path);
            bytes[^1] ^= 1;
            File.WriteAllBytes(path, bytes);
            Throws<CryptographicException>(() => provider.ReadTransition(f.Workspace));
            File.WriteAllBytes(path, new byte[1024]);
            Throws<InvalidDataException>(() => provider.ReadTransition(f.Workspace));
        });
        Check("provider refuses a valid state copied across workspace slots", () =>
        {
            string folder = f.Folder();
            using var provider = f.Open(folder, provision: true);
            var other = new VaultGenerationStamp(Guid.NewGuid(), 1, f.Initial.Stamp.ManifestSha256);
            provider.Provision(other);
            File.Copy(Path.Combine(folder, $"{f.Workspace:N}.state"), Path.Combine(folder, $"{other.WorkspaceId:N}.state"), true);
            Throws<InvalidDataException>(() => provider.ReadTransition(other.WorkspaceId));
        });
        Check("lost acknowledgement before commit leaves a persistent pending intent", () =>
        {
            string folder = f.Folder();
            var data = f.Data(f.Folder());
            using (var provider = f.Open(folder, provision: true))
            {
                var coordinator = new VaultGenerationRecovery(new LostReply(provider, commitWritten: false), data);
                Equal(VaultGenerationAdvanceState.PriorObserved, coordinator.StageAndAdvance(f.Initial.Stamp, f.Next));
            }
            using var restarted = f.Open(folder);
            Equal(f.Intent, restarted.ReadTransition(f.Workspace).Pending);
            Equal(VaultGenerationAdvanceState.Committed, new VaultGenerationRecovery(restarted, data).Resume(f.Initial.Stamp));
        });
        Check("lost acknowledgement after commit resolves from the persistent receipt", () =>
        {
            string folder = f.Folder();
            var data = f.Data(f.Folder());
            using (var provider = f.Open(folder, provision: true))
            {
                var coordinator = new VaultGenerationRecovery(new LostReply(provider, commitWritten: true), data);
                Equal(VaultGenerationAdvanceState.Committed, coordinator.StageAndAdvance(f.Initial.Stamp, f.Next));
            }
            using var restarted = f.Open(folder);
            Equal(VaultGenerationAdvanceState.Committed, new VaultGenerationRecovery(restarted, data).Resume(f.Initial.Stamp));
        });
        Check("lost candidate fails closed until the exact candidate is restored separately", () =>
        {
            using var provider = f.Open(f.Folder(), provision: true);
            var data = f.Data(f.Folder());
            provider.ReserveTransition(f.Intent);
            var coordinator = new VaultGenerationRecovery(provider, data);
            Throws<CryptographicException>(() => coordinator.Resume(f.Initial.Stamp));
            Throws<CryptographicException>(() => coordinator.RestageReservedCandidate(f.Initial.Stamp, f.Rival));
            Equal<VaultGenerationManifest?>(null, data.Read(f.Rival.Stamp));
            coordinator.RestageReservedCandidate(f.Initial.Stamp, f.Next);
            coordinator.RestageReservedCandidate(f.Initial.Stamp, f.Next);
            Equal(new VaultTransitionState(f.Initial.Stamp, f.Intent), provider.ReadTransition(f.Workspace));
            Equal(VaultGenerationAdvanceState.Committed, coordinator.Resume(f.Initial.Stamp));
            Throws<CryptographicException>(() => coordinator.RestageReservedCandidate(f.Initial.Stamp, f.Next));
        });
        Check("stale or pending generation cannot acquire a provider fence", () =>
        {
            using var provider = f.Open(f.Folder(), provision: true);
            Throws<CryptographicException>(() => provider.EnterKeyUse(f.Next.Stamp));
            provider.ReserveTransition(f.Intent);
            Throws<CryptographicException>(() => provider.EnterKeyUse(f.Initial.Stamp));
            provider.CommitTransition(f.Intent);
            Throws<CryptographicException>(() => provider.EnterKeyUse(f.Initial.Stamp));
            using var _ = provider.EnterKeyUse(f.Next.Stamp);
        });
        Check("RISK restoring an authentic provider snapshot rolls back its state", () =>
        {
            string folder = f.Folder();
            using var provider = f.Open(folder, provision: true);
            string path = StateFile(folder);
            byte[] prior = File.ReadAllBytes(path);
            provider.ReserveTransition(f.Intent);
            provider.CommitTransition(f.Intent);
            File.WriteAllBytes(path, prior);
            Equal(new VaultTransitionState(f.Initial.Stamp), provider.ReadTransition(f.Workspace));
            provider.ReserveTransition(f.RivalIntent);
            Equal(f.RivalIntent, provider.ReadTransition(f.Workspace).Pending);
        });
    }

    private static string StateFile(string folder) => Directory.EnumerateFiles(folder, "*.state").Single();

    private sealed class LostReply(VaultTransitionFilePrototype disk, bool commitWritten) : IExternalVaultTransitionAnchor
    {
        public VaultTransitionState ReadTransition(Guid workspaceId) => disk.ReadTransition(workspaceId);
        public void ReserveTransition(VaultRetryIntent intent)
        {
            disk.ReserveTransition(intent);
            throw new VaultGenerationOutcomeUnknownException("Injected lost reservation reply.");
        }
        public void CommitTransition(VaultRetryIntent intent)
        {
            if (commitWritten) disk.CommitTransition(intent);
            throw new VaultGenerationOutcomeUnknownException("Injected lost commit reply.");
        }
    }

    private static void WindowsChecks(Fixture f)
    {
        Check("Windows fenced callback uses exact file identity and disposes the supplied key", () =>
        {
            using var provider = f.Open(f.Folder(), provision: true);
            var gate = new TrustedVaultOpenGate(new VaultGenerationRecovery(provider, f.Data(f.Folder())));
            using var portable = PortableEmd.Encrypt(Markdown, Password);
            byte[] wrapped = LayeredEmd3.Seal(portable, [f.Root]);
            LayerSecret? captured = null;
            gate.UseFileBound(f.VaultPath, f.Workspace, f.Root.Id, LayerKind.Folder, Password, secret =>
            {
                captured = secret;
                Equal(Markdown, LayeredEmd3.Open(wrapped, [secret], Password));
            });
            Throws<ObjectDisposedException>(() => LayeredEmd3.Open(wrapped, [captured!], Password));
            provider.ReserveTransition(f.Intent);
        });
        Check("Windows fenced callback fails closed without a fence or with pending state", () =>
        {
            using var provider = f.Open(f.Folder(), provision: true);
            var data = f.Data(f.Folder());
            var unsupported = new TrustedVaultOpenGate(new VaultGenerationRecovery(new LostReply(provider, false), data));
            bool called = false;
            Throws<NotSupportedException>(() => unsupported.UseFileBound(f.VaultPath, f.Workspace, f.Root.Id,
                LayerKind.Folder, Password, _ => called = true));
            provider.ReserveTransition(f.Intent);
            var gate = new TrustedVaultOpenGate(new VaultGenerationRecovery(provider, data));
            Throws<CryptographicException>(() => gate.UseFileBound(f.VaultPath, f.Workspace, f.Root.Id,
                LayerKind.Folder, Password, _ => called = true));
            Equal(false, called);
        });
        Check("Windows callback failure still releases the fence and disposes the key", () =>
        {
            using var provider = f.Open(f.Folder(), provision: true);
            var gate = new TrustedVaultOpenGate(new VaultGenerationRecovery(provider, f.Data(f.Folder())));
            LayerSecret? captured = null;
            Throws<InvalidOperationException>(() => gate.UseFileBound(f.VaultPath, f.Workspace, f.Root.Id,
                LayerKind.Folder, Password, secret => { captured = secret; throw new InvalidOperationException(); }));
            Equal(f.Root.Id, captured?.Id);
            provider.ReserveTransition(f.Intent);
        });
    }
}
