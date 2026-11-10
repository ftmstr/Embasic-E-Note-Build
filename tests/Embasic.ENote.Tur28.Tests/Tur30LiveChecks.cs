using System.Security.Cryptography;
using Embasic.ENote.Emd;
using Embasic.ENote.Storage;

internal static partial class Program
{
    private static void Tur30LiveChecks()
    {
        Check("two consecutive root rekeys recover ELH1 and restart mixed live ciphertext without touching Markdown", () =>
        {
            using var f = new Fixture();
            using var firstRoot = LayerSecret.Regenerate(f.Root);
            using var secondRoot = LayerSecret.Regenerate(firstRoot);
            const string direct = "Vault/Direct.emd";
            string md = Path.Combine(f.Live, "independent.md");
            File.WriteAllText(md, "independent plain Markdown");
            f.Enroll();
            var first = PrepareRoot(f, f.Root, firstRoot, Password, NewPassword);
            Equal(VaultGenerationAdvanceState.Committed, f.Published.Resume(f.Recovery, first.Next.Stamp, f.Root));
            string indexObject = Path.Combine(f.Home, "published", Digest(first.Index) + ".elh");
            File.Delete(indexObject);
            Throws<InvalidDataException>(() => f.Published.RegisterSelectedSnapshot(f.Recovery, first.Next.Stamp, f.Root, firstRoot));
            f.Published.RestorePublishedObject(f.Recovery, first.Next.Stamp, "index", new MemoryStream(first.Index), f.Root);
            f.Published.RegisterSelectedSnapshot(f.Recovery, first.Next.Stamp, f.Root, firstRoot);
            Throws<CryptographicException>(() => f.Published.OpenSelectedSnapshot(f.Recovery, f.Workspace, f.Root));
            Equal(new VaultLiveSyncResult(1, 1),
                f.Published.SynchronizeSelectedLiveTree(f.Recovery, first.Next.Stamp, f.Root, firstRoot, maximumReplacements: 1));
            using var reopened = Publication(f.Home);
            Equal(new VaultLiveSyncResult(1, 0),
                reopened.SynchronizeSelectedLiveTree(f.Recovery, first.Next.Stamp, f.Root, firstRoot));
            Equal(new VaultLiveSyncResult(0, 0),
                reopened.SynchronizeSelectedLiveTree(f.Recovery, first.Next.Stamp, f.Root, firstRoot));
            Equal(Text, LayeredEmd3.Open(File.ReadAllBytes(Path.Combine(f.Live, "Direct.emd")), [firstRoot], NewPassword));
            var second = PrepareRoot(f, firstRoot, secondRoot, NewPassword, Password);
            Equal(first.Next.Stamp.Epoch + 1, second.Next.Stamp.Epoch);
            Equal(VaultGenerationAdvanceState.Committed, reopened.Resume(f.Recovery, second.Next.Stamp, firstRoot));
            reopened.RegisterSelectedSnapshot(f.Recovery, second.Next.Stamp, firstRoot, secondRoot);
            Equal(new VaultLiveSyncResult(2, 0),
                reopened.SynchronizeSelectedLiveTree(f.Recovery, second.Next.Stamp, firstRoot, secondRoot));
            Equal(Text, LayeredEmd3.Open(reopened.ReadSelectedDocument(f.Recovery, f.Workspace, secondRoot, direct),
                [secondRoot], Password));
            Equal(Text, LayeredEmd3.Open(File.ReadAllBytes(Path.Combine(f.Live, "Sub", "Inherited.emd")), [secondRoot, f.Child], Password));
            Equal("independent plain Markdown", File.ReadAllText(md));
            Equal(3, reopened.InspectReachability(f.Recovery, f.Workspace).SelectedGenerations);
        });

        Check("unknown live edit or additional encrypted path rejects the complete batch before the first write", () =>
        {
            using var f = new Fixture();
            f.Enroll();
            using var newRoot = LayerSecret.Regenerate(f.Root);
            var rekey = PrepareRoot(f, f.Root, newRoot, Password, NewPassword);
            Equal(VaultGenerationAdvanceState.Committed, f.Published.Resume(f.Recovery, rekey.Next.Stamp, f.Root));
            f.Published.RegisterSelectedSnapshot(f.Recovery, rekey.Next.Stamp, f.Root, newRoot);

            string inherited = Path.Combine(f.Live, "Sub", "Inherited.emd");
            using var portable = PortableEmd.Encrypt("third value", Password);
            byte[] unexpected = LayeredEmd3.Seal(portable, [f.Root, f.Child]);
            File.WriteAllBytes(inherited, unexpected);
            Throws<CryptographicException>(() => f.Published.SynchronizeSelectedLiveTree(f.Recovery, rekey.Next.Stamp, f.Root, newRoot));
            Equal(Digest(f.Direct), Digest(File.ReadAllBytes(Path.Combine(f.Live, "Direct.emd"))));
            Equal(Digest(unexpected), Digest(File.ReadAllBytes(inherited)));

            File.WriteAllBytes(inherited, f.Inherited);
            string extra = Path.Combine(f.Live, "New.emd");
            File.WriteAllBytes(extra, f.Direct);
            Throws<CryptographicException>(() => f.Published.SynchronizeSelectedLiveTree(f.Recovery, rekey.Next.Stamp, f.Root, newRoot));
            File.Delete(extra);
            Equal(new VaultLiveSyncResult(2, 0),
                f.Published.SynchronizeSelectedLiveTree(f.Recovery, rekey.Next.Stamp, f.Root, newRoot));
        });

        Check("full publication scan reports retained references, missing objects and unknown names without deleting", () =>
        {
            using var f = new Fixture();
            using var first = LayerSecret.Regenerate(f.Child);
            using var sibling = LayerSecret.Regenerate(f.Child);
            var winner = f.Prepare(first);
            var loser = f.Prepare(sibling);
            Equal(VaultGenerationAdvanceState.Committed, f.Published.Resume(f.Recovery, winner.Manifest.Stamp, f.Root));
            f.Published.RegisterSelectedSnapshot(f.Recovery, winner.Manifest.Stamp, f.Root, f.Root);
            string dir = Path.Combine(f.Home, "published");
            string name = loser.TargetDigest + ".emd";
            string missing = Path.Combine(dir, name);
            File.Delete(missing);
            string orphan = Path.Combine(dir, "unattributed.keep");
            File.WriteAllText(orphan, "retain");
            var scan = f.Published.InspectReachability(f.Recovery, f.Workspace);
            Equal(2, scan.SelectedGenerations);
            Equal(2, scan.RegisteredPlans);
            Equal(3, scan.VerifiedObjects.Count);
            Equal(name, scan.MissingObjects.Single());
            Equal(true, scan.UnclassifiedEntries.Contains("unattributed.keep"));
            Equal(true, File.Exists(orphan));
            File.Delete(f.Journal(winner.Manifest.Stamp));
            Throws<CryptographicException>(() => f.Published.InspectReachability(f.Recovery, f.Workspace));
            Equal(true, File.Exists(orphan));
        });
    }

