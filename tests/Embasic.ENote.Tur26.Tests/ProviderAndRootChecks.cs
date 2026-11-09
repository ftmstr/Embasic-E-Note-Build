using System.Security.Cryptography;
using Embasic.ENote.Emd;
using Embasic.ENote.Storage;

internal static partial class Program
{
    private static void ProviderChecks(Fixture f)
    {
        Check("pinned provider identity survives restart and rejects another identity with the same key", () =>
        {
            string folder = f.Folder();
            using (var provider = f.Provider(folder))
                provider.ReserveTransition(f.Intent);
            using var correct = f.Provider(folder, provision: false);
            Equal(f.Intent, correct.ReadTransition(f.Workspace).Pending);
            using var wrong = new VaultTransitionFilePrototype(folder, Key, Guid.NewGuid());
            Throws<InvalidDataException>(() => wrong.ReadTransition(f.Workspace));
        });
        Check("pinned mode and legacy mode reject each other's state without downgrade", () =>
        {
            string legacyFolder = f.Folder();
            using var legacy = new VaultTransitionFilePrototype(legacyFolder, Key);
            legacy.Provision(f.Initial.Stamp);
            using var strict = f.Provider(legacyFolder, provision: false);
            Throws<InvalidDataException>(() => strict.ReadTransition(f.Workspace));
            string pinnedFolder = f.Folder();
            using var pinned = f.Provider(pinnedFolder);
            using var downgrade = new VaultTransitionFilePrototype(pinnedFolder, Key);
            Throws<InvalidDataException>(() => downgrade.ReadTransition(f.Workspace));
        });
        Check("RISK the same pinned provider identity still accepts a valid historical state", () =>
        {
            string folder = f.Folder();
            using var provider = f.Provider(folder);
            string state = StateFile(folder);
            byte[] old = File.ReadAllBytes(state);
            provider.ReserveTransition(f.Intent);
            provider.CommitTransition(f.Intent);
            File.WriteAllBytes(state, old);
            Equal(f.Initial.Stamp, provider.ReadTransition(f.Workspace).Current);
        });
        Check("an opt-in operation gate refuses raw open and path-bound alternatives", () =>
        {
            using var provider = f.Provider();
            var strict = new TrustedVaultOpenGate(new VaultGenerationRecovery(provider, f.Data()),
                requireOperationScope: true);
            Throws<NotSupportedException>(() => strict.Unlock(f.VaultPath, f.Workspace, f.Root.Id, LayerKind.Folder, Password));
            Throws<NotSupportedException>(() => strict.UnlockPathBound(f.VaultPath, f.Workspace, f.Root.Id, LayerKind.Folder, Password));
            Throws<NotSupportedException>(() => strict.UseFileBound(f.VaultPath, f.Workspace, f.Root.Id,
                LayerKind.Folder, Password, _ => throw new InvalidOperationException("Must not call raw delegate.")));
        });
        Check("RISK standalone legacy decryption bypasses the operation-only gate when credentials and files are available", () =>
        {
            using var key = LayerKeyVault.Open(f.RootVault, f.Root.Id, LayerKind.Folder, Password);
            Equal(Markdown, LayeredEmd3.Open(f.Direct, [key], Password));
        });
    }

