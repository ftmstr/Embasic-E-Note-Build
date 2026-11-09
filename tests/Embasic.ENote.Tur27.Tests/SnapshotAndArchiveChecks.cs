using System.Security.Cryptography;
using Embasic.ENote.Emd;
using Embasic.ENote.Storage;

internal static partial class Program
{
    private static void SnapshotChecks()
    {
        Check("first authoritative enrollment captures inherited ELK1 and leaves Markdown outside the catalog", () =>
        {
            using var f = new Fixture();
            var snapshot = f.Capture();
            Equal(3, snapshot.Documents.Count);
            Equal(Digest(f.Inherited), snapshot.Documents.Single(x => x.RelativePath == "Vault/Sub/Inherited.emd").SourceSha256);
            Equal(snapshot.CatalogSha256, f.Enroll().Bindings?.CatalogSha256);
            Equal("# original Markdown", File.ReadAllText(Path.Combine(f.Live, "Readme.md")));
            Equal(VaultGenerationAdvanceState.Committed,
                f.Recovery.EnrollCatalog(f.Original.Stamp, snapshot, f.Root, f.Live));
            Throws<CryptographicException>(() => f.Recovery.EnrollCatalog(f.Source!.Stamp, snapshot, f.Root, f.Live));
        });
        Check("missing explicit ELK1 prevents creating an enrollable snapshot", () =>
        {
            using var f = new Fixture();
            File.Delete(Path.Combine(f.Live, "Sub", "Explicit.emd"));
            Throws<InvalidDataException>(() => f.Capture());
            Equal(false, Directory.Exists(f.SnapshotPath));
            Equal(f.Original.Stamp, f.Provider.ReadTransition(f.Workspace).Current);
        });
        Check("new inherited ELK1 or edited live bytes after capture blocks first enrollment", () =>
        {
            using var f = new Fixture();
            var snapshot = f.Capture();
            string added = Path.Combine(f.Live, "Sub", "New.emd");
            File.WriteAllBytes(added, f.Inherited);
            Throws<CryptographicException>(() => f.Enroll());
            File.Delete(added);
            File.WriteAllBytes(Path.Combine(f.Live, "Direct.emd"), f.Inherited);
            Throws<CryptographicException>(() => f.Enroll());
            Equal(snapshot.CatalogSha256, f.Snapshot!.CatalogSha256);
            Equal<VaultRetryIntent?>(null, f.Provider.ReadTransition(f.Workspace).Pending);
        });
        Check("non-ELK1 .emd and overlapping snapshot roots fail closed", () =>
        {
            using var f = new Fixture();
            Throws<ArgumentException>(() => VaultWorkspaceSnapshot.Capture(f.Live, Path.Combine(f.Live, "copy"),
                f.Original, f.Root, f.Index));
            File.WriteAllBytes(Path.Combine(f.Live, "Other.emd"), "EMD2fixture"u8.ToArray());
            Throws<InvalidDataException>(() => f.Capture());
        });
        Check("missing source directory prevents snapshot capture", () =>
        {
            using var f = new Fixture();
            string invalid = Path.Combine(f.Home, "missing");
            Throws<IOException>(() => VaultWorkspaceSnapshot.Capture(invalid, f.SnapshotPath, f.Original, f.Root, f.Index));
        });
        if (OperatingSystem.IsLinux())
            Check("Linux snapshot refuses linked entries before enrollment", () =>
            {
                using var f = new Fixture();
                File.CreateSymbolicLink(Path.Combine(f.Live, "Alias.emd"), Path.Combine(f.Live, "Direct.emd"));
                Throws<IOException>(() => f.Capture());
            });
        else Console.WriteLine("NOT RUN Linux snapshot link test");
        Check("published ciphertext uses a new grant and decrypts with the intended rotated factors", () =>
        {
            using var f = new Fixture();
            var next = f.Prepare();
            var commit = f.Commitment!;
            var item = commit.Inventory.Artifacts.Single(x => x.RelativePath == "Vault/Sub/Inherited.emd");
            using var published = OpenPublication(f.Home, f.Store);
            using var source = published.OpenPublished(item, commit.Targets[item.Id]);
            using var content = new MemoryStream();
            source.CopyTo(content);
            Equal(Markdown, LayeredEmd3.Open(content.ToArray(), [f.Fresh, f.Child], NextPassword));
            string path = next.Find(f.Root.Id, LayerKind.Folder)!.BoundVaultPath!;
            Equal(f.Published, Path.GetDirectoryName(path));
            using var newKey = LayerKeyVault.Open(File.ReadAllBytes(path), f.Root.Id, LayerKind.Folder, NextPassword);
            Equal(f.Root.Id, newKey.Id);
            Equal(f.Source!.Stamp, f.Provider.ReadTransition(f.Workspace).Current);
            Equal(VaultGenerationAdvanceState.Committed, published.Resume(f.Recovery, next.Stamp, f.Root));
            Equal(next.Stamp, f.Recovery.ReadCommitted(f.Workspace).Stamp);
            Equal(true, File.ReadAllBytes(Path.Combine(f.Live, "Sub", "Inherited.emd")).SequenceEqual(f.Inherited));
        });
        Check("interrupted materialization reuses verified objects without changing the selected generation", () =>
        {
            using var f = new Fixture();
            var commitment = f.Stage();
            var vault = commitment.Inventory.Artifacts.Single(x => x.Kind == VaultRekeyArtifactKind.FactorVault);
            string conflict = Path.Combine(f.Published, commitment.Targets[vault.Id] + ".elkey");
            File.WriteAllBytes(conflict, f.OriginalVault);
            Throws<CryptographicException>(() => f.Prepare());
            Equal(4, Directory.EnumerateFiles(f.Published, "*.emd").Count() +
                Directory.EnumerateFiles(f.Published, "*.elh").Count());
            Equal(0, Directory.EnumerateFiles(f.Journal, "*.erj").Count());
            Equal(f.Source!.Stamp, f.Provider.ReadTransition(f.Workspace).Current);
            File.Delete(conflict);
            var next = f.Prepare();
            using var published = OpenPublication(f.Home, f.Store);
            Equal(VaultGenerationAdvanceState.Committed, published.Resume(f.Recovery, next.Stamp, f.Root));
        });
        Check("a valid encrypted vault with the wrong factor identity cannot register as a candidate", () =>
        {
            using var f = new Fixture();
            using var wrong = LayerSecret.Generate(LayerKind.Folder);
            f.Stage(LayerKeyVault.Create(wrong, NextPassword, BackupPassword));
            Throws<CryptographicException>(() => f.Prepare());
            Equal(0, Directory.EnumerateFiles(f.Journal, "*.erj").Count());
            Equal(f.Source!.Stamp, f.Provider.ReadTransition(f.Workspace).Current);
        });
        Check("source workspace edits after preparation block candidate selection", () =>
        {
            using var f = new Fixture();
            var next = f.Prepare();
            File.WriteAllBytes(Path.Combine(f.Live, "Sub", "New.emd"), f.Inherited);
            using var service = OpenPublication(f.Home, f.Store);
            Throws<CryptographicException>(() => service.Resume(f.Recovery, next.Stamp, f.Root));
            Equal(f.Source!.Stamp, f.Provider.ReadTransition(f.Workspace).Current);
            Equal<VaultRetryIntent?>(null, f.Provider.ReadTransition(f.Workspace).Pending);
        });
        Check("mutated frozen snapshot is rejected before switching generation", () =>
        {
            using var f = new Fixture();
            var next = f.Prepare();
            File.Delete(Path.Combine(f.SnapshotPath, "notes", "Sub", "Inherited.emd"));
            using var service = OpenPublication(f.Home, f.Store);
            Throws<CryptographicException>(() => service.Resume(f.Recovery, next.Stamp, f.Root));
            Equal(f.Source!.Stamp, f.Provider.ReadTransition(f.Workspace).Current);
        });
        Check("corrupt published bytes and corrupt restart journal block selection", () =>
        {
            using var f = new Fixture();
            var next = f.Prepare();
            using var service = OpenPublication(f.Home, f.Store);
            string path = Directory.EnumerateFiles(f.Published, "*.elh").Single();
            File.WriteAllBytes(path, f.Index);
            Throws<CryptographicException>(() => service.Resume(f.Recovery, next.Stamp, f.Root));
            string journal = Directory.EnumerateFiles(f.Journal, "*.erj").Single();
            byte[] data = File.ReadAllBytes(journal);
            data[^1] ^= 1;
            File.WriteAllBytes(journal, data);
            Throws<CryptographicException>(() => service.Resume(f.Recovery, next.Stamp, f.Root));
            Equal(f.Source!.Stamp, f.Provider.ReadTransition(f.Workspace).Current);
        });
        Check("a valid restart journal cannot be copied to a different candidate filename", () =>
        {
            using var f = new Fixture();
            var next = f.Prepare();
            var different = new VaultGenerationStamp(f.Workspace, next.Stamp.Epoch, new string('D', 64));
            string source = Directory.EnumerateFiles(f.Journal, "*.erj").Single();
            File.Copy(source, Path.Combine(f.Journal, f.Workspace.ToString("N") + "-" + different.ManifestSha256 + ".erj"));
            using var service = OpenPublication(f.Home, f.Store);
            Throws<CryptographicException>(() => service.Resume(f.Recovery, different, f.Root));
            Equal(f.Source!.Stamp, f.Provider.ReadTransition(f.Workspace).Current);
        });
    }

