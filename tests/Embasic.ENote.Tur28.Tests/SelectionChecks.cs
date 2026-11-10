using System.Security.Cryptography;
using Embasic.ENote.Emd;
using Embasic.ENote.Storage;

internal static partial class Program
{
    private static void SelectionChecks()
    {
        Check("selected catalog serves original, affected and unaffected entries without reading live edits", () =>
        {
            using var f = new Fixture();
            Throws<InvalidDataException>(() => f.Published.ReadSelectedDocument(f.Recovery, f.Workspace, f.Root, "Vault/Direct.emd"));
            f.Enroll();
            Equal(Digest(f.Direct), Digest(f.Published.ReadSelectedDocument(f.Recovery, f.Workspace, f.Root, "Vault/Direct.emd")));
            using var fresh = LayerSecret.Regenerate(f.Child);
            var next = f.Prepare(fresh);
            Equal(VaultGenerationAdvanceState.Committed, f.Published.Resume(f.Recovery, next.Manifest.Stamp, f.Root));
            File.WriteAllBytes(Path.Combine(f.Live, "Direct.emd"), [1, 2, 3]);
            Equal(Digest(f.Direct), Digest(f.Published.ReadSelectedDocument(f.Recovery, f.Workspace, f.Root, "Vault/Direct.emd")));
            byte[] changed = f.Published.ReadSelectedDocument(f.Recovery, f.Workspace, f.Root, "Vault/Sub/Inherited.emd");
            Equal(next.TargetDigest, Digest(changed));
            Equal(Text, LayeredEmd3.Open(changed, [f.Root, fresh], NewPassword));
            Throws<FileNotFoundException>(() =>
                f.Published.ReadSelectedDocument(f.Recovery, f.Workspace, f.Root, "Vault/../Direct.emd"));
            File.Delete(Path.Combine(f.Home, "published", next.TargetDigest + ".emd"));
            Throws<InvalidDataException>(() =>
                f.Published.ReadSelectedDocument(f.Recovery, f.Workspace, f.Root, "Vault/Sub/Inherited.emd"));
        });

        Check("independent writer process excludes publication and dies without a stale lock", () =>
        {
            using var f = new Fixture();
            f.Enroll();
            using var fresh = LayerSecret.Regenerate(f.Child);
            var next = f.Prepare(fresh);
            using var child = HoldChild(f.Home);
            try
            {
                Equal(true, SpinWait.SpinUntil(() => File.Exists(Path.Combine(f.Home, "held")), TimeSpan.FromSeconds(15)));
                Throws<IOException>(() => f.Published.Resume(f.Recovery, next.Manifest.Stamp, f.Root));
                Throws<IOException>(() => f.Published.ReadSelectedDocument(f.Recovery, f.Workspace, f.Root, "Vault/Direct.emd"));
                Equal(f.Enrolled!.Stamp, f.Recovery.ReadCommitted(f.Workspace).Stamp);
            }
            finally
            {
                if (!child.HasExited) child.Kill();
                child.WaitForExit();
            }
            Equal(VaultGenerationAdvanceState.Committed, f.Published.Resume(f.Recovery, next.Manifest.Stamp, f.Root));
        });

        Check("restart backup restores only a pending or selected exact ERJ1 and detects tampering", () =>
        {
            using var f = new Fixture();
            using var fresh = LayerSecret.Regenerate(f.Child);
            var next = f.Prepare(fresh).Manifest.Stamp;
            byte[] backup = f.Published.ExportRestartJournal(next);
            File.Delete(f.Journal(next));
            Throws<CryptographicException>(() => f.Published.RestoreRestartJournal(f.Recovery, next, backup));
            f.Provider.ReserveTransition(new VaultRetryIntent(f.Enrolled!.Stamp, next));
            byte[] tampered = (byte[])backup.Clone();
            tampered[^1] ^= 1;
            Throws<CryptographicException>(() => f.Published.RestoreRestartJournal(f.Recovery, next, tampered));
            f.Published.RestoreRestartJournal(f.Recovery, next, backup);
            Equal(VaultGenerationAdvanceState.Committed, f.Published.Resume(f.Recovery, next, f.Root));
            File.Delete(f.Journal(next));
            f.Published.RestoreRestartJournal(f.Recovery, next, backup);
            Equal(VaultGenerationAdvanceState.Committed, f.Published.Resume(f.Recovery, next, f.Root));
        });

        Check("cleanup removes only the authenticated losing sibling and preserves ciphertext and winner", () =>
        {
            using var f = new Fixture();
            using var first = LayerSecret.Regenerate(f.Child);
            using var second = LayerSecret.Regenerate(f.Child);
            var winner = f.Prepare(first).Manifest.Stamp;
            var loser = f.Prepare(second).Manifest.Stamp;
            Throws<InvalidOperationException>(() => f.Published.DiscardUnselectedSibling(f.Recovery, loser));
            Equal(VaultGenerationAdvanceState.Committed, f.Published.Resume(f.Recovery, winner, f.Root));
            Throws<InvalidOperationException>(() => f.Published.DiscardUnselectedSibling(f.Recovery, winner));
            int objects = Directory.EnumerateFiles(Path.Combine(f.Home, "published")).Count();
            byte[] good = File.ReadAllBytes(f.Journal(loser));
            byte[] corrupt = (byte[])good.Clone();
            corrupt[^1] ^= 1;
            File.WriteAllBytes(f.Journal(loser), corrupt);
            Throws<CryptographicException>(() => f.Published.DiscardUnselectedSibling(f.Recovery, loser));
            Equal(true, File.Exists(f.Journal(loser)));
            File.WriteAllBytes(f.Journal(loser), good);
            Equal(true, f.Published.DiscardUnselectedSibling(f.Recovery, loser));
            Equal(false, f.Published.DiscardUnselectedSibling(f.Recovery, loser));
            Equal(true, File.Exists(f.Journal(winner)));
            Equal(objects, Directory.EnumerateFiles(Path.Combine(f.Home, "published")).Count());
        });
    }
}
