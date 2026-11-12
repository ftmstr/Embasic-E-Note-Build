using System.Security.Cryptography;
using Embasic.ENote.Emd;

namespace Embasic.ENote.Storage;

public sealed partial class VaultRekeyPublication
{
    /// <summary>Export just the authenticated selected snapshot to a new directory outside the stores.</summary>
    public void BackupSelectedCatalog(VaultGenerationRecovery recovery, VaultGenerationStamp selected,
        LayerSecret root, string destination)
    {
        ArgumentNullException.ThrowIfNull(recovery);
        ArgumentNullException.ThrowIfNull(selected);
        using var fence = EnterLiveWrite();
        var state = recovery.ReadAuthorityState(selected.WorkspaceId);
        var manifest = ReadBackupSelection(state, selected);
        var snapshot = OpenRegisteredSnapshot(manifest, root)
            ?? throw new InvalidDataException("Selected registered catalog is absent.");
        CopySelectedCatalog(snapshot, manifest, root, CheckCatalogBackupLocation(destination), RequireCurrent);

        void RequireCurrent()
        {
            if (recovery.ReadAuthorityState(selected.WorkspaceId) != state)
                throw new VaultGenerationOutcomeUnknownException("Selection changed during catalog backup.");
        }
    }

    /// <summary>Restore a missing snapshot only. Live grants and unknown existing catalog data are never replaced.</summary>
    public void RestoreMissingSelectedCatalog(VaultGenerationRecovery recovery, VaultGenerationStamp selected,
        LayerSecret root, string backupDirectory) =>
        RestoreMissingSelectedCatalog(recovery, selected, root, backupDirectory, null);

    internal void RestoreMissingSelectedCatalog(VaultGenerationRecovery recovery, VaultGenerationStamp selected,
        LayerSecret root, string backupDirectory, Action? afterBackupVerified)
    {
        ArgumentNullException.ThrowIfNull(recovery);
        ArgumentNullException.ThrowIfNull(selected);
        using var fence = EnterLiveWrite();
        var state = recovery.ReadAuthorityState(selected.WorkspaceId);
        var manifest = ReadBackupSelection(state, selected);
        var snapshot = VaultWorkspaceSnapshot.Open(CheckCatalogBackupLocation(backupDirectory), selected.WorkspaceId, root);
        snapshot.VerifyAgainst(manifest, root);
        afterBackupVerified?.Invoke();
        CopySelectedCatalog(snapshot, manifest, root, CatalogPath(selected), RequireCurrent);

        void RequireCurrent()
        {
            if (recovery.ReadAuthorityState(selected.WorkspaceId) != state)
                throw new VaultGenerationOutcomeUnknownException("Selection changed during catalog restoration.");
        }
    }

    private VaultGenerationManifest ReadBackupSelection(VaultTransitionState state, VaultGenerationStamp selected)
    {
        if (state.Current != selected || state.Pending is not null)
            throw new CryptographicException("Catalog backup or restore requires the quiescent selected generation.");
        _ = ReadWorkspacePublicationNames(selected.WorkspaceId);
        var manifest = ReadExact(selected);
        if (manifest.Bindings?.RekeySha256 is null) throw new InvalidDataException("No registered rekey catalog.");
        return manifest;
    }

    private static void CopySelectedCatalog(VaultWorkspaceSnapshot snapshot, VaultGenerationManifest manifest,
        LayerSecret root, string destination, Action requireCurrent)
    {
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new IOException("Catalog destination already exists.");
        string stage = destination + "." + Guid.NewGuid().ToString("N") + ".snapshot";
        try
        {
            snapshot.Materialize(stage, manifest, _ => null, (_, _) => throw new InvalidOperationException());
            VaultWorkspaceSnapshot.Open(stage, manifest.Stamp.WorkspaceId, root).VerifyAgainst(manifest, root);
            requireCurrent();
            Directory.Move(stage, destination);
        }
        finally
        {
            if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true);
        }
    }

    private string CheckCatalogBackupLocation(string path)
    {
        string candidate = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(candidate) ?? throw new ArgumentException("Backup requires a parent.", nameof(path));
        VaultWorkspaceSnapshot.CheckDirectoryPath(parent);
        foreach (string store in new[] { _liveRoot, _publishedPath, _journalPath, _snapshotPath })
            if (Within(store, candidate) || Within(candidate, store))
                throw new ArgumentException("Catalog backup must be outside and disjoint from managed stores.", nameof(path));
        return candidate;

        static bool Within(string parent, string child)
        {
            string relative = Path.GetRelativePath(parent, child);
            return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                !Path.IsPathRooted(relative);
        }
    }
}
