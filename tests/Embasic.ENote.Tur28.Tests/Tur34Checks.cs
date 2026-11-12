using System.Runtime.Versioning;
using System.Security.Cryptography;
using Embasic.ENote.Emd;
using Embasic.ENote.Storage;

internal static partial class Program
{
    private static int Tur34OperatorChecks()
    {
        Check("fresh remote proof requires the exact selected floor and its rotated operator", () =>
        {
            using var first = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var current = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var prior = new VaultRecoveryPolicyPin(Guid.NewGuid(), 1, new string('A', 64));
            var selected = prior with { Revision = 2, Sha256 = new string('B', 64) };
            byte[] anchor = first.ExportSubjectPublicKeyInfo();
            byte[] challenge = RandomNumberGenerator.GetBytes(32);
            var transition = VaultRecoveryPinFloor.AuthorizeRotation(prior.Workspace, 2, first, current);
            byte[] fresh = VaultRecoveryPinFloor.Attest(selected, challenge, current);
            VaultRecoveryPinFloor.VerifyRequiredAttestation(selected, selected, challenge, fresh, anchor, transition);
            Throws<CryptographicException>(() => VaultRecoveryPinFloor.VerifyRequiredAttestation(selected,
                selected with { Sha256 = prior.Sha256 }, challenge, fresh, anchor, transition));
            byte[] old = VaultRecoveryPinFloor.Attest(prior, challenge, first);
            Throws<CryptographicException>(() => VaultRecoveryPinFloor.VerifyRequiredAttestation(selected, prior, challenge, old, anchor, transition));
            VaultRecoveryPinFloor.VerifyRequiredAttestation(prior, prior, challenge, old, anchor);
            challenge[0] ^= 1;
            Throws<CryptographicException>(() => VaultRecoveryPinFloor.VerifyRequiredAttestation(selected, selected, challenge, fresh, anchor, transition));
        });

        Check("operator key loss preserves offline evidence but cannot be replaced unilaterally", () =>
        {
            using var backup = new TempBackup();
            using var prior = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var unapproved = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var pin = new VaultRecoveryPolicyPin(Guid.NewGuid(), 2, new string('C', 64));
            string path = Path.Combine(backup.Path, "floor.epf");
            byte[] anchor = prior.ExportSubjectPublicKeyInfo();
            VaultRecoveryOperatorKeyTransition transition;
            using (var lost = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            {
                transition = VaultRecoveryPinFloor.AuthorizeRotation(pin.Workspace, pin.Revision, prior, lost);
                File.WriteAllBytes(path, VaultRecoveryPinFloor.Issue(pin, lost));
            }
            Equal(pin, VaultRecoveryPinFloor.ReadRequired(path, pin.Workspace, anchor, pin.Revision, transition));
            byte[] challenge = RandomNumberGenerator.GetBytes(32);
            byte[] replacement = VaultRecoveryPinFloor.Attest(pin, challenge, unapproved);
            Throws<CryptographicException>(() => VaultRecoveryPinFloor.VerifyRequiredAttestation(pin, pin, challenge, replacement, anchor, transition));
            var bypass = VaultRecoveryPinFloor.AuthorizeRotation(pin.Workspace, 3, prior, unapproved);
            var later = pin with { Revision = 3 };
            byte[] offered = VaultRecoveryPinFloor.Attest(later, challenge, unapproved);
            Throws<CryptographicException>(() => VaultRecoveryPinFloor.VerifyRequiredAttestation(later, later, challenge, offered, anchor, transition, bypass));
        });
        Console.WriteLine($"{_passed}/{_total} Tur34 selected remote proof and operator key loss checks passed.");
        return _passed == _total ? 0 : 1;
    }

    [SupportedOSPlatform("windows")]
    private static int Tur34WindowsPathChecks()
    {
        Check("held live directory refuses ordinary rename during the replacement validation", () =>
        {
            using var fixture = new Fixture();
            using var root = LayerSecret.Regenerate(fixture.Root);
            var selected = SelectRootRekey(fixture, root);
            var anchor = new InterceptedAnchor(fixture.Provider);
            bool checkedDirectory = false;
            anchor.Observe = state =>
            {
                if (!checkedDirectory && Directory.EnumerateFiles(fixture.Live, "*.enote-sync").Any())
                {
                    Throws<IOException>(() => Directory.Move(fixture.Live, fixture.Live + "-renamed"));
                    checkedDirectory = true;
                }
                return state;
            };
            Equal(new VaultLiveSyncResult(1, 1), fixture.Published.SynchronizeSelectedLiveTree(
                new VaultGenerationRecovery(anchor, Store(fixture.Home)), selected, fixture.Root, root, 1));
            Equal(true, checkedDirectory);
            Equal(Text, LayeredEmd3.Open(File.ReadAllBytes(Path.Combine(fixture.Live, "Direct.emd")), [root], NewPassword));
        });

        Check("same-content destination replacement fails the Windows object check before installation", () =>
        {
            using var fixture = new Fixture();
            using var root = LayerSecret.Regenerate(fixture.Root);
            var selected = SelectRootRekey(fixture, root);
            var anchor = new InterceptedAnchor(fixture.Provider);
            string live = Path.Combine(fixture.Live, "Direct.emd");
            string retained = live + ".retained";
            anchor.Observe = state =>
            {
                if (!File.Exists(retained) && Directory.EnumerateFiles(fixture.Live, "*.enote-sync").Any())
                {
                    File.Move(live, retained);
                    File.Copy(retained, live);
                }
                return state;
            };
            Throws<IOException>(() => fixture.Published.SynchronizeSelectedLiveTree(
                new VaultGenerationRecovery(anchor, Store(fixture.Home)), selected, fixture.Root, root, 1));
            Equal(Digest(fixture.Direct), Digest(File.ReadAllBytes(live)));
            Equal(Digest(fixture.Direct), Digest(File.ReadAllBytes(retained)));
            Equal(0, Directory.EnumerateFiles(fixture.Live, "*.enote-sync").Count());
        });

        Check("selection change at the paired handle check leaves the original destination installed", () =>
        {
            using var fixture = new Fixture();
            using var root = LayerSecret.Regenerate(fixture.Root);
            var selected = SelectRootRekey(fixture, root);
            var anchor = new InterceptedAnchor(fixture.Provider);
            int stageChecks = 0;
            anchor.Observe = state =>
            {
                if (!Directory.EnumerateFiles(fixture.Live, "*.enote-sync").Any() || ++stageChecks != 2) return state;
                return state with
                {
                    Pending = new VaultRetryIntent(selected, new VaultGenerationStamp(fixture.Workspace,
                        selected.Epoch + 1, new string('D', 64)))
                };
            };
            Throws<VaultGenerationOutcomeUnknownException>(() => fixture.Published.SynchronizeSelectedLiveTree(
                new VaultGenerationRecovery(anchor, Store(fixture.Home)), selected, fixture.Root, root, 1));
            Equal(2, stageChecks);
            Equal(Digest(fixture.Direct), Digest(File.ReadAllBytes(Path.Combine(fixture.Live, "Direct.emd"))));
            Equal(0, Directory.EnumerateFiles(fixture.Live, "*.enote-sync").Count());
        });
        Console.WriteLine($"{_passed}/{_total} Tur34 pinned directories, destination identity and selected generation checks passed.");
        return _passed == _total ? 0 : 1;
    }

    private static int Tur34BackupChecks(string workspace, string backupRoot)
    {
        Check("loss and corruption across separate mounts leave the catalog absent until verified restore", () =>
        {
            using var fixture = new Fixture(workspace);
            using var root = LayerSecret.Regenerate(fixture.Root);
            var selected = SelectRootRekey(fixture, root);
            string catalog = Directory.EnumerateDirectories(Path.Combine(fixture.Home, "published"), "catalog-*-" + selected.ManifestSha256).Single();
            string backup = Path.Combine(backupRoot, "snapshot");
            fixture.Published.BackupSelectedCatalog(fixture.Recovery, selected, root, backup);
            string note = Path.Combine(backup, "notes", "Direct.emd");
            if (OperatingSystem.IsWindows())
            {
                using var primary = File.OpenRead(Path.Combine(catalog, "notes", "Direct.emd"));
                using var external = File.OpenRead(note);
                if (WindowsVaultFileIdentity.Read(primary.SafeFileHandle).VolumeSerialNumber ==
                    WindowsVaultFileIdentity.Read(external.SafeFileHandle).VolumeSerialNumber)
                    throw new IOException("Expected different Windows volume serials.");
            }
            Directory.Delete(catalog, recursive: true);
            string unavailable = Path.Combine(backupRoot, "unavailable");
            Directory.Move(backup, unavailable);
            Throws<IOException>(() => fixture.Published.RestoreMissingSelectedCatalog(fixture.Recovery, selected, root, backup));
            Equal(false, Directory.Exists(catalog));
            Directory.Move(unavailable, backup);
            byte[] verified = File.ReadAllBytes(note);
            byte[] corrupt = verified.ToArray();
            corrupt[^1] ^= 1;
            File.WriteAllBytes(note, corrupt);
            Throws<CryptographicException>(() => fixture.Published.RestoreMissingSelectedCatalog(fixture.Recovery, selected, root, backup));
            Equal(false, Directory.Exists(catalog));
            File.WriteAllBytes(note, verified);
            var anchor = new InterceptedAnchor(fixture.Provider);
            int reads = 0;
            anchor.Observe = state => ++reads == 1 ? state : state with
            {
                Pending = new VaultRetryIntent(selected, new VaultGenerationStamp(fixture.Workspace,
                    selected.Epoch + 1, new string('E', 64)))
            };
            Throws<VaultGenerationOutcomeUnknownException>(() => fixture.Published.RestoreMissingSelectedCatalog(
                new VaultGenerationRecovery(anchor, Store(fixture.Home)), selected, root, backup));
            Equal(false, Directory.Exists(catalog));
            Equal(false, Directory.EnumerateDirectories(Path.GetDirectoryName(catalog)!, "*.snapshot").Any());
            fixture.Published.RestoreMissingSelectedCatalog(fixture.Recovery, selected, root, backup);
            Equal(2, fixture.Published.InspectSelectedCatalog(fixture.Recovery, fixture.Workspace, root).Documents);
        });
        Console.WriteLine($"{_passed}/{_total} Tur34 separate volume catalog loss, corruption and restore checks passed.");
        return _passed == _total ? 0 : 1;
    }
}