    private static void RootChecks(Fixture f)
    {
        Check("lost authorization acknowledgement before write observes prior revision", () =>
        {
            using var provider = f.Provider();
            var fault = new FaultyAuthority(provider, () => { });
            Equal(VaultRootRecoveryUpdateState.PriorObserved,
                VaultRootRecoveryUpdates.Change(fault, f.Initial.Stamp, 0, f.Backup.Proof));
            Equal(new VaultRootRecoveryAuthorization(0, null), provider.ReadAuthorization(f.Workspace));
        });
        Check("lost acknowledgement after enroll and revoke reconciles exact durable revision", () =>
        {
            string folder = f.Folder();
            using (var provider = f.Provider(folder))
            {
                var enrolled = new FaultyAuthority(provider,
                    () => provider.CompareAndSetAuthorization(f.Initial.Stamp, 0, f.Backup.Proof));
                Equal(VaultRootRecoveryUpdateState.Applied,
                    VaultRootRecoveryUpdates.Change(enrolled, f.Initial.Stamp, 0, f.Backup.Proof));
            }
            using var restarted = f.Provider(folder, provision: false);
            Equal(new VaultRootRecoveryAuthorization(1, f.Backup.Proof), restarted.ObserveAuthorization(f.Initial.Stamp));
            var revoked = new FaultyAuthority(restarted,
                () => restarted.CompareAndSetAuthorization(f.Initial.Stamp, 1, null));
            Equal(VaultRootRecoveryUpdateState.Applied,
                VaultRootRecoveryUpdates.Change(revoked, f.Initial.Stamp, 1, null));
            Equal(new VaultRootRecoveryAuthorization(2, null), restarted.ObserveAuthorization(f.Initial.Stamp));
        });
        Check("lost authorization acknowledgement cannot mistake generation advance for revoke", () =>
        {
            using var provider = f.Provider();
            var fault = new FaultyAuthority(provider, () =>
            {
                provider.ReserveTransition(f.Intent);
                provider.CommitTransition(f.Intent);
            });
            Throws<CryptographicException>(() => VaultRootRecoveryUpdates.Change(fault, f.Initial.Stamp, 0, null));
            Equal(new VaultRootRecoveryAuthorization(1, null), provider.ObserveAuthorization(f.Next.Stamp));
        });
        Check("pending and competing replacement invalidate ambiguous authorization readback", () =>
        {
            using var first = f.Provider();
            var pending = new FaultyAuthority(first, () => first.ReserveTransition(f.Intent));
            Throws<CryptographicException>(() => VaultRootRecoveryUpdates.Change(pending, f.Initial.Stamp, 0, null));
            using var second = f.Provider();
            var other = f.Backup.Proof with { IndexSha256 = new string('A', 64) };
            var rival = new FaultyAuthority(second, () => second.CompareAndSetAuthorization(f.Initial.Stamp, 0, other));
            Throws<CryptographicException>(() => VaultRootRecoveryUpdates.Change(rival, f.Initial.Stamp, 0, f.Backup.Proof));
        });
        Check("independent provider handles serialize a root enrollment and transition race", () =>
        {
            string folder = f.Folder();
            using var first = f.Provider(folder);
            using var second = f.Provider(folder, provision: false);
            using var start = new ManualResetEventSlim();
            var enrollment = Task.Run(() =>
            {
                start.Wait();
                try
                {
                    first.CompareAndSetAuthorization(f.Initial.Stamp, 0, f.Backup.Proof);
                    return true;
                }
                catch (CryptographicException) { return false; }
            });
            var transition = Task.Run(() =>
            {
                start.Wait();
                second.ReserveTransition(f.Intent);
            });
            start.Set();
            Task.WhenAll(enrollment, transition).GetAwaiter().GetResult();
            bool applied = enrollment.Result;
            Equal(f.Intent, second.ReadTransition(f.Workspace).Pending);
            Equal(new VaultRootRecoveryAuthorization(applied ? 1 : 0, applied ? f.Backup.Proof : null),
                first.ReadAuthorization(f.Workspace));
            second.CommitTransition(f.Intent);
            Equal(new VaultRootRecoveryAuthorization(applied ? 2 : 1, null),
                first.ObserveAuthorization(f.Next.Stamp));
        });
        Check("missing authority state fails closed and cannot be recreated over the marker", () =>
        {
            string folder = f.Folder();
            using var provider = f.Provider(folder);
            provider.CompareAndSetAuthorization(f.Initial.Stamp, 0, f.Backup.Proof);
            var data = f.Data();
            File.Delete(StateFile(folder));
            var recovery = new VaultRootRecovery(new VaultGenerationRecovery(provider, data), provider);
            Throws<FileNotFoundException>(() => recovery.Restore(f.Workspace, f.Backup.Envelope, f.Index, BackupPassword));
            Throws<FileNotFoundException>(() =>
                VaultRootRecoveryUpdates.Change(provider, f.Initial.Stamp, 1, f.Backup.Proof));
            Throws<IOException>(() => provider.Provision(f.Initial.Stamp));
        });
        Check("re-enrollment needs latest revision and old proof cannot enroll in a new generation", () =>
        {
            using var provider = f.Provider();
            Equal(VaultRootRecoveryUpdateState.Applied,
                VaultRootRecoveryUpdates.Change(provider, f.Initial.Stamp, 0, f.Backup.Proof));
            VaultRootRecoveryUpdates.Change(provider, f.Initial.Stamp, 1, null);
            Throws<CryptographicException>(() =>
                VaultRootRecoveryUpdates.Change(provider, f.Initial.Stamp, 1, f.Backup.Proof));
            VaultRootRecoveryUpdates.Change(provider, f.Initial.Stamp, 2, f.Backup.Proof);
            Equal(3L, provider.ObserveAuthorization(f.Initial.Stamp).Revision);
            provider.ReserveTransition(f.Intent);
            provider.CommitTransition(f.Intent);
            Throws<ArgumentException>(() =>
                VaultRootRecoveryUpdates.Change(provider, f.Next.Stamp, 4, f.Backup.Proof));
            Equal(new VaultRootRecoveryAuthorization(4, null), provider.ObserveAuthorization(f.Next.Stamp));
        });
    }

