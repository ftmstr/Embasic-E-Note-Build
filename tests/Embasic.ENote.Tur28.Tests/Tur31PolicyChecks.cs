using System.Security.Cryptography;
using Embasic.ENote.Storage;

internal static partial class Program
{
    private static void Tur31PolicyChecks()
    {
        Check("durable selector verifies continuation and refuses loss, stale writes and competing selections", () =>
        {
            using var f = new Fixture();
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            string dir = Directory.CreateDirectory(Path.Combine(f.Home, "selector")).FullName;
            byte[] pub = key.ExportSubjectPublicKeyInfo();
            var journal = new VaultRecoveryPinJournal(dir, f.Workspace, pub);
            var first = new VaultRecoveryPolicyPin(f.Workspace, 1, new string('A', 64));
            var next = first with { Revision = 2, Sha256 = new string('B', 64) };
            var rival = next with { Sha256 = new string('C', 64) };
            var firstSig = VaultRecoveryPinJournal.Sign(null, first, key);
            journal.Append(null, first, firstSig);
            journal.Append(first, next, VaultRecoveryPinJournal.Sign(first, next, key));
            journal.Append(first, next, VaultRecoveryPinJournal.Sign(first, next, key));
            var restarted = new VaultRecoveryPinJournal(dir, f.Workspace, pub);
            Equal(next, restarted.ReadRequired(first));
            Equal(next, restarted.ReadRequired(next));
            Throws<CryptographicException>(() => restarted.Append(null, first, firstSig));
            Throws<CryptographicException>(() => restarted.Append(first, rival, VaultRecoveryPinJournal.Sign(first, rival, key)));
            Throws<CryptographicException>(() => restarted.ReadRequired(null));

            var tip = Directory.EnumerateFiles(dir, "*.eps").Order(StringComparer.Ordinal).Last();
            byte[] saved = File.ReadAllBytes(tip);
            File.Delete(tip);
            Throws<CryptographicException>(() => restarted.ReadRequired(next));
            // A replayed floor plus a replayed journal is intentionally observable as the older selection.
            Equal(first, restarted.ReadRequired(first));
            byte[] tampered = (byte[])saved.Clone();
            tampered[^1] ^= 1;
            File.WriteAllBytes(tip, tampered);
            Throws<CryptographicException>(() => restarted.ReadRequired(first));
            File.WriteAllBytes(tip, saved);
            Equal(next, restarted.ReadRequired(next));
        });

        Check("one ERP1-bound authority rejects the raw old provider and observes durable witness withdrawal", () =>
        {
            using var f = new Fixture();
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var selected = f.Enroll().Stamp;
            string genesis = Digest("TUR31 witness genesis"u8.ToArray());
            var checkpoint = new VaultProviderRecoveryCheckpoint(f.ProviderId, Guid.NewGuid(), selected, 11);
            var link = VaultProviderWitnessChain.Sign(checkpoint, genesis, key);
            byte[] pub = key.ExportSubjectPublicKeyInfo();
            var chain = new VaultProviderWitnessChain(selected, f.ProviderId, genesis, [link], VaultProviderWitnessChain.Digest(link), pub);
            var policy = new VaultRecoveryPolicyDiskStore(Directory.CreateDirectory(Path.Combine(f.Home, "policy")).FullName, pub);
            var selector = new VaultRecoveryPinJournal(Directory.CreateDirectory(Path.Combine(f.Home, "selector")).FullName,
                f.Workspace, pub);
            var policyRecord = new VaultRecoveryPolicyRecord(f.Workspace, 1, chain.TipSha256, null);
            var first = policy.Stage(policyRecord, VaultRecoveryPolicyDiskStore.Sign(policyRecord, key));
            selector.Append(null, first, VaultRecoveryPinJournal.Sign(null, first, key));
            VaultRecoveryPolicyPin? minimum = first;
            var reader = policy.Select(_ => selector.ReadRequired(minimum), chain);

            var old = reader.BindProvider(f.Provider);
            Throws<CryptographicException>(() => old.ReadTransition(f.Workspace));
            Throws<CryptographicException>(() => old.ObserveAuthorization(selected));
            Equal(selected, f.Provider.ReadTransition(f.Workspace).Current);
            using var active = new VaultTransitionFilePrototype(
                Directory.CreateDirectory(Path.Combine(f.Home, "new-authority")).FullName, ProviderKey, checkpoint.ReplacementProvider);
            active.Recover(checkpoint, VaultTransitionFilePrototype.SignRecoveryCheckpoint(checkpoint, key), pub, reader, Store(f.Home));
            var guarded = reader.BindProvider(active);
            Equal(selected, new VaultGenerationRecovery(guarded, Store(f.Home)).ReadCommitted(f.Workspace).Stamp);
            Equal(11L, guarded.ObserveAuthorization(selected).Revision);
            var withdrawn = policyRecord with { Revision = 2, WitnessTipSha256 = null };
            var second = policy.Stage(withdrawn, VaultRecoveryPolicyDiskStore.Sign(withdrawn, key));
            selector.Append(first, second, VaultRecoveryPinJournal.Sign(first, second, key));
            Throws<CryptographicException>(() => guarded.ReadTransition(f.Workspace));
            Throws<CryptographicException>(() => guarded.CompareAndSetAuthorization(selected, 11, null));
            minimum = null;
            Throws<CryptographicException>(() => guarded.ObserveAuthorization(selected));
        });
    }
}
