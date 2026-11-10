using System.Globalization;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using Embasic.ENote.Emd;
using Embasic.ENote.Storage;

internal static partial class Program
{
    private static void Tur31LiveChecks()
    {
        Check("force-killed writer releases the live fence and resumes after an exact first replacement", () =>
        {
            using var f = new Fixture();
            using var selectedRoot = LayerSecret.Regenerate(f.Root);
            var candidate = SelectRootRekey(f, selectedRoot);
            string direct = Path.Combine(f.Live, "Direct.emd");
            string inherited = Path.Combine(f.Live, "Sub", "Inherited.emd");
            string marker = Path.Combine(f.Home, "first-write-confirmed");
            using var worker = StartHarness("tur31-live-worker", f.Home, f.Workspace.ToString("D"),
                f.ProviderId.ToString("D"), f.Root.Id.ToString("D"), candidate.Epoch.ToString(CultureInfo.InvariantCulture),
                candidate.ManifestSha256, Digest(f.Direct));
            try
            {
                Equal(true, SpinWait.SpinUntil(() => File.Exists(marker) || worker.HasExited, TimeSpan.FromSeconds(25)));
                Equal(false, worker.HasExited);
                Equal(true, File.Exists(marker));
                Equal(Digest(f.Inherited), Digest(File.ReadAllBytes(inherited)));
                Equal(Text, LayeredEmd3.Open(File.ReadAllBytes(direct), [selectedRoot], NewPassword));
                Throws<IOException>(() => f.Published.EnterLiveWrite().Dispose());
            }
            finally
            {
                if (!worker.HasExited) worker.Kill();
                if (!worker.WaitForExit(10_000)) throw new TimeoutException("Killed synchronization worker did not exit.");
            }
            using var reopened = Publication(f.Home);
            Equal(new VaultLiveSyncResult(1, 0), reopened.SynchronizeSelectedLiveTree(f.Recovery, candidate, f.Root, selectedRoot));
            Equal(new VaultLiveSyncResult(0, 0), reopened.SynchronizeSelectedLiveTree(f.Recovery, candidate, f.Root, selectedRoot));
            Equal(Text, LayeredEmd3.Open(File.ReadAllBytes(inherited), [selectedRoot, f.Child], NewPassword));
        });

        Check("selected snapshot verifies nested documents and vaults and refuses mixed workspace publication or journals", () =>
        {
            using var f = new Fixture();
            using var foreign = new Fixture();
            using var selectedRoot = LayerSecret.Regenerate(f.Root);
            var selected = SelectRootRekey(f, selectedRoot);
            var published = Path.Combine(f.Home, "published");
            var directory = Directory.EnumerateDirectories(published, "catalog-*-" + selected.ManifestSha256).Single();
            var note = Path.Combine(directory, "notes", "Sub", "unattributed.txt");
            File.WriteAllText(note, "keep");
            var result = f.Published.InspectSelectedCatalog(f.Recovery, f.Workspace, selectedRoot);
            Equal(2, result.Documents);
            Equal(2, result.Vaults);
            Equal("notes/Sub/unattributed.txt", result.UnclassifiedEntries.Single());

            string foreignCatalog = Directory.CreateDirectory(Path.Combine(published,
                "catalog-" + foreign.Workspace.ToString("N") + "-" + new string('A', 64))).FullName;
            Throws<InvalidDataException>(() => f.Published.InspectSelectedCatalog(f.Recovery, f.Workspace, selectedRoot));
            Throws<InvalidDataException>(() => f.Published.InspectReachability(f.Recovery, f.Workspace));
            Directory.Delete(foreignCatalog);

            using var freshForeign = LayerSecret.Regenerate(foreign.Child);
            var foreignCandidate = foreign.Prepare(freshForeign).Manifest.Stamp;
            File.Copy(foreign.Journal(foreignCandidate), f.Journal(foreignCandidate));
            Throws<InvalidDataException>(() => f.Published.InspectSelectedCatalog(f.Recovery, f.Workspace, selectedRoot));
            File.Delete(f.Journal(foreignCandidate));
            Equal(true, File.Exists(note));
            File.Delete(Path.Combine(directory, "notes", "Sub", "Inherited.emd"));
            Throws<CryptographicException>(() => f.Published.InspectSelectedCatalog(f.Recovery, f.Workspace, selectedRoot));
        });
    }