    private static void ArchiveChecks()
    {
        Check("approved root archive restores exact proof into a newer tombstone and permits backup recovery", () =>
        {
            using var f = new Fixture();
            var packet = PrepareRootArchive(f);
            var approval = new ApprovalReader(new(f.Original.Stamp, 2, Digest(packet.Archive)));
            Equal(VaultRootRecoveryUpdateState.Applied,
                VaultRootAuthorizationArchive.ReEnroll(f.Provider, approval, f.Original.Stamp, packet.Archive));
            Equal(VaultRootRecoveryUpdateState.Applied,
                VaultRootAuthorizationArchive.ReEnroll(f.Provider, approval, f.Original.Stamp, packet.Archive));
            Equal(new VaultRootRecoveryAuthorization(3, packet.Backup.Proof), f.Provider.ObserveAuthorization(f.Original.Stamp));
            var recovery = new VaultRootRecovery(f.Recovery, f.Provider);
            using var key = recovery.Restore(f.Workspace, packet.Backup.Envelope, f.Index, BackupPassword);
            Equal(f.Root.Id, key.Id);
        });
        Check("tampered or missing root archive and stale tombstone are refused", () =>
        {
            using var f = new Fixture();
            var packet = PrepareRootArchive(f);
            var approval = new ApprovalReader(new(f.Original.Stamp, 2, Digest(packet.Archive)));
            byte[] tampered = (byte[])packet.Archive.Clone();
            tampered[^1] ^= 1;
            Throws<CryptographicException>(() =>
                VaultRootAuthorizationArchive.ReEnroll(f.Provider, approval, f.Original.Stamp, tampered));
            Throws<InvalidDataException>(() =>
                VaultRootAuthorizationArchive.ReEnroll(f.Provider, approval, f.Original.Stamp, []));
            f.Provider.CompareAndSetAuthorization(f.Original.Stamp, 2, null);
            Throws<CryptographicException>(() =>
                VaultRootAuthorizationArchive.ReEnroll(f.Provider, approval, f.Original.Stamp, packet.Archive));
        });
        Check("revoked approval and another selected generation block re-enrollment", () =>
        {
            using var f = new Fixture();
            var packet = PrepareRootArchive(f);
            var approval = new ApprovalReader(new(f.Original.Stamp, 2, Digest(packet.Archive)), revokeOnSecond: true);
            Throws<CryptographicException>(() =>
                VaultRootAuthorizationArchive.ReEnroll(f.Provider, approval, f.Original.Stamp, packet.Archive));
            var newGeneration = f.Enroll();
            var stale = new ApprovalReader(new(f.Original.Stamp, 2, Digest(packet.Archive)));
            Throws<CryptographicException>(() =>
                VaultRootAuthorizationArchive.ReEnroll(f.Provider, stale, f.Original.Stamp, packet.Archive));
            Equal<VaultRootRecoveryProof?>(null, f.Provider.ObserveAuthorization(newGeneration.Stamp).Proof);
        });
        Check("a backup cannot resurrect wholly missing provider state", () =>
        {
            using var f = new Fixture();
            var packet = PrepareRootArchive(f);
            var approval = new ApprovalReader(new(f.Original.Stamp, 2, Digest(packet.Archive)));
            File.Delete(Directory.EnumerateFiles(Path.Combine(f.Home, "authority"), "*.state").Single());
            Throws<FileNotFoundException>(() =>
                VaultRootAuthorizationArchive.ReEnroll(f.Provider, approval, f.Original.Stamp, packet.Archive));
            Throws<IOException>(() => f.Provider.Provision(f.Original.Stamp));
        });
    }

    private static (VaultRootRecoveryBackup Backup, byte[] Archive) PrepareRootArchive(Fixture f)
    {
        var backup = VaultRootRecovery.Prepare(f.Original, f.Root.Id, f.OriginalVault, Password, f.Index, BackupPassword);
        f.Provider.CompareAndSetAuthorization(f.Original.Stamp, 0, backup.Proof);
        byte[] archive = VaultRootAuthorizationArchive.Capture(f.Provider, f.Original.Stamp);
        f.Provider.CompareAndSetAuthorization(f.Original.Stamp, 1, null);
        return (backup, archive);
    }

    private sealed class ApprovalReader(VaultRootReEnrollmentApproval approval, bool revokeOnSecond = false)
        : IVaultRootReEnrollmentApprovalReader
    {
        private int _reads;
        public VaultRootReEnrollmentApproval? Read(VaultGenerationStamp generation) =>
            generation == approval.Generation && (++_reads == 1 || !revokeOnSecond) ? approval : null;
    }
}
