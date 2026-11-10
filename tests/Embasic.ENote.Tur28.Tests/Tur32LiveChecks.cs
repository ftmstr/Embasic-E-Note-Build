using System.Globalization;
using System.Security.Cryptography;
using Embasic.ENote.Emd;
using Embasic.ENote.Storage;

internal static partial class Program
{
    private static void Tur32LiveChecks()
    {
        Check("midcopy process termination reclaims only authenticated partial stages and preserves Windows sharing", () =>
        {
            using var f = new Fixture();
            using var root = LayerSecret.Regenerate(f.Root);
            var candidate = SelectRootRekey(f, root);
            string marker = Path.Combine(f.Home, "midcopy-confirmed");
            string original = Path.Combine(f.Live, "Direct.emd");
            string unrelated = Path.Combine(f.Live, "." + Guid.NewGuid().ToString("N") + ".enote-sync");
            File.WriteAllText(unrelated, "unattributed legacy stage");
            using var worker = StartHarness("tur32-copy-worker", f.Home, f.Workspace.ToString("D"),
                f.ProviderId.ToString("D"), f.Root.Id.ToString("D"), candidate.Epoch.ToString(CultureInfo.InvariantCulture),
                candidate.ManifestSha256);
            try
            {
                Equal(true, SpinWait.SpinUntil(() => File.Exists(marker) || worker.HasExited, TimeSpan.FromSeconds(25)));
                Equal(false, worker.HasExited);
                Equal(true, File.Exists(marker));
                Equal(Digest(f.Direct), Digest(File.ReadAllBytes(original)));
                Throws<IOException>(() => f.Published.EnterLiveWrite().Dispose());
            }
            finally
            {
                if (!worker.HasExited) worker.Kill();
                if (!worker.WaitForExit(10_000)) throw new TimeoutException("Killed copy worker did not exit.");
            }

            string partial = Directory.EnumerateFiles(f.Live, "*.enote-sync").Single(path => path != unrelated);
            Equal("ELK1", File.ReadAllText(partial));
            string wrongDirectory = Path.Combine(f.Live, "Sub", Path.GetFileName(partial));
            File.Copy(partial, wrongDirectory);
            using var reopened = Publication(f.Home);
            if (OperatingSystem.IsWindows())
            {
                using var reader = new FileStream(partial, FileMode.Open, FileAccess.Read, FileShare.Read);
                Throws<IOException>(() => reopened.SynchronizeSelectedLiveTree(f.Recovery, candidate, f.Root, root));
                Equal(Digest(f.Direct), Digest(File.ReadAllBytes(original)));
            }
            var resumed = reopened.SynchronizeSelectedLiveTree(f.Recovery, candidate, f.Root, root);
            Equal(1, resumed.ReclaimedStages);
            Equal(2, resumed.Replaced);
            Equal(false, File.Exists(partial));
            Equal(true, File.Exists(wrongDirectory));
            Equal("unattributed legacy stage", File.ReadAllText(unrelated));
            Equal(Text, LayeredEmd3.Open(File.ReadAllBytes(original), [root], NewPassword));
            Equal(new VaultLiveSyncResult(0, 0), reopened.SynchronizeSelectedLiveTree(f.Recovery, candidate, f.Root, root));
        });

        Check("catalog backup copies selected ciphertext only and restores an absent catalog without replacing live grants", () =>
        {
            using var f = new Fixture();
            using var root = LayerSecret.Regenerate(f.Root);
            var candidate = SelectRootRekey(f, root);
            var directory = Directory.EnumerateDirectories(Path.Combine(f.Home, "published"), "catalog-*-" + candidate.ManifestSha256).Single();
            string unrelated = Path.Combine(directory, "notes", "retained.txt");
            File.WriteAllText(unrelated, "not part of the authenticated snapshot");
            string backup = Path.Combine(f.Home, "catalog-backup");
            Throws<ArgumentException>(() => f.Published.BackupSelectedCatalog(f.Recovery, candidate, root, Path.Combine(f.Live, "backup")));
            f.Published.BackupSelectedCatalog(f.Recovery, candidate, root, backup);
            Equal(false, File.Exists(Path.Combine(backup, "notes", "retained.txt")));
            Equal(true, File.Exists(unrelated));
            Throws<IOException>(() => f.Published.RestoreMissingSelectedCatalog(f.Recovery, candidate, root, backup));
            Directory.Move(directory, Path.Combine(f.Home, "retained-catalog"));
            string backupNote = Path.Combine(backup, "notes", "Direct.emd");
            byte[] ciphertext = File.ReadAllBytes(backupNote);
            byte[] corrupt = ciphertext.ToArray();
            corrupt[^1] ^= 1;
            File.WriteAllBytes(backupNote, corrupt);
            Throws<CryptographicException>(() => f.Published.RestoreMissingSelectedCatalog(f.Recovery, candidate, root, backup));
            Equal(false, Directory.Exists(directory));
            File.WriteAllBytes(backupNote, ciphertext);
            f.Published.RestoreMissingSelectedCatalog(f.Recovery, candidate, root, backup);
            Equal(2, f.Published.InspectSelectedCatalog(f.Recovery, f.Workspace, root).Documents);
            Equal(new VaultLiveSyncResult(2, 0), f.Published.SynchronizeSelectedLiveTree(f.Recovery, candidate, f.Root, root));
        });
    }
}