    [SupportedOSPlatform("windows")]
    private static int RunWindowsLiveAcceptance()
    {
        Check("Windows sharing denial preserves live bytes and replacement retains the explicit file DACL", () =>
        {
            using var f = new Fixture();
            using var selectedRoot = LayerSecret.Regenerate(f.Root);
            var candidate = SelectRootRekey(f, selectedRoot);
            var direct = new FileInfo(Path.Combine(f.Live, "Direct.emd"));
            using var identity = WindowsIdentity.GetCurrent();
            var owner = identity.User!;
            var acl = new FileSecurity();
            acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            acl.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl, AccessControlType.Allow));
            direct.SetAccessControl(acl);
            string original = direct.GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorSddlForm(AccessControlSections.Access);
            using (var reader = direct.Open(FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Throws<IOException>(() => f.Published.SynchronizeSelectedLiveTree(f.Recovery, candidate, f.Root, selectedRoot, 1));
                Equal(Digest(f.Direct), Digest(File.ReadAllBytes(direct.FullName)));
                Equal(0, Directory.EnumerateFiles(f.Live, "*.enote-sync").Count());
            }
            Equal(new VaultLiveSyncResult(1, 1),
                f.Published.SynchronizeSelectedLiveTree(f.Recovery, candidate, f.Root, selectedRoot, 1));
            string updated = direct.GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorSddlForm(AccessControlSections.Access);
            Equal(original, updated);
            Equal(Text, LayeredEmd3.Open(File.ReadAllBytes(direct.FullName), [selectedRoot], NewPassword));
        });
        Console.WriteLine($"{_passed}/{_total} Tur31 Windows live file DACL and sharing checks passed.");
        return _passed == _total ? 0 : 1;
    }

    private static VaultGenerationStamp SelectRootRekey(Fixture f, LayerSecret root)
    {
        f.Enroll();
        var candidate = PrepareRoot(f, f.Root, root, Password, NewPassword).Next.Stamp;
        Equal(VaultGenerationAdvanceState.Committed, f.Published.Resume(f.Recovery, candidate, f.Root));
        f.Published.RegisterSelectedSnapshot(f.Recovery, candidate, f.Root, root);
        return candidate;
    }

    private static int RunLiveWorker(string[] args)
    {
        string home = args[1];
        var candidate = new VaultGenerationStamp(Guid.Parse(args[2]), long.Parse(args[5], CultureInfo.InvariantCulture), args[6]);
        var factor = Guid.Parse(args[4]);
        var store = Store(home);
        var manifest = store.Read(candidate)!;
        using var priorRoot = LayerKeyVault.Open(File.ReadAllBytes(Path.Combine(home, "root.elkey")), factor, LayerKind.Folder, Password);
        using var nextRoot = LayerKeyVault.Open(File.ReadAllBytes(manifest.Find(factor, LayerKind.Folder)!.BoundVaultPath!),
            factor, LayerKind.Folder, NewPassword);
        using var provider = new VaultTransitionFilePrototype(Path.Combine(home, "authority"), ProviderKey, Guid.Parse(args[3]));
        using var publication = Publication(home);
        if (args[0] == "tur32-copy-worker")
        {
            publication.SynchronizeSelectedLiveTree(new VaultGenerationRecovery(provider, store), candidate, priorRoot, nextRoot,
                int.MaxValue, output =>
                {
                    output.Flush(flushToDisk: true);
                    File.WriteAllText(Path.Combine(home, "midcopy-confirmed"), "partial ELK1 flushed");
                    Thread.Sleep(TimeSpan.FromSeconds(35));
                });
            return 3;
        }
        var observed = new PausingAnchor(provider, home, args[7]);
        publication.SynchronizeSelectedLiveTree(new VaultGenerationRecovery(observed, store), candidate, priorRoot, nextRoot);
        return 3;
    }

    private sealed class PausingAnchor(VaultTransitionFilePrototype inner, string home, string oldDigest)
        : IExternalVaultTransitionAnchor
    {
        public VaultTransitionState? ReadTransition(Guid workspaceId)
        {
            if (Digest(File.ReadAllBytes(Path.Combine(home, "live", "Direct.emd"))) != oldDigest)
            {
                File.WriteAllText(Path.Combine(home, "first-write-confirmed"), "paused inside writer fence");
                Thread.Sleep(TimeSpan.FromSeconds(35));
            }
            return inner.ReadTransition(workspaceId);
        }

        public void ReserveTransition(VaultRetryIntent intent) => inner.ReserveTransition(intent);
        public void CommitTransition(VaultRetryIntent intent) => inner.CommitTransition(intent);
    }
}
