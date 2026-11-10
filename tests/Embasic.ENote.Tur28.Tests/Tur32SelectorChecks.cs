using System.Security.Cryptography;
using Embasic.ENote.Storage;

internal static partial class Program
{
    private static void Tur32SelectorChecks()
    {
        Check("selector rotation keeps old history and refuses old signatures at the new boundary", () =>
        {
            using var f = new Fixture();
            using var old = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var fresh = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            byte[] initial = old.ExportSubjectPublicKeyInfo();
            string path = Directory.CreateDirectory(Path.Combine(f.Home, "selector")).FullName;
            var first = new VaultRecoveryPolicyPin(f.Workspace, 1, new string('A', 64));
            var second = first with { Revision = 2, Sha256 = new string('B', 64) };
            var third = first with { Revision = 3, Sha256 = new string('C', 64) };
            var rotation = VaultRecoveryPinJournal.AuthorizeRotation(f.Workspace, 2, old, fresh);
            var journal = new VaultRecoveryPinJournal(path, f.Workspace, initial, rotation);
            journal.Append(null, first, VaultRecoveryPinJournal.Sign(null, first, old));
            Throws<CryptographicException>(() => journal.Append(first, second, VaultRecoveryPinJournal.Sign(first, second, old)));
            journal.Append(first, second, VaultRecoveryPinJournal.Sign(first, second, fresh));
            journal.Append(second, third, VaultRecoveryPinJournal.Sign(second, third, fresh));
            Equal(third, new VaultRecoveryPinJournal(path, f.Workspace, initial, rotation).ReadRequired(first));
            Throws<CryptographicException>(() => new VaultRecoveryPinJournal(path, f.Workspace, initial).ReadRequired(first));
            Throws<CryptographicException>(() => new VaultRecoveryPinJournal(path, Guid.NewGuid(), initial, rotation));
            Throws<CryptographicException>(() => new VaultRecoveryPinJournal(path, f.Workspace, initial,
                rotation with { FirstRevision = 3 }));
            var reused = VaultRecoveryPinJournal.AuthorizeRotation(f.Workspace, 3, fresh, old);
            Throws<CryptographicException>(() => new VaultRecoveryPinJournal(path, f.Workspace, initial, rotation, reused));
        });

        Check("signed separate operator floor rejects a rolled-back selector and exposes only the managed bound provider", () =>
        {
            using var f = new Fixture();
            using var issuer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var selectorKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var selected = f.Enroll().Stamp;
            var checkpoint = new VaultProviderRecoveryCheckpoint(f.ProviderId, Guid.NewGuid(), selected, 11);
            string genesis = Digest("TUR32 operator witness"u8.ToArray());
            byte[] issuerKey = issuer.ExportSubjectPublicKeyInfo();
            var link = VaultProviderWitnessChain.Sign(checkpoint, genesis, issuer);
            var chain = new VaultProviderWitnessChain(selected, f.ProviderId, genesis, [link], VaultProviderWitnessChain.Digest(link), issuerKey);
            var policy = new VaultRecoveryPolicyDiskStore(Directory.CreateDirectory(Path.Combine(f.Home, "policy")).FullName, issuerKey);
            string selectorPath = Directory.CreateDirectory(Path.Combine(f.Home, "selector")).FullName;
            var journal = new VaultRecoveryPinJournal(selectorPath, f.Workspace, selectorKey.ExportSubjectPublicKeyInfo());
            var enabled = new VaultRecoveryPolicyRecord(f.Workspace, 1, chain.TipSha256, null);
            var revoked = enabled with { Revision = 2, WitnessTipSha256 = null };
            var first = policy.Stage(enabled, VaultRecoveryPolicyDiskStore.Sign(enabled, issuer));
            var second = policy.Stage(revoked, VaultRecoveryPolicyDiskStore.Sign(revoked, issuer));
            journal.Append(null, first, VaultRecoveryPinJournal.Sign(null, first, selectorKey));
            string floor = Path.Combine(Directory.CreateDirectory(Path.Combine(f.Home, "management")).FullName, "minimum.epf");
            File.WriteAllBytes(floor, VaultRecoveryPinFloor.Issue(first, issuer));
            var reader = policy.Select(ws => journal.ReadRequired(VaultRecoveryPinFloor.Read(floor, ws, issuerKey)), chain);
            string activePath = Directory.CreateDirectory(Path.Combine(f.Home, "managed")).FullName;
            using (var bootstrap = new VaultTransitionFilePrototype(activePath, ProviderKey, checkpoint.ReplacementProvider))
                bootstrap.Recover(checkpoint, VaultTransitionFilePrototype.SignRecoveryCheckpoint(checkpoint, issuer),
                    issuerKey, reader, Store(f.Home));
            using var managed = reader.OpenManagedProvider(activePath, ProviderKey, checkpoint.ReplacementProvider);
            using var old = reader.OpenManagedProvider(Path.Combine(f.Home, "authority"), ProviderKey, f.ProviderId);
            Equal(selected, new VaultGenerationRecovery(managed.Authority, Store(f.Home)).ReadCommitted(f.Workspace).Stamp);
            Throws<CryptographicException>(() => old.Authority.ReadTransition(f.Workspace));
            Equal(selected, f.Provider.ReadTransition(f.Workspace).Current);
            journal.Append(first, second, VaultRecoveryPinJournal.Sign(first, second, selectorKey));
            byte[] strongFloor = VaultRecoveryPinFloor.Issue(second, issuer);
            File.WriteAllBytes(floor, strongFloor);
            File.Delete(Directory.EnumerateFiles(selectorPath, "*.eps").Order(StringComparer.Ordinal).Last());
            Throws<CryptographicException>(() => managed.Authority.ObserveAuthorization(selected));
            File.WriteAllBytes(floor, VaultRecoveryPinFloor.Issue(first, selectorKey));
            Throws<CryptographicException>(() => managed.Authority.ObserveAuthorization(selected));
            File.Delete(floor);
            Throws<IOException>(() => managed.Authority.ObserveAuthorization(selected));
        });
    }
}
