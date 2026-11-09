using System.Security.Cryptography;
using Embasic.ENote.Storage;

internal static partial class Program
{
    private static void RekeyChecks(Fixture f)
    {
        Check("v1 and v2 manifests round-trip without losing the committed catalog or rekey digest", () =>
        {
            var data = f.Data(f.PlainInitial);
            data.Stage(f.Initial);
            data.Stage(f.Next);
            Equal<VaultGenerationBindings?>(null, data.Read(f.PlainInitial.Stamp)?.Bindings);
            Equal(f.Initial.Bindings, data.Read(f.Initial.Stamp)?.Bindings);
            Equal(f.Commitment.Destination, data.Read(f.Next.Stamp)?.Bindings);
            Equal(false, f.Initial.Stamp == f.PlainInitial.Stamp);
        });
        Check("catalog order is canonical and the root rekey includes inherited documents", () =>
        {
            var reordered = f.Catalog.Reverse().ToArray();
            Equal(f.CatalogDigest(f.Catalog), f.CatalogDigest(reordered));
            var plan = VaultRekeyInventory.Create(f.Initial, f.Index, f.Root, [f.Root.Id], reordered);
            Equal(true, plan.Artifacts.Any(x => x.RelativePath == f.Catalog[0].RelativePath &&
                x.RequiredFactors.SequenceEqual(new[] { f.Root.Id, f.Child.Id })));
            var reorderedCommit = new VaultRekeyCommitment(plan,
                f.Commitment.Targets.Reverse().ToDictionary(x => x.Key, x => x.Value));
            Equal(f.Commitment.Destination, reorderedCommit.Destination);
        });
        Check("catalog anchor detects an omitted inherited document and changed unaffected source metadata", () =>
        {
            Throws<CryptographicException>(() => VaultRekeyInventory.Create(f.Initial, f.Index, f.Root,
                [f.Root.Id], f.Catalog.Skip(1)));
            var changed = (VaultRekeyDocument[])f.Catalog.Clone();
            changed[1] = changed[1] with { SourceSha256 = new string('A', 64) };
            Throws<CryptographicException>(() => VaultRekeyInventory.Create(f.Initial, f.Index, f.Root,
                [f.Child.Id], changed));
            byte[] alternateIndex = Embasic.ENote.Emd.WorkspaceLockIndex.RekeyRoot(f.Index,
                f.Workspace, f.Root, f.Fresh);
            Throws<CryptographicException>(() => VaultRekeyInventory.Create(f.Initial, alternateIndex, f.Fresh,
                [f.Root.Id], f.Catalog));
        });
        Check("an enrolled catalog with a missing factor is refused", () =>
        {
            var incomplete = f.Manifest(1, f.Initial.Bindings, omitChild: true);
            Throws<CryptographicException>(() => VaultRekeyInventory.Create(incomplete, f.Index, f.Root,
                [f.Root.Id], f.Catalog));
        });
        Check("RISK incomplete inherited documents cannot be detected when omitted before authority enrollment", () =>
        {
            var omitted = f.Catalog.Skip(1).ToArray();
            var incomplete = f.Manifest(1, new VaultGenerationBindings(f.CatalogDigest(omitted)));
            var plan = VaultRekeyInventory.Create(incomplete, f.Index, f.Root, [f.Root.Id], omitted);
            Equal(false, plan.Artifacts.Any(x => x.RelativePath == f.Catalog[0].RelativePath));
        });
        Check("candidate must retain the committed target map, changed vault pin and unaffected file grants", () =>
        {
            using var provider = f.Provider();
            var data = f.Data();
            var coordinator = new VaultGenerationRecovery(provider, data);
            var staging = new VaultRekeyStaging(f.Folder());
            var rivalMap = new Dictionary<string, string>(f.Commitment.Targets) { ["index"] = new string('B', 64) };
            var rival = new VaultRekeyCommitment(f.Inventory, rivalMap);
            Throws<CryptographicException>(() => coordinator.StageAndAdvanceRekey(rival, f.Next, staging, f.Source));
            var wrongPin = f.Manifest(2, f.Commitment.Destination);
            Throws<CryptographicException>(() =>
                coordinator.StageAndAdvanceRekey(f.Commitment, wrongPin, staging, f.Source));
            var redirected = f.Manifest(2, f.Commitment.Destination, fresh: true, redirectChild: true);
            Throws<CryptographicException>(() =>
                coordinator.StageAndAdvanceRekey(f.Commitment, redirected, staging, f.Source));
            Equal<VaultRetryIntent?>(null, provider.ReadTransition(f.Workspace).Pending);
            Equal<VaultGenerationManifest?>(null, data.Read(f.Next.Stamp));
        });
        Check("rekey refuses a plan from a source without an enrolled catalog", () =>
        {
            string folder = f.Folder();
            using var provider = f.Provider(folder, provision: false);
            provider.Provision(f.PlainInitial.Stamp);
            var inventory = VaultRekeyInventory.Create(f.PlainInitial, f.Index, f.Root, [f.Root.Id], f.Catalog);
            var commitment = new VaultRekeyCommitment(inventory, f.Commitment.Targets);
            var destination = f.Manifest(2, commitment.Destination, fresh: true);
            var coordinator = new VaultGenerationRecovery(provider, f.Data(f.PlainInitial));
            Throws<CryptographicException>(() =>
                coordinator.StageAndAdvanceRekey(commitment, destination, new VaultRekeyStaging(f.Folder()), f.Source));
        });
        Check("generic stage, resume and restage cannot bypass bound rekey proof", () =>
        {
            using var provider = f.Provider();
            var data = f.Data();
            var coordinator = new VaultGenerationRecovery(provider, data);
            Throws<CryptographicException>(() => coordinator.StageAndAdvance(f.Initial.Stamp, f.Next));
            Throws<CryptographicException>(() => coordinator.StageAndAdvance(f.Initial.Stamp, f.PlainNext));
            provider.ReserveTransition(f.Intent);
            Throws<CryptographicException>(() => coordinator.RestageReservedCandidate(f.Initial.Stamp, f.Next));
            data.Stage(f.Next);
            Throws<CryptographicException>(() => coordinator.Resume(f.Initial.Stamp));
            Equal(f.Intent, provider.ReadTransition(f.Workspace).Pending);
        });
        Check("unprepared bound rekey cannot reserve or publish a candidate", () =>
        {
            using var provider = f.Provider();
            var data = f.Data();
            var coordinator = new VaultGenerationRecovery(provider, data);
            var staging = new VaultRekeyStaging(f.Folder());
            Throws<InvalidDataException>(() =>
                coordinator.StageAndAdvanceRekey(f.Commitment, f.Next, staging, f.Source));
            Equal<VaultRetryIntent?>(null, provider.ReadTransition(f.Workspace).Pending);
            Equal<VaultGenerationManifest?>(null, data.Read(f.Next.Stamp));
        });
        Check("loss after reservation fails closed and exact prepared rekey can resume", () =>
        {
            using var provider = f.Provider();
            var data = f.Data();
            string directory = f.Folder();
            var staging = new VaultRekeyStaging(directory);
            staging.Prepare(f.Inventory, f.Commitment.Targets, f.Source, f.Target);
            string indexStage = Path.Combine(directory, f.Commitment.Targets["index"] + ".rekey");
            var fault = new TransitionFault(provider, onReserve: () => File.Delete(indexStage));
            var coordinator = new VaultGenerationRecovery(fault, data);
            Throws<InvalidDataException>(() =>
                coordinator.StageAndAdvanceRekey(f.Commitment, f.Next, staging, f.Source));
            Equal(f.Intent, provider.ReadTransition(f.Workspace).Pending);
            Equal(0, fault.Commits);
            staging.Prepare(f.Inventory, f.Commitment.Targets, f.Source, f.Target);
            Equal(VaultGenerationAdvanceState.Committed,
                coordinator.StageAndAdvanceRekey(f.Commitment, f.Next, staging, f.Source));
        });
        Check("lost rekey acknowledgement and restart resolve exact receipt with the prepared set", () =>
        {
            string authority = f.Folder(), directory = f.Folder();
            var data = f.Data();
            var staging = new VaultRekeyStaging(directory);
            staging.Prepare(f.Inventory, f.Commitment.Targets, f.Source, f.Target);
            using (var provider = f.Provider(authority))
            {
                var fault = new TransitionFault(provider, lostCommitReply: true);
                Equal(VaultGenerationAdvanceState.Committed, new VaultGenerationRecovery(fault, data)
                    .StageAndAdvanceRekey(f.Commitment, f.Next, staging, f.Source));
            }
            using var reopened = f.Provider(authority, provision: false);
            var coordinator = new VaultGenerationRecovery(reopened, data);
            Equal(VaultGenerationAdvanceState.Committed,
                coordinator.StageAndAdvanceRekey(f.Commitment, f.Next, staging, f.Source));
            Throws<CryptographicException>(() => coordinator.Resume(f.Initial.Stamp));
            File.Delete(Path.Combine(directory, f.Commitment.Targets["index"] + ".rekey"));
            Throws<InvalidDataException>(() =>
                coordinator.StageAndAdvanceRekey(f.Commitment, f.Next, staging, f.Source));
            Equal(f.Next.Stamp, reopened.ReadTransition(f.Workspace).Current);
        });
        Check("RISK a caller with direct provider mutation can bypass coordinator preparation", () =>
        {
            using var provider = f.Provider();
            var data = f.Data();
            data.Stage(f.Next);
            provider.ReserveTransition(f.Intent);
            provider.CommitTransition(f.Intent);
            Equal(f.Next.Stamp, new VaultGenerationRecovery(provider, data).ReadCommitted(f.Workspace).Stamp);
        });
    }

    private sealed class TransitionFault(VaultTransitionFilePrototype provider, Action? onReserve = null,
        bool lostCommitReply = false) : IExternalVaultTransitionAnchor
    {
        public int Commits { get; private set; }
        public VaultTransitionState ReadTransition(Guid workspaceId) => provider.ReadTransition(workspaceId);
        public void ReserveTransition(VaultRetryIntent intent)
        {
            provider.ReserveTransition(intent);
            onReserve?.Invoke();
        }
        public void CommitTransition(VaultRetryIntent intent)
        {
            Commits++;
            provider.CommitTransition(intent);
            if (lostCommitReply) throw new VaultGenerationOutcomeUnknownException("Injected lost rekey commit reply.");
        }
    }
}
