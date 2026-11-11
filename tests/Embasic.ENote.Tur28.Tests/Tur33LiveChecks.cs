using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using Embasic.ENote.Emd;
using Embasic.ENote.Storage;

internal static partial class Program
{
    private static void Tur33LiveChecks()
    {
        Check("a selection change after the first bytes aborts the full copy before live replacement", () =>
        {
            using var f = new Fixture();
            using var root = LayerSecret.Regenerate(f.Root);
            var candidate = SelectRootRekey(f, root);
            var anchor = new InterceptedAnchor(f.Provider);
            var recovery = new VaultGenerationRecovery(anchor, Store(f.Home));
            bool copying = false;
            Throws<VaultGenerationOutcomeUnknownException>(() =>
                f.Published.SynchronizeSelectedLiveTree(recovery, candidate, f.Root, root, 1, _ =>
                {
                    copying = true;
                    anchor.Observe = state => state with
                    {
                        Pending = new VaultRetryIntent(candidate, new VaultGenerationStamp(f.Workspace,
                            candidate.Epoch + 1, new string('A', 64)))
                    };
                }));
            Equal(true, copying);
            Equal(Digest(f.Direct), Digest(File.ReadAllBytes(Path.Combine(f.Live, "Direct.emd"))));
            Equal(0, Directory.EnumerateFiles(f.Live, "*.enote-sync").Count());
            Equal(new VaultLiveSyncResult(2, 0), f.Published.SynchronizeSelectedLiveTree(f.Recovery, candidate, f.Root, root));
        });

        Check("only a direct authenticated ancestor stage is reclaimed and an external backup survives catalog loss", () =>
        {
            using var f = new Fixture();
            using var backup = new TempBackup();
            using var firstRoot = LayerSecret.Regenerate(f.Root);
            using var secondRoot = LayerSecret.Regenerate(f.Root);
            var first = SelectRootRekey(f, firstRoot);
            Equal(new VaultLiveSyncResult(2, 0), f.Published.SynchronizeSelectedLiveTree(f.Recovery, first, f.Root, firstRoot));
            var second = PrepareRoot(f, firstRoot, secondRoot, NewPassword, Password).Next.Stamp;
            Equal(VaultGenerationAdvanceState.Committed, f.Published.Resume(f.Recovery, second, firstRoot));
            f.Published.RegisterSelectedSnapshot(f.Recovery, second, firstRoot, secondRoot);
            string target = Path.Combine(backup.Path, "snapshot");
            f.Published.BackupSelectedCatalog(f.Recovery, second, secondRoot, target);
            string catalog = Directory.EnumerateDirectories(Path.Combine(f.Home, "published"), "catalog-*-" + second.ManifestSha256).Single();
            Directory.Delete(catalog, recursive: true);
            Equal(true, Directory.Exists(target));
            f.Published.RestoreMissingSelectedCatalog(f.Recovery, second, secondRoot, target);
            const string direct = "Vault/Direct.emd";
            string eligible = Path.Combine(f.Live, f.Published.StageName(first, direct, Guid.NewGuid()));
            string older = Path.Combine(f.Live, f.Published.StageName(f.Enrolled!.Stamp, direct, Guid.NewGuid()));
            File.WriteAllText(eligible, "ELK1");
            File.WriteAllText(older, "ELK1");
            var result = f.Published.SynchronizeSelectedLiveTree(f.Recovery, second, firstRoot, secondRoot);
            Equal(1, result.ReclaimedStages);
            Equal(2, result.Replaced);
            Equal(false, File.Exists(eligible));
            Equal(true, File.Exists(older));
        });
    }

    [SupportedOSPlatform("windows")]
    private static int Tur33WindowsStageChecks()
    {
        Check("same ciphertext on a different Windows file object is refused and the substitute is retained", () =>
        {
            using var f = new Fixture();
            using var root = LayerSecret.Regenerate(f.Root);
            var candidate = SelectRootRekey(f, root);
            var anchor = new InterceptedAnchor(f.Provider);
            string? substituted = null;
            anchor.Observe = state =>
            {
                if (substituted is not null) return state;
                string? stage = Directory.EnumerateFiles(f.Live, "*.enote-sync").SingleOrDefault();
                if (stage is null) return state;
                string old = stage + ".retained";
                File.Move(stage, old);
                File.Copy(old, stage);
                substituted = stage;
                return state;
            };
            Throws<IOException>(() => f.Published.SynchronizeSelectedLiveTree(
                new VaultGenerationRecovery(anchor, Store(f.Home)), candidate, f.Root, root, 1));
            Equal(true, substituted is not null && File.Exists(substituted));
            Equal(Digest(f.Direct), Digest(File.ReadAllBytes(Path.Combine(f.Live, "Direct.emd"))));
        });

        Check("the exact authenticated stage name with multiple links is refused without unlinking", () =>
        {
            using var f = new Fixture();
            using var root = LayerSecret.Regenerate(f.Root);
            var candidate = SelectRootRekey(f, root);
            string original = Path.Combine(f.Live, "retained-object.txt");
            File.WriteAllText(original, "ELK1");
            string stage = Path.Combine(f.Live, f.Published.StageName(candidate, "Vault/Direct.emd", Guid.NewGuid()));
            if (!CreateHardLinkW(stage, original, IntPtr.Zero))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
            Throws<IOException>(() => f.Published.SynchronizeSelectedLiveTree(f.Recovery, candidate, f.Root, root));
            Equal("ELK1", File.ReadAllText(original));
            Equal(true, File.Exists(stage));
        });

        Check("a real directory junction in the selected live tree is refused", () =>
        {
            using var f = new Fixture();
            using var root = LayerSecret.Regenerate(f.Root);
            var candidate = SelectRootRekey(f, root);
            string original = Path.Combine(f.Live, "Sub");
            string moved = Path.Combine(f.Home, "junction-target");
            Directory.Move(original, moved);
            var command = new System.Diagnostics.ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = false,
                Arguments = $"/d /c mklink /J \"{original}\" \"{moved}\""
            };
            using var make = System.Diagnostics.Process.Start(command)!;
            if (!make.WaitForExit(10000) || make.ExitCode != 0)
                throw new IOException("Could not create the test junction.");
            try
            {
                Throws<IOException>(() => f.Published.SynchronizeSelectedLiveTree(f.Recovery, candidate, f.Root, root));
                Equal(Digest(f.Inherited), Digest(File.ReadAllBytes(Path.Combine(moved, "Inherited.emd"))));
            }
            finally { Directory.Delete(original); }
        });
        Console.WriteLine($"{_passed}/{_total} Tur33 Windows identity, hard link and junction checks passed.");
        return _total == _passed ? 0 : 1;
    }

    private sealed class TempBackup : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("enote33-backup-").FullName;
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private sealed class InterceptedAnchor(VaultTransitionFilePrototype inner) : IExternalVaultTransitionAnchor
    {
        internal Func<VaultTransitionState, VaultTransitionState> Observe { get; set; } = state => state;
        public VaultTransitionState? ReadTransition(Guid workspaceId) => inner.ReadTransition(workspaceId) is { } state ? Observe(state) : null;
        public void ReserveTransition(VaultRetryIntent intent) => inner.ReserveTransition(intent);
        public void CommitTransition(VaultRetryIntent intent) => inner.CommitTransition(intent);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string newName, string existing, IntPtr security);
}
