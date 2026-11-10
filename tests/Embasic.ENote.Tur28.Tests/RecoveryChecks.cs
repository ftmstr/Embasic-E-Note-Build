using System.Security.Cryptography;
using Embasic.ENote.Storage;

internal static partial class Program
{
    private static void RecoveryChecks()
    {
        Check("independent signed checkpoint restores a fresh provider and ERA1 needs a separate approval", () =>
        {
            using var f = new Fixture();
            var selected = f.Enroll().Stamp;
            var rootBackup = VaultRootRecovery.Prepare(f.Enrolled!, f.Root.Id, f.RootVault, Password, f.Index, BackupPassword);
            var existing = f.Provider.ObserveAuthorization(selected);
            f.Provider.CompareAndSetAuthorization(selected, existing.Revision, rootBackup.Proof);
            byte[] archive = VaultRootAuthorizationArchive.Capture(f.Provider, selected);
            Directory.Delete(Path.Combine(f.Home, "authority"), recursive: true);
            using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            byte[] publicKey = signer.ExportSubjectPublicKeyInfo();
            var checkpoint = new VaultProviderRecoveryCheckpoint(f.ProviderId, Guid.NewGuid(), selected, 11);
            string replacement = Directory.CreateDirectory(Path.Combine(f.Home, "replacement")).FullName;
            using var provider = new VaultTransitionFilePrototype(replacement, RandomNumberGenerator.GetBytes(32),
                checkpoint.ReplacementProvider);
            byte[] signature = VaultTransitionFilePrototype.SignRecoveryCheckpoint(checkpoint, signer);
            provider.Recover(checkpoint, signature, publicKey, new Witness(checkpoint), Store(f.Home));
            Equal(selected, new VaultGenerationRecovery(provider, Store(f.Home)).ReadCommitted(f.Workspace).Stamp);
            Equal(new VaultRootRecoveryAuthorization(11, null), provider.ObserveAuthorization(selected));
            var approval = new VaultRootReEnrollmentApproval(selected, 11, Digest(archive));
            Throws<CryptographicException>(() => new VaultSignedRootApproval(approval, signature, publicKey));
            var reader = new VaultSignedRootApproval(approval, VaultSignedRootApproval.Sign(approval, signer), publicKey);
            Equal(VaultRootRecoveryUpdateState.Applied, VaultRootAuthorizationArchive.ReEnroll(provider, reader, selected, archive));
            Equal(rootBackup.Proof, provider.ObserveAuthorization(selected).Proof);
            Throws<IOException>(() => provider.Recover(checkpoint, signature, publicKey, new Witness(checkpoint), Store(f.Home)));
        });

        Check("stale witness, changed checkpoint, wrong authority and missing manifest fail closed", () =>
        {
            using var f = new Fixture();
            var selected = f.Enroll().Stamp;
            using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            byte[] publicKey = signer.ExportSubjectPublicKeyInfo();
            var checkpoint = new VaultProviderRecoveryCheckpoint(f.ProviderId, Guid.NewGuid(), selected, 7);
            byte[] signature = VaultTransitionFilePrototype.SignRecoveryCheckpoint(checkpoint, signer);
            string folder = Directory.CreateDirectory(Path.Combine(f.Home, "replacement")).FullName;
            using var provider = new VaultTransitionFilePrototype(folder, ProviderKey, checkpoint.ReplacementProvider);
            Throws<CryptographicException>(() => provider.Recover(checkpoint, signature, publicKey,
                new Witness(checkpoint with { VacantRevision = 8 }), Store(f.Home)));
            Throws<CryptographicException>(() => provider.Recover(checkpoint, signature, publicKey,
                new Witness(checkpoint, changeOnRecheck: true), Store(f.Home)));
            Throws<CryptographicException>(() => provider.Recover(checkpoint, signature, other.ExportSubjectPublicKeyInfo(),
                new Witness(checkpoint), Store(f.Home)));
            Throws<CryptographicException>(() => provider.Recover(checkpoint, signature, publicKey,
                new Witness(checkpoint), new VaultGenerationDiskStore(Directory.CreateDirectory(Path.Combine(f.Home, "empty")).FullName)));
            Equal(0, Directory.EnumerateFileSystemEntries(folder).Count());
        });
    }

    private static void KeyChecks()
    {
        Check("signed key epoch admits exact signing and role keys only", () =>
        {
            Guid workspace = Guid.NewGuid();
            using var authority = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var service = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            byte[] reader = RandomNumberGenerator.GetBytes(32), publisher = RandomNumberGenerator.GetBytes(32);
            var keys = VaultAuthorityKeyCustody.Describe(workspace, 2, service, reader, publisher);
            byte[] permit = VaultAuthorityKeyCustody.Approve(keys, authority);
            byte[] publicKey = authority.ExportSubjectPublicKeyInfo(), privateKey = service.ExportPkcs8PrivateKey();
            using var custody = VaultAuthorityKeyCustody.Open(keys, workspace, 2, permit, publicKey, privateKey, reader, publisher);
            Equal(keys, custody.Epoch);
            Throws<CryptographicException>(() => VaultAuthorityKeyCustody.Open(keys, workspace, 3, permit, publicKey, privateKey, reader, publisher));
            Throws<CryptographicException>(() => VaultAuthorityKeyCustody.Open(keys, workspace, 2, permit, publicKey, privateKey, publisher, reader));
            Throws<CryptographicException>(() => VaultAuthorityKeyCustody.Open(keys with { Epoch = 3 }, workspace, 3, permit,
                publicKey, privateKey, reader, publisher));
        });

        Check("rotation requires next epoch and retires signing and both role pins without role swaps", () =>
        {
            Guid workspace = Guid.NewGuid();
            using var first = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var second = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            byte[] reader = RandomNumberGenerator.GetBytes(32), publisher = RandomNumberGenerator.GetBytes(32);
            var old = VaultAuthorityKeyCustody.Describe(workspace, 1, first, reader, publisher);
            var next = VaultAuthorityKeyCustody.Describe(workspace, 2, second,
                RandomNumberGenerator.GetBytes(32), RandomNumberGenerator.GetBytes(32));
            VaultAuthorityKeyCustody.VerifySuccessor(old, next);
            Throws<CryptographicException>(() => VaultAuthorityKeyCustody.VerifySuccessor(old, next with { Epoch = 3 }));
            Throws<CryptographicException>(() => VaultAuthorityKeyCustody.VerifySuccessor(old, next with { SigningSha256 = old.SigningSha256 }));
            Throws<CryptographicException>(() => VaultAuthorityKeyCustody.VerifySuccessor(old, next with { ReaderSha256 = old.PublisherSha256 }));
        });
    }

    private sealed class Witness(VaultProviderRecoveryCheckpoint checkpoint, bool changeOnRecheck = false)
        : IVaultProviderRecoveryWitness
    {
        private int _reads;
        public VaultProviderRecoveryCheckpoint? Read(Guid workspace) =>
            workspace == checkpoint.Selected.WorkspaceId && (++_reads == 1 || !changeOnRecheck) ? checkpoint : null;
    }
}
