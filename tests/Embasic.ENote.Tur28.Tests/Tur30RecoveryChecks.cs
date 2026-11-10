using System.Security.Cryptography;
using Embasic.ENote.Storage;

internal static partial class Program
{
    private static void Tur30RecoveryChecks()
    {
        Check("signed recovery policy survives restart and requires a current external pin for witness and ERA1", () =>
        {
            using var f = new Fixture();
            using var signing = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            byte[] pub = signing.ExportSubjectPublicKeyInfo();
            var selected = f.Enroll().Stamp;
            var checkpoint = new VaultProviderRecoveryCheckpoint(f.ProviderId, Guid.NewGuid(), selected, 10);
            string genesis = Digest("TUR30 witness genesis"u8.ToArray());
            var link = VaultProviderWitnessChain.Sign(checkpoint, genesis, signing);
            var chain = new VaultProviderWitnessChain(selected, f.ProviderId, genesis, [link], VaultProviderWitnessChain.Digest(link), pub);
            string directory = Directory.CreateDirectory(Path.Combine(f.Home, "independent-policy")).FullName;
            var store = new VaultRecoveryPolicyDiskStore(directory, pub);
            var approval = new VaultRootReEnrollmentApproval(selected, 10, Digest("ERA1 fixture digest"u8.ToArray()));
            var record = new VaultRecoveryPolicyRecord(f.Workspace, 1, chain.TipSha256, approval);
            VaultRecoveryPolicyPin? pin = store.Stage(record, VaultRecoveryPolicyDiskStore.Sign(record, signing));
            var retainedPin = pin;
            var reopened = new VaultRecoveryPolicyDiskStore(directory, pub);
            var reader = reopened.Select(_ => pin, chain);
            Equal(checkpoint, reader.Read(f.Workspace));
            Equal(approval, reader.Read(selected));
            Directory.Delete(Path.Combine(f.Home, "authority"), recursive: true);
            Equal(approval, reader.Read(selected));
            Equal(checkpoint, reader.Read(f.Workspace));

            var withdrawn = record with { Revision = 2, RootApproval = null, WitnessTipSha256 = null };
            pin = reopened.Stage(withdrawn, VaultRecoveryPolicyDiskStore.Sign(withdrawn, signing));
            Equal<VaultProviderRecoveryCheckpoint?>(null, reader.Read(f.Workspace));
            Equal<VaultRootReEnrollmentApproval?>(null, reader.Read(selected));
            Equal(record, reopened.Read(retainedPin));
            int reads = 0;
            var changed = reopened.Select(_ => ++reads == 1 ? retainedPin : pin, chain);
            Throws<VaultGenerationOutcomeUnknownException>(() => changed.Read(f.Workspace));
            foreach (var file in Directory.EnumerateFiles(directory, "*-2-*.erp")) File.Delete(file);
            Equal<VaultRootReEnrollmentApproval?>(null, reader.Read(selected));
        });

        Check("pin mismatch, modified policy, wrong signer and overlong policy refuse activation", () =>
        {
            using var f = new Fixture();
            using var signing = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            string directory = Directory.CreateDirectory(Path.Combine(f.Home, "policy")).FullName;
            var store = new VaultRecoveryPolicyDiskStore(directory, signing.ExportSubjectPublicKeyInfo());
            var record = new VaultRecoveryPolicyRecord(f.Workspace, 1, null, null);
            Throws<CryptographicException>(() => store.Stage(record, VaultRecoveryPolicyDiskStore.Sign(record, attacker)));
            var pin = store.Stage(record, VaultRecoveryPolicyDiskStore.Sign(record, signing));
            Equal<VaultRecoveryPolicyRecord?>(null, store.Read(pin with { Sha256 = new string('B', 64) }));
            var path = Directory.EnumerateFiles(directory).Single();
            byte[] data = File.ReadAllBytes(path);
            data[^1] ^= 1;
            File.WriteAllBytes(path, data);
            Throws<CryptographicException>(() => store.Read(pin));
            File.WriteAllBytes(path, new byte[257]);
            Throws<InvalidDataException>(() => store.Read(pin));
        });

        Check("old direct provider can mutate its own copy but the witnessed root and transition paths reject it", () =>
        {
            using var f = new Fixture();
            var selected = f.Enroll().Stamp;
            var checkpoint = new VaultProviderRecoveryCheckpoint(f.ProviderId, Guid.NewGuid(), selected, 10);
            var witness = new CurrentWitness { Current = checkpoint };
            var staleRoot = f.Provider.RequireActiveRootWitness(witness);
            var staleTransition = f.Provider.RequireActiveWitness(witness);
            var prior = f.Provider.ObserveAuthorization(selected);
            Equal(prior.Revision + 1, f.Provider.CompareAndSetAuthorization(selected, prior.Revision, null).Revision);
            Throws<CryptographicException>(() => staleRoot.ObserveAuthorization(selected));
            Throws<CryptographicException>(() => staleRoot.CompareAndSetAuthorization(selected, prior.Revision + 1, null));
            Throws<CryptographicException>(() => staleTransition.ReadTransition(f.Workspace));

            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var replacement = new VaultTransitionFilePrototype(
                Directory.CreateDirectory(Path.Combine(f.Home, "replacement")).FullName, ProviderKey, checkpoint.ReplacementProvider);
            replacement.Recover(checkpoint, VaultTransitionFilePrototype.SignRecoveryCheckpoint(checkpoint, key),
                key.ExportSubjectPublicKeyInfo(), witness, Store(f.Home));
            var uncertain = replacement.RequireActiveRootWitness(new Witness(checkpoint, changeOnRecheck: true));
            Throws<VaultGenerationOutcomeUnknownException>(() => uncertain.CompareAndSetAuthorization(selected, 10, null));
            Equal(11L, replacement.ObserveAuthorization(selected).Revision);
        });
    }
}