    private static (VaultGenerationManifest Next, byte[] Index) PrepareRoot(Fixture f, LayerSecret prior,
        LayerSecret next, string priorPassword, string nextPassword)
    {
        var source = f.Recovery.ReadCommitted(f.Workspace);
        var snapshot = f.Published.OpenSelectedSnapshot(f.Recovery, f.Workspace, prior);
        var inventory = snapshot.BuildInventory(source, prior, [prior.Id]);
        var entries = WorkspaceLockIndex.Open(f.Index, f.Workspace, f.Root);
        byte[] index = WorkspaceLockIndex.Protect(f.Workspace, next, entries);
        var objects = new Dictionary<string, byte[]>
        {
            ["vault:" + next.Id.ToString("N")] = LayerKeyVault.Create(next, nextPassword, BackupPassword),
            ["index"] = index
        };
        foreach (var item in inventory.Artifacts.Where(x => x.Kind == VaultRekeyArtifactKind.Document))
        {
            byte[] bytes = f.Published.ReadSelectedDocument(f.Recovery, f.Workspace, prior, item.RelativePath!);
            LayerSecret[] current = item.RequiredFactors.Count == 1 ? [prior] : [prior, f.Child];
            LayerSecret[] fresh = item.RequiredFactors.Count == 1 ? [next] : [next, f.Child];
            objects[item.Id] = LayeredEmd3.Rekey(bytes, current, fresh, nextPassword, out byte[] recovery, currentPassword: priorPassword);
            CryptographicOperations.ZeroMemory(recovery);
        }
        var commitment = new VaultRekeyCommitment(inventory, objects.ToDictionary(x => x.Key, x => Digest(x.Value)));
        Staging(f.Home).Prepare(inventory, commitment.Targets, snapshot.OpenSource, item => new MemoryStream(objects[item.Id]));
        var candidate = f.Published.PrepareCandidate(commitment, prior,
            _ => new WindowsVaultFileIdentity(27, (ulong)source.Stamp.Epoch + 1, 1));
        return (candidate, index);
    }
}
