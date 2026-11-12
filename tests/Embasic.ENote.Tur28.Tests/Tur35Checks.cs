using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Embasic.ENote.Emd;
using Embasic.ENote.Storage;

internal static partial class Program
{
    [SupportedOSPlatform("windows")]
    private static int Tur35WindowsPathChecks()
    {
        Check("path publication installs the verified stage object and preserves the target DACL", () =>
        {
            using var fixture = new Fixture();
            using var root = LayerSecret.Regenerate(fixture.Root);
            var selected = SelectRootRekey(fixture, root);
            var destination = new FileInfo(Path.Combine(fixture.Live, "Direct.emd"));
            using var identity = WindowsIdentity.GetCurrent();
            var security = new FileSecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.FullControl, AccessControlType.Allow));
            destination.SetAccessControl(security);
            string expectedAcl = ReadDacl(destination);
            var original = ReadIdentity(destination.FullName);
            var anchor = new InterceptedAnchor(fixture.Provider);
            WindowsVaultFileIdentity? stageIdentity = null;
            anchor.Observe = state =>
            {
                if (stageIdentity is null)
                {
                    string? stage = Directory.EnumerateFiles(fixture.Live, "*.enote-sync").SingleOrDefault();
                    if (stage is not null) stageIdentity = ReadIdentity(stage);
                }
                return state;
            };
            Equal(new VaultLiveSyncResult(1, 1), fixture.Published.SynchronizeSelectedLiveTree(
                new VaultGenerationRecovery(anchor, Store(fixture.Home)), selected, fixture.Root, root, 1));
            var installed = ReadIdentity(destination.FullName);
            Equal(true, stageIdentity.HasValue && stageIdentity.Value == installed);
            Equal(false, original == installed);
            Equal(expectedAcl, ReadDacl(destination));
            Equal(Text, LayeredEmd3.Open(File.ReadAllBytes(destination.FullName), [root], NewPassword));
        });
        Console.WriteLine($"{_passed}/{_total} Tur35 Windows installed identity and target DACL checks passed.");
        return _passed == _total ? 0 : 1;

        static string ReadDacl(FileInfo file) =>
            file.GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorSddlForm(AccessControlSections.Access);

        static WindowsVaultFileIdentity ReadIdentity(string path)
        {
            using var file = File.OpenRead(path);
            return WindowsVaultFileIdentity.Read(file.SafeFileHandle);
        }
    }

    [SupportedOSPlatform("windows")]
    private static int Tur35BackupChecks(string workspace, string backupRoot)
    {
        Check("detached NTFS backup refuses restore after verification and reattached media allows a verified retry", () =>
        {
            using var fixture = new Fixture(workspace);
            using var root = LayerSecret.Regenerate(fixture.Root);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(70));
            var selected = SelectRootRekey(fixture, root);
            string published = Path.Combine(fixture.Home, "published");
            string catalog = Directory.EnumerateDirectories(published, "catalog-*-" + selected.ManifestSha256).Single();
            string backup = Path.Combine(backupRoot, "snapshot");
            fixture.Published.BackupSelectedCatalog(fixture.Recovery, selected, root, backup);
            string backupNote = Path.Combine(backup, "notes", "Direct.emd");
            WindowsVaultFileIdentity backupIdentity;
            using (var local = File.OpenRead(Path.Combine(catalog, "notes", "Direct.emd")))
            using (var external = File.OpenRead(backupNote))
            {
                backupIdentity = WindowsVaultFileIdentity.Read(external.SafeFileHandle);
                Equal(false, backupIdentity.VolumeSerialNumber == WindowsVaultFileIdentity.Read(local.SafeFileHandle).VolumeSerialNumber);
            }
            Directory.Delete(catalog, recursive: true);
            Throws<IOException>(() => fixture.Published.RestoreMissingSelectedCatalog(fixture.Recovery, selected, root, backup, () =>
            {
                WriteFixtureReceipt(Path.Combine(workspace, "restore-verified"), "verified");
                WaitForSignal(workspace, "volume-detached", deadline.Token);
                Equal(false, File.Exists(backupNote));
            }));
            Equal(false, Directory.Exists(catalog));
            Equal(false, Directory.EnumerateDirectories(published, "*.snapshot").Any());
            WriteFixtureReceipt(Path.Combine(workspace, "restore-blocked"), "blocked");
            WaitForSignal(workspace, "volume-reattached", deadline.Token);
            using (var external = File.OpenRead(backupNote))
                Equal(backupIdentity, WindowsVaultFileIdentity.Read(external.SafeFileHandle));
            fixture.Published.RestoreMissingSelectedCatalog(fixture.Recovery, selected, root, backup);
            Equal(2, fixture.Published.InspectSelectedCatalog(fixture.Recovery, fixture.Workspace, root).Documents);
        });
        Console.WriteLine($"{_passed}/{_total} Tur35 detached NTFS volume restore and verified retry checks passed.");
        return _passed == _total ? 0 : 1;
    }
}
