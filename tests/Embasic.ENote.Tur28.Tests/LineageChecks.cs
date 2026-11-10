using System.Security.Cryptography;
using Embasic.ENote.Emd;
using Embasic.ENote.Storage;

internal static partial class Program
{
    private const string InheritedPath = "Vault/Sub/Inherited.emd";
    private const string InheritedArtifact = "note:" + InheritedPath;

    private static void LineageChecks()
    {
        Check("enrollment, successive selected rekeys and unchanged inherited catalog entries", () =>
        {
            using var f = new Fixture();
            Equal(VaultGenerationAdvanceState.Committed, f.Published.CaptureAndEnroll(f.Recovery, f.Workspace, f.Root, f.Index));
            Equal(VaultGenerationAdvanceState.Committed, f.Published.CaptureAndEnroll(f.Recovery, f.Workspace, f.Root, f.Index));
            using var child1 = LayerSecret.Regenerate(f.Child);
            using var child2 = LayerSecret.Regenerate(child1);
            var first = PrepareFromSelected(f, f.Child, child1, Password, NewPassword);
            Throws<CryptographicException>(() => f.Published.RegisterSelectedSnapshot(f.Recovery, first.Stamp, f.Root, f.Root));
            Equal(VaultGenerationAdvanceState.Committed, f.Published.Resume(f.Recovery, first.Stamp, f.Root));
            f.Published.RegisterSelectedSnapshot(f.Recovery, first.Stamp, f.Root, f.Root);
            f.Published.RegisterSelectedSnapshot(f.Recovery, first.Stamp, f.Root, f.Root);
            Throws<CryptographicException>(() => PrepareFromSelected(f, child1, child2, NewPassword, Password));
            byte[] selected = f.Published.ReadSelectedDocument(f.Recovery, f.Workspace, f.Root, InheritedPath);
            using (f.Published.EnterLiveWrite())
                File.WriteAllBytes(Path.Combine(f.Live, "Sub", "Inherited.emd"), selected);
            var second = PrepareFromSelected(f, child1, child2, NewPassword, Password);
            Equal(first.Stamp.Epoch + 1, second.Stamp.Epoch);
            using var restarted = Publication(f.Home);
            Equal(VaultGenerationAdvanceState.Committed, restarted.Resume(f.Recovery, second.Stamp, f.Root));
            restarted.RegisterSelectedSnapshot(f.Recovery, second.Stamp, f.Root, f.Root);
            Equal(Text, LayeredEmd3.Open(restarted.ReadSelectedDocument(f.Recovery, f.Workspace, f.Root, InheritedPath),
                [f.Root, child2], Password));
            Equal(Digest(f.Direct), Digest(restarted.ReadSelectedDocument(f.Recovery, f.Workspace, f.Root, "Vault/Direct.emd")));
            var snapshot = Directory.EnumerateDirectories(Path.Combine(f.Home, "published"),
                "catalog-*-" + second.Stamp.ManifestSha256).Single();
            File.Delete(Path.Combine(snapshot, "notes", "Direct.emd"));
            Throws<CryptographicException>(() => restarted.ReadSelectedDocument(f.Recovery, f.Workspace, f.Root, InheritedPath));
        });

        Check("one protected writer lock excludes capture, catalog registration, restore and pruning across processes", () =>
        {
            using var f = new Fixture();
            using var child = HoldChild(f.Home);
            try
            {
                Equal(true, SpinWait.SpinUntil(() => File.Exists(Path.Combine(f.Home, "held")), TimeSpan.FromSeconds(15)));
                Throws<IOException>(() => f.Published.CaptureAndEnroll(f.Recovery, f.Workspace, f.Root, f.Index));
                Throws<IOException>(() => f.Published.RegisterSelectedSnapshot(f.Recovery, f.Initial.Stamp, f.Root, f.Root));
                Throws<IOException>(() => f.Published.RestorePublishedObject(f.Recovery, f.Initial.Stamp, InheritedArtifact, Stream.Null, f.Root));
                Throws<IOException>(() => f.Published.PruneUnreachableSiblingObjects(f.Recovery, f.Initial.Stamp, f.Root));
                Equal(f.Initial.Stamp, f.Recovery.ReadCommitted(f.Workspace).Stamp);
            }
            finally
            {
                if (!child.HasExited) child.Kill();
                child.WaitForExit();
            }
            Equal(VaultGenerationAdvanceState.Committed, f.Published.CaptureAndEnroll(f.Recovery, f.Workspace, f.Root, f.Index));
        });

        Check("restore requires exact ERJ1 and ciphertext, refuses alternate bytes and ELV1 file recreation", () =>
        {
            using var f = new Fixture();
            using var child = LayerSecret.Regenerate(f.Child);
            var next = f.Prepare(child);
            var candidate = next.Manifest.Stamp;
            string path = Path.Combine(f.Home, "published", next.TargetDigest + ".emd");
            byte[] journal = f.Published.ExportRestartJournal(candidate);
            byte[] backup = File.ReadAllBytes(path);
            File.Delete(path);
            Throws<CryptographicException>(() => f.Published.RestorePublishedObject(f.Recovery, candidate,
                InheritedArtifact, new MemoryStream(backup), f.Root));
            f.Provider.ReserveTransition(new VaultRetryIntent(f.Enrolled!.Stamp, candidate));
            File.Delete(f.Journal(candidate));
            f.Published.RestoreRestartJournal(f.Recovery, candidate, journal);
            byte[] tampered = (byte[])backup.Clone();
            tampered[^1] ^= 1;
            Throws<CryptographicException>(() => f.Published.RestorePublishedObject(f.Recovery, candidate,
                InheritedArtifact, new MemoryStream(tampered), f.Root));
            Equal(false, File.Exists(path));
            Throws<InvalidOperationException>(() => f.Published.RestorePublishedObject(f.Recovery, candidate,
                "vault:" + f.Child.Id.ToString("N"), Stream.Null, f.Root));
            f.Published.RestorePublishedObject(f.Recovery, candidate, InheritedArtifact, new MemoryStream(backup), f.Root);
            Equal(VaultGenerationAdvanceState.Committed, f.Published.Resume(f.Recovery, candidate, f.Root));
            Equal(Digest(backup), Digest(f.Published.ReadSelectedDocument(f.Recovery, f.Workspace, f.Root, InheritedPath)));
        });

        Check("prune proves ancestry and retains shared objects and ELV1 grants while removing unreferenced ciphertext", () =>
        {
            using var f = new Fixture();
            using var child1 = LayerSecret.Regenerate(f.Child);
            using var child2 = LayerSecret.Regenerate(f.Child);
            var winner = f.Prepare(child1);
            var loser = f.Prepare(child2);
            // Register a sibling with the same note bytes as the winner and a distinct vault ciphertext.
            var shared = PrepareFromSelected(f, f.Child, child1, Password, NewPassword,
                File.ReadAllBytes(Path.Combine(f.Home, "published", winner.TargetDigest + ".emd")));
            Equal(VaultGenerationAdvanceState.Committed, f.Published.Resume(f.Recovery, winner.Manifest.Stamp, f.Root));
            byte[] journal = f.Published.ExportRestartJournal(winner.Manifest.Stamp);
            File.Delete(f.Journal(winner.Manifest.Stamp));
            Throws<CryptographicException>(() => f.Published.PruneUnreachableSiblingObjects(f.Recovery, loser.Manifest.Stamp, f.Root));
            f.Published.RestoreRestartJournal(f.Recovery, winner.Manifest.Stamp, journal);
            Equal(0, f.Published.PruneUnreachableSiblingObjects(f.Recovery, shared.Stamp, f.Root));
            Equal(1, f.Published.PruneUnreachableSiblingObjects(f.Recovery, loser.Manifest.Stamp, f.Root));
            Equal(0, f.Published.PruneUnreachableSiblingObjects(f.Recovery, loser.Manifest.Stamp, f.Root));
            Equal(true, File.Exists(loser.Manifest.Find(f.Child.Id, f.Child.Kind)!.BoundVaultPath));
            Equal(winner.TargetDigest, Digest(f.Published.ReadSelectedDocument(f.Recovery, f.Workspace, f.Root, InheritedPath)));
        });
    }