    private static string StateFile(string directory) => Directory.EnumerateFiles(directory, "*.state").Single();

    private sealed class FaultyAuthority(VaultTransitionFilePrototype provider, Action mutate) : IVaultRootRecoveryLifecycle
    {
        public VaultRootRecoveryAuthorization ReadAuthorization(Guid workspaceId) => provider.ReadAuthorization(workspaceId);
        public VaultRootRecoveryAuthorization ObserveAuthorization(VaultGenerationStamp expected) => provider.ObserveAuthorization(expected);

        public VaultRootRecoveryAuthorization CompareAndSetAuthorization(VaultGenerationStamp expected,
            long expectedRevision, VaultRootRecoveryProof? replacement)
        {
            mutate();
            throw new VaultGenerationOutcomeUnknownException("Injected lost reply.");
        }
    }

    private static void WindowsChecks(Fixture f)
    {
        Check("strict Windows gate reads scoped layered Markdown without returning the root key", () =>
        {
            using var provider = f.Provider();
            var gate = new TrustedVaultOpenGate(new VaultGenerationRecovery(provider, f.Data()), requireOperationScope: true);
            Throws<NotSupportedException>(() =>
                gate.UnlockFileBound(f.VaultPath, f.Workspace, f.Root.Id, LayerKind.Folder, Password));
            Equal(Markdown, gate.ReadLayeredMarkdownFileBound(f.VaultPath, f.Workspace, f.Root.Id, LayerKind.Folder,
                Password, f.Inherited, Password, [f.Child]));
            provider.ReserveTransition(f.Intent);
            Throws<CryptographicException>(() => gate.ReadLayeredMarkdownFileBound(f.VaultPath, f.Workspace, f.Root.Id,
                LayerKind.Folder, Password, f.Inherited, Password, [f.Child]));
        });
        Check("strict Windows read refuses a cloned vault and releases the fence on failure", () =>
        {
            using var provider = f.Provider();
            var gate = new TrustedVaultOpenGate(new VaultGenerationRecovery(provider, f.Data()), requireOperationScope: true);
            string clone = Path.Combine(f.Folder(), "Clone.elkey");
            File.Copy(f.VaultPath, clone);
            Throws<CryptographicException>(() => gate.ReadLayeredMarkdownFileBound(clone, f.Workspace, f.Root.Id,
                LayerKind.Folder, Password, f.Direct, Password));
            provider.ReserveTransition(f.Intent);
            Equal(f.Intent, provider.ReadTransition(f.Workspace).Pending);
        });
    }

    private static void LinuxChecks(Fixture f)
    {
        Check("pinned provider refuses a linked directory ancestor and linked state or marker", () =>
        {
            string parent = f.Folder();
            string real = Directory.CreateDirectory(Path.Combine(parent, "real")).FullName;
            string alias = Path.Combine(f.Folder(), "alias");
            Directory.CreateSymbolicLink(alias, parent);
            Throws<IOException>(() => f.Provider(Path.Combine(alias, "real"), provision: false));
            using var provider = f.Provider(real);
            string state = StateFile(real);
            string moved = Path.Combine(real, "state-original");
            File.Move(state, moved);
            File.CreateSymbolicLink(state, moved);
            Throws<IOException>(() => provider.ReadTransition(f.Workspace));
            File.Delete(state);
            File.Move(moved, state);
            string marker = Directory.EnumerateFiles(real, "*.lock").Single();
            string oldMarker = Path.Combine(real, "marker-original");
            File.Move(marker, oldMarker);
            File.CreateSymbolicLink(marker, oldMarker);
            Throws<IOException>(() => provider.ReadTransition(f.Workspace));
        });
    }
}
