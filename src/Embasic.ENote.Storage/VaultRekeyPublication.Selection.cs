using System.Security.Cryptography;
using Embasic.ENote.Emd;

namespace Embasic.ENote.Storage;

public sealed partial class VaultRekeyPublication
{
    /// <summary>All cooperating live writers and publication instances must use this protected journal directory.</summary>
    public IDisposable EnterLiveWrite()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _journal.OpenExclusiveLock("workspace-write.lock");
    }

    /// <summary>Return verified ciphertext for one catalog entry of the currently selected generation.</summary>
    public byte[] ReadSelectedDocument(VaultGenerationRecovery recovery, Guid workspace, LayerSecret sourceRoot,
        string relativePath)
    {
        ArgumentNullException.ThrowIfNull(recovery);
        ArgumentException.ThrowIfNullOrEmpty(relativePath);
        using var fence = EnterLiveWrite();
        var selected = recovery.ReadCommitted(workspace);
        if (selected.Bindings is null) throw new InvalidDataException("Selected generation has no catalog.");
        var snapshot = VaultWorkspaceSnapshot.Open(_snapshotPath, workspace, sourceRoot);
        string? target = null;
        if (selected.Bindings.RekeySha256 is not null)
        {
            var plan = ReadPlan(selected.Stamp);
            var source = ReadExact(plan.Expected);
            var inventory = snapshot.BuildInventory(source, sourceRoot, plan.Factors);
            var commitment = new VaultRekeyCommitment(inventory, plan.Targets);
            commitment.RequireMatch(source, selected);
            target = commitment.Targets.GetValueOrDefault("note:" + relativePath);
        }
        else
        {
            snapshot.VerifyAgainst(selected, sourceRoot);
        }

        var entry = snapshot.Documents.SingleOrDefault(item => item.RelativePath == relativePath)
            ?? throw new FileNotFoundException("Path is absent from the selected catalog.");
        var artifact = new VaultRekeyArtifact("note:" + entry.RelativePath, VaultRekeyArtifactKind.Document,
            entry.RelativePath, entry.SourceSha256, Array.Empty<Guid>());
        using var input = target is null ? snapshot.OpenSource(artifact) : OpenObject(artifact, target);
        using var buffer = new MemoryStream();
        VaultRekeyStaging.Verify(artifact, input, target ?? entry.SourceSha256, buffer);
        if (recovery.ReadCommitted(workspace).Stamp != selected.Stamp)
            throw new CryptographicException("The selected generation changed during the read.");
        return buffer.ToArray();
    }

    /// <summary>Discard only an authenticated losing sibling journal. Shared ciphertext and manifests are retained.</summary>
    public bool DiscardUnselectedSibling(VaultGenerationRecovery recovery, VaultGenerationStamp candidate)
    {
        ArgumentNullException.ThrowIfNull(recovery);
        ArgumentNullException.ThrowIfNull(candidate);
        using var fence = EnterLiveWrite();
        var state = recovery.ReadAuthorityState(candidate.WorkspaceId);
        if (state.Pending is not null || state.LastCommitted is not { } winner ||
            winner.Candidate.Epoch != candidate.Epoch || winner.Candidate == candidate)
            throw new InvalidOperationException("Cleanup requires an exact committed sibling and no pending transition.");

        string name = JournalName(candidate);
        using (var file = _journal.OpenRead(name))
            if (file is null) return false;
        if (ReadPlan(candidate).Expected != winner.Expected || recovery.ReadAuthorityState(candidate.WorkspaceId) != state)
            throw new CryptographicException("Losing journal does not match the stable committed sibling.");
        return _journal.DeleteFile(name);
    }
}