    private static VaultGenerationManifest PrepareFromSelected(Fixture f, LayerSecret from, LayerSecret to,
        string fromPassword, string toPassword, byte[]? sharedCiphertext = null)
    {
        var source = f.Recovery.ReadCommitted(f.Workspace);
        var snapshot = f.Published.OpenSelectedSnapshot(f.Recovery, f.Workspace, f.Root);
        var inventory = snapshot.BuildInventory(source, f.Root, [from.Id]);
        byte[] old = f.Published.ReadSelectedDocument(f.Recovery, f.Workspace, f.Root, InheritedPath);
        byte[] next = LayeredEmd3.Rekey(old, [f.Root, from], [f.Root, to], toPassword,
            out byte[] recovery, currentPassword: fromPassword);
        CryptographicOperations.ZeroMemory(recovery);
        var objects = new Dictionary<string, byte[]>
        {
            ["vault:" + to.Id.ToString("N")] = LayerKeyVault.Create(to, toPassword, BackupPassword),
            [InheritedArtifact] = sharedCiphertext ?? next
        };
        var commitment = new VaultRekeyCommitment(inventory, objects.ToDictionary(x => x.Key, x => Digest(x.Value)));
        Staging(f.Home).Prepare(inventory, commitment.Targets, snapshot.OpenSource, item => new MemoryStream(objects[item.Id]));
        return f.Published.PrepareCandidate(commitment, f.Root, _ => new WindowsVaultFileIdentity(20, 1, 1));
    }
}
