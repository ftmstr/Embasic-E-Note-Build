using System.Security.Cryptography;
using Embasic.ENote.Emd;

namespace Embasic.ENote.Storage;

public sealed partial class VaultRekeyPublication
{
    /// <summary>Capture and enroll under the same exclusion used by cooperating live writers and rekey.</summary>
    public VaultGenerationAdvanceState CaptureAndEnroll(VaultGenerationRecovery recovery, Guid workspace,
        LayerSecret root, ReadOnlySpan<byte> index)
    {
        ArgumentNullException.ThrowIfNull(recovery);
        using var fence = EnterLiveWrite();
        var selected = recovery.ReadCommitted(workspace);
        if (selected.Bindings is { } bindings)
        {
            if (bindings.RekeySha256 is not null)
                throw new InvalidOperationException("An enrolled workspace already has a selected rekey.");
            var enrolled = OpenSourceSnapshot(selected, root);
            enrolled.VerifyAgainst(selected, root);
            if (!enrolled.HasIndex(index) || recovery.ReadCommitted(workspace).Stamp != selected.Stamp)
                throw new CryptographicException("The selected enrollment does not match this capture.");
            return VaultGenerationAdvanceState.Committed;
        }
        var snapshot = Directory.Exists(_snapshotPath)
            ? VaultWorkspaceSnapshot.Open(_snapshotPath, workspace, root)
            : VaultWorkspaceSnapshot.Capture(_liveRoot, _snapshotPath, selected, root, index);
        if (!snapshot.HasIndex(index))
            throw new CryptographicException("Existing capture uses a different encrypted index.");
        return recovery.EnrollCatalog(selected.Stamp, snapshot, root, _liveRoot);
    }

    /// <summary>Register the complete verified ciphertext catalog of an independently selected rekey.</summary>
    public void RegisterSelectedSnapshot(VaultGenerationRecovery recovery, VaultGenerationStamp candidate,
        LayerSecret sourceRoot, LayerSecret selectedRoot)
    {
        ArgumentNullException.ThrowIfNull(recovery);
        ArgumentNullException.ThrowIfNull(candidate);
        using var fence = EnterLiveWrite();
        var state = recovery.ReadAuthorityState(candidate.WorkspaceId);
        if (state.Current != candidate || state.Pending is not null)
            throw new CryptographicException("Snapshot registration requires the committed candidate and no pending transition.");

        var selected = ReadExact(candidate);
        if (OpenRegisteredSnapshot(selected, selectedRoot) is not null)
        {
            if (recovery.ReadAuthorityState(candidate.WorkspaceId) != state)
                throw new CryptographicException("The independent selection changed during verification.");
            return;
        }
        var (source, snapshot, commitment) = ReadPublicationPlan(candidate, sourceRoot);
        commitment.RequireMatch(source, selected);
        VerifyPublication(commitment, selected);

        string path = CatalogPath(candidate);
        string stage = path + "." + Guid.NewGuid().ToString("N") + ".snapshot";
        try
        {
            snapshot.Materialize(stage, source, item => commitment.Targets.GetValueOrDefault(item.Id), OpenObject);
            VaultWorkspaceSnapshot.Open(stage, candidate.WorkspaceId, selectedRoot).VerifyAgainst(selected, selectedRoot);
            if (recovery.ReadAuthorityState(candidate.WorkspaceId) != state)
                throw new CryptographicException("The independent selection changed during registration.");
            Directory.Move(stage, path);
        }
        finally
        {
            if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true);
        }
    }

    /// <summary>Open the verified frozen snapshot for staging from the currently selected catalog.</summary>
    public VaultWorkspaceSnapshot OpenSelectedSnapshot(VaultGenerationRecovery recovery, Guid workspace, LayerSecret root)
    {
        ArgumentNullException.ThrowIfNull(recovery);
        using var fence = EnterLiveWrite();
        var selected = recovery.ReadCommitted(workspace);
        if (selected.Bindings is null) throw new InvalidDataException("Selected generation has no catalog.");
        var snapshot = OpenSourceSnapshot(selected, root);
        snapshot.VerifyAgainst(selected, root);
        if (recovery.ReadCommitted(workspace).Stamp != selected.Stamp)
            throw new CryptographicException("The independent selection changed during snapshot verification.");
        return snapshot;
    }

    /// <summary>Restore exact selected or reserved ciphertext from an explicit backup under the writer fence.</summary>
    public void RestorePublishedObject(VaultGenerationRecovery recovery, VaultGenerationStamp candidate,
        string artifactId, Stream backup, LayerSecret sourceRoot)
    {
        ArgumentNullException.ThrowIfNull(recovery);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentException.ThrowIfNullOrEmpty(artifactId);
        ArgumentNullException.ThrowIfNull(backup);
        using var fence = EnterLiveWrite();
        var state = recovery.ReadAuthorityState(candidate.WorkspaceId);
        var (source, _, commitment) = ReadPublicationPlan(candidate, sourceRoot);
        var intent = new VaultRetryIntent(source.Stamp, candidate);
        if (state.Pending != intent && !(state.Current == candidate && state.LastCommitted == intent))
            throw new CryptographicException("Object restore requires an exact reserved or committed transition.");
        commitment.RequireMatch(source, ReadExact(candidate));
        var item = commitment.Inventory.Artifacts.SingleOrDefault(x => x.Id == artifactId)
            ?? throw new InvalidDataException("Object is absent from the authenticated publication plan.");
        if (item.Kind == VaultRekeyArtifactKind.FactorVault)
            throw new InvalidOperationException("A replacement file cannot restore the selected vault identity.");

        string digest = commitment.Targets[artifactId];
        _published.Publish(NameFor(item, digest), output =>
        {
            VaultRekeyStaging.Verify(item, backup, digest, output);
            if (recovery.ReadAuthorityState(candidate.WorkspaceId) != state)
                throw new CryptographicException("The independent selection changed during restore.");
        }, () => VerifyObject(item, digest));
    }

    private (VaultGenerationManifest Source, VaultWorkspaceSnapshot Snapshot, VaultRekeyCommitment Commitment)
        ReadPublicationPlan(VaultGenerationStamp candidate, LayerSecret root)
    {
        var plan = ReadPlan(candidate);
        var source = ReadExact(plan.Expected);
        var snapshot = OpenSourceSnapshot(source, root);
        var inventory = snapshot.BuildInventory(source, root, plan.Factors);
        return (source, snapshot, new VaultRekeyCommitment(inventory, plan.Targets));
    }

    private string CatalogPath(VaultGenerationStamp stamp) =>
        Path.Combine(_publishedPath, "catalog-" + stamp.WorkspaceId.ToString("N") + "-" + stamp.ManifestSha256);

    private VaultWorkspaceSnapshot OpenSourceSnapshot(VaultGenerationManifest manifest, LayerSecret root) =>
        OpenRegisteredSnapshot(manifest, root) ??
        VaultWorkspaceSnapshot.Open(_snapshotPath, manifest.Stamp.WorkspaceId, root);

    private VaultWorkspaceSnapshot? OpenRegisteredSnapshot(VaultGenerationManifest manifest, LayerSecret root)
    {
        string path = CatalogPath(manifest.Stamp);
        try { _ = File.GetAttributes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        var snapshot = VaultWorkspaceSnapshot.Open(path, manifest.Stamp.WorkspaceId, root);
        snapshot.VerifyAgainst(manifest, root);
        return snapshot;
    }
}
