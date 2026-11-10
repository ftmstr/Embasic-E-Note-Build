using System.Security.Cryptography;
using Embasic.ENote.Storage;

internal static partial class Program
{
    private static void WitnessChecks()
    {
        Check("witness continuity rejects truncation, old identity and revision replay after a second provider loss", () =>
        {
            using var f = new Fixture();
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var selected = f.Enroll().Stamp;
            byte[] publicKey = key.ExportSubjectPublicKeyInfo();
            string genesis = Digest("independent witness genesis"u8.ToArray());
            var first = new VaultProviderRecoveryCheckpoint(f.ProviderId, Guid.NewGuid(), selected, 9);
            var second = new VaultProviderRecoveryCheckpoint(first.ReplacementProvider, Guid.NewGuid(), selected, 10);
            var link1 = VaultProviderWitnessChain.Sign(first, genesis, key);
            string firstTip = VaultProviderWitnessChain.Digest(link1);
            var link2 = VaultProviderWitnessChain.Sign(second, firstTip, key);
            string tip = VaultProviderWitnessChain.Digest(link2);
            var live = new CurrentWitness
            {
                Current = new VaultProviderWitnessChain(selected, f.ProviderId, genesis, [link1], firstTip, publicKey)
                    .Read(f.Workspace)
            };
            using var provider1 = NewProvider(first.ReplacementProvider);
            using var provider2 = NewProvider(second.ReplacementProvider);
            var stale = provider1.RequireActiveWitness(live);
            Recover(provider1, first);
            Equal(selected, stale.ReadTransition(f.Workspace)!.Current);
            var current = new VaultProviderWitnessChain(selected, f.ProviderId, genesis, [link1, link2], tip, publicKey);
            live.Current = current.Read(f.Workspace);
            Recover(provider2, second);
            Equal(selected, provider2.RequireActiveWitness(live).ReadTransition(f.Workspace)!.Current);
            Throws<CryptographicException>(() => stale.ReadTransition(f.Workspace));
            var intent = new VaultRetryIntent(selected, new VaultGenerationStamp(f.Workspace, selected.Epoch + 1, new string('A', 64)));
            Throws<CryptographicException>(() => stale.ReserveTransition(intent));
            Throws<CryptographicException>(() => stale.CommitTransition(intent));
            Throws<CryptographicException>(() => new VaultProviderWitnessChain(selected, f.ProviderId, genesis, [link1], tip, publicKey));
            var bad = VaultProviderWitnessChain.Sign(second with { VacantRevision = 9 }, firstTip, key);
            Throws<CryptographicException>(() => new VaultProviderWitnessChain(selected, f.ProviderId, genesis,
                [link1, bad], VaultProviderWitnessChain.Digest(bad), publicKey));
            var reused = VaultProviderWitnessChain.Sign(second with { ReplacementProvider = f.ProviderId }, firstTip, key);
            Throws<CryptographicException>(() => new VaultProviderWitnessChain(selected, f.ProviderId, genesis,
                [link1, reused], VaultProviderWitnessChain.Digest(reused), publicKey));

            VaultTransitionFilePrototype NewProvider(Guid id) =>
                new(Directory.CreateDirectory(Path.Combine(f.Home, id.ToString("N"))).FullName, ProviderKey, id);
            void Recover(VaultTransitionFilePrototype provider, VaultProviderRecoveryCheckpoint checkpoint) =>
                provider.Recover(checkpoint, VaultTransitionFilePrototype.SignRecoveryCheckpoint(checkpoint, key),
                    publicKey, live, Store(f.Home));
        });

        Check("withdrawn ERA1 approval fails closed and a consumed tombstone revision cannot be replayed", () =>
        {
            using var f = new Fixture();
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var selected = f.Enroll().Stamp;
            var root = VaultRootRecovery.Prepare(f.Enrolled!, f.Root.Id, f.RootVault, Password, f.Index, BackupPassword);
            var before = f.Provider.ObserveAuthorization(selected);
            f.Provider.CompareAndSetAuthorization(selected, before.Revision, root.Proof);
            byte[] archive = VaultRootAuthorizationArchive.Capture(f.Provider, selected);
            var bound = f.Provider.ObserveAuthorization(selected);
            f.Provider.CompareAndSetAuthorization(selected, bound.Revision, null);
            var vacant = f.Provider.ObserveAuthorization(selected);
            var approval = new VaultRootReEnrollmentApproval(selected, vacant.Revision, Digest(archive));
            var selection = new CurrentApproval();
            var approved = new VaultSignedRootApproval(approval, VaultSignedRootApproval.Sign(approval, key),
                key.ExportSubjectPublicKeyInfo(), selection);
            Equal<VaultRootReEnrollmentApproval?>(null, approved.Read(selected));
            Throws<CryptographicException>(() => VaultRootAuthorizationArchive.ReEnroll(f.Provider, approved, selected, archive));
            selection.Current = approval;
            Equal(VaultRootRecoveryUpdateState.Applied, VaultRootAuthorizationArchive.ReEnroll(f.Provider, approved, selected, archive));
            var used = f.Provider.ObserveAuthorization(selected);
            f.Provider.CompareAndSetAuthorization(selected, used.Revision, null);
            Throws<CryptographicException>(() => VaultRootAuthorizationArchive.ReEnroll(f.Provider, approved, selected, archive));
        });

        Check("rotated pin rejects stale independent selection, forged signature and prior key bytes", () =>
        {
            Guid workspace = Guid.NewGuid();
            using var authority = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var oldSigner = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var newSigner = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            byte[] oldReader = RandomNumberGenerator.GetBytes(32), newReader = RandomNumberGenerator.GetBytes(32);
            var old = VaultAuthorityKeyCustody.Describe(workspace, 1, oldSigner, oldReader, RandomNumberGenerator.GetBytes(32));
            var next = VaultAuthorityKeyCustody.Describe(workspace, 2, newSigner, newReader, RandomNumberGenerator.GetBytes(32));
            byte[] signature = VaultAuthorityKeyCustody.Approve(next, authority);
            byte[] authorityKey = authority.ExportSubjectPublicKeyInfo(), nextKey = newSigner.ExportSubjectPublicKeyInfo();
            _ = VaultAuthorityKeyCustody.AdvanceWindowsClientPin("enote29", old, next, 2, signature, authorityKey, nextKey, newReader);
            Throws<CryptographicException>(() => VaultAuthorityKeyCustody.AdvanceWindowsClientPin(
                "enote29", old, next, 1, signature, authorityKey, nextKey, newReader));
            Throws<CryptographicException>(() => VaultAuthorityKeyCustody.AdvanceWindowsClientPin(
                "enote29", old, next, 2, signature, authorityKey, nextKey, oldReader));
            Throws<CryptographicException>(() => VaultAuthorityKeyCustody.AdvanceWindowsClientPin(
                "enote29", old, next, 2, signature, authorityKey, oldSigner.ExportSubjectPublicKeyInfo(), newReader));
            signature[^1] ^= 1;
            Throws<CryptographicException>(() => VaultAuthorityKeyCustody.AdvanceWindowsClientPin(
                "enote29", old, next, 2, signature, authorityKey, nextKey, newReader));
        });
    }

    private sealed class CurrentWitness : IVaultProviderRecoveryWitness
    {
        public VaultProviderRecoveryCheckpoint? Current { get; set; }
        public VaultProviderRecoveryCheckpoint? Read(Guid workspace) => Current?.Selected.WorkspaceId == workspace ? Current : null;
    }

    private sealed class CurrentApproval : IVaultRootReEnrollmentApprovalReader
    {
        public VaultRootReEnrollmentApproval? Current { get; set; }
        public VaultRootReEnrollmentApproval? Read(VaultGenerationStamp generation) => Current?.Generation == generation ? Current : null;
    }
}
