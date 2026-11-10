using System.Security.Cryptography;

namespace Embasic.ENote.Storage;

public sealed partial class VaultGenerationRecovery
{
    /// <summary>Restore only the exact already reserved candidate. This never commits or cancels.</summary>
    public void RestageReservedCandidate(VaultGenerationStamp expected, VaultGenerationManifest backup)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(backup);
        var intent = new VaultRetryIntent(expected, backup.Stamp);
        if (_anchor is not IExternalVaultTransitionAnchor anchor)
            throw new NotSupportedException("An independent transition provider is required.");
        RequireReserved();
        CheckLocalIntent(intent);
        VerifyFactorContinuity(expected, backup);
        _store.Stage(backup);
        ReadExact(intent.Candidate);
        RequireReserved();

        void RequireReserved()
        {
            var state = ReadTransition(anchor, expected.WorkspaceId);
            if (state.Current != expected || state.Pending != intent)
                throw new CryptographicException("Backup is not the independently reserved candidate.");
        }
    }

    internal IDisposable EnterKeyUseFence(VaultGenerationStamp expected)
    {
        if (_anchor is not IExternalVaultTransitionAnchor || _anchor is not IVaultKeyUseFence fence)
            throw new NotSupportedException("A transition-aware key use fence is required.");

        return fence.EnterKeyUse(expected)
            ?? throw new CryptographicException("Provider did not establish an independent key use fence.");
    }

    /// <summary>Enroll the first verified workspace catalog with the exact prior vault grants.</summary>
    public VaultGenerationAdvanceState EnrollCatalog(VaultGenerationStamp expected, VaultWorkspaceSnapshot snapshot,
        Embasic.ENote.Emd.LayerSecret root, string liveRoot)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(root);
        if (_anchor is not IExternalVaultTransitionAnchor anchor)
            throw new NotSupportedException("An independent transition provider is required.");
        var source = ReadExact(expected);
        if (source.Bindings is not null) throw new CryptographicException("The catalog is already enrolled.");
        long epoch = checked(expected.Epoch + 1);
        var grants = source.Grants.Select(grant => grant with
        {
            MinimumEpoch = epoch,
            Pin = Embasic.ENote.Emd.VaultSnapshotPin.RestoreVerifiedDigest(grant.LockId, grant.Kind, epoch,
                Convert.FromHexString(grant.Pin.CiphertextSha256))
        });
        var next = VaultGenerationManifest.Create(expected.WorkspaceId, epoch, grants,
            new VaultGenerationBindings(snapshot.CatalogSha256));
        return StageBound(anchor, new VaultRetryIntent(expected, next.Stamp), next, () =>
        {
            snapshot.VerifyAgainst(source, root);
            snapshot.VerifyLive(liveRoot, source);
        });
    }

    /// <summary>Prepare or retry only this catalog-bound candidate with the same complete staged set.</summary>
    public VaultGenerationAdvanceState StageAndAdvanceRekey(VaultRekeyCommitment commitment,
        VaultGenerationManifest next, VaultRekeyStaging staging, Func<VaultRekeyArtifact, Stream?> openSource) =>
        StageAndAdvanceRekey(commitment, next, staging, openSource, null);

    internal VaultGenerationAdvanceState StageAndAdvanceRekey(VaultRekeyCommitment commitment,
        VaultGenerationManifest next, VaultRekeyStaging staging, Func<VaultRekeyArtifact, Stream?> openSource,
        Action? verifyPublication)
    {
        ArgumentNullException.ThrowIfNull(commitment);
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(staging);
        ArgumentNullException.ThrowIfNull(openSource);
        if (_anchor is not IExternalVaultTransitionAnchor anchor)
            throw new NotSupportedException("An independent transition provider is required.");

        var expected = commitment.Inventory.Generation;
        var intent = new VaultRetryIntent(expected, next.Stamp);
        commitment.RequireMatch(ReadExact(expected), next);
        return StageBound(anchor, intent, next, () =>
        {
            staging.RequirePrepared(commitment.Inventory, commitment.Targets, openSource);
            verifyPublication?.Invoke();
        });
    }

    private VaultGenerationAdvanceState StageBound(IExternalVaultTransitionAnchor anchor,
        VaultRetryIntent intent, VaultGenerationManifest next, Action? verifyPrepared = null)
    {
        var expected = intent.Expected;
        var state = ReadTransition(anchor, expected.WorkspaceId);
        if (state.Current != expected) return ResolveBound(intent, state, verifyPrepared);
        if (state.Pending is not null && state.Pending != intent)
            throw new CryptographicException("A different candidate is independently reserved.");

        CheckLocalIntent(intent);
        VerifyFactorContinuity(expected, next, boundRekey: verifyPrepared is not null);
        verifyPrepared?.Invoke();
        _store.Stage(next);
        ReadExact(intent.Candidate);

        if (state.Pending is null)
        {
            try { anchor.ReserveTransition(intent); }
            catch (VaultGenerationOutcomeUnknownException)
            {
                // Only exact independent readback can authorize a commit.
            }
        }
        return CommitBound(anchor, intent, verifyPrepared);
    }

    private VaultGenerationAdvanceState ResumeBound(IExternalVaultTransitionAnchor anchor,
        VaultGenerationStamp expected)
    {
        var state = ReadTransition(anchor, expected.WorkspaceId);
        if (state.Current != expected)
        {
            if (state.LastCommitted is { } receipt && receipt.Expected == expected)
            {
                if (ReadExact(receipt.Candidate).Bindings is not null)
                    throw new CryptographicException("Catalog-bound receipts require the bound rekey entry point.");
                return ResolveBound(receipt, state);
            }
            throw new CryptographicException("No matching independent commit receipt.");
        }

        var intent = state.Pending ?? throw new CryptographicException("No independently reserved retry intent.");
        CheckLocalIntent(intent);
        VerifyFactorContinuity(expected, ReadExact(intent.Candidate));
        return CommitBound(anchor, intent);
    }

    private VaultGenerationAdvanceState CommitBound(IExternalVaultTransitionAnchor anchor, VaultRetryIntent intent,
        Action? verifyPrepared = null)
    {
        var expected = intent.Expected;
        var state = ReadTransition(anchor, expected.WorkspaceId);
        if (state.Current != expected) return ResolveBound(intent, state, verifyPrepared);
        if (state.Pending is null)
            throw new VaultGenerationOutcomeUnknownException("Independent reservation is not confirmed.");
        if (state.Pending != intent)
            throw new CryptographicException("A different candidate is independently reserved.");

        _intents?.Record(intent);
        if (_intents is not null && _intents.Read(expected.WorkspaceId, expected.Epoch) != intent)
            throw new CryptographicException("The local retry intent failed readback.");
        ReadExact(intent.Candidate);
        verifyPrepared?.Invoke();

        try { anchor.CommitTransition(intent); }
        catch (VaultGenerationOutcomeUnknownException)
        {
            // Resolve against the authoritative transition state.
        }
        return ResolveBound(intent, ReadTransition(anchor, expected.WorkspaceId), verifyPrepared);
    }

    private VaultGenerationAdvanceState ResolveBound(VaultRetryIntent intent, VaultTransitionState state,
        Action? verifyPrepared = null)
    {
        if (state.Current == intent.Candidate && state.LastCommitted != intent)
            throw new CryptographicException("The independent commit receipt does not match the retry intent.");
        if (state.Current == intent.Expected && state.Pending != intent)
            throw new CryptographicException("The independent reservation changed before commit was confirmed.");

        var result = Resolve(intent.Expected, intent.Candidate, state.Current);
        if (result == VaultGenerationAdvanceState.Committed) verifyPrepared?.Invoke();
        return result;
    }

    internal VaultTransitionState ReadAuthorityState(Guid workspace) =>
        _anchor is IExternalVaultTransitionAnchor transition
            ? ReadTransition(transition, workspace)
            : throw new NotSupportedException("An independent transition anchor is required.");

    private static VaultTransitionState ReadTransition(IExternalVaultTransitionAnchor anchor, Guid workspaceId)
    {
        if (workspaceId == Guid.Empty) throw new ArgumentException("Missing workspace.", nameof(workspaceId));
        var state = anchor.ReadTransition(workspaceId);
        if (state?.Current is not { } current || current.WorkspaceId != workspaceId ||
            (state.Pending is { } pending && pending.Expected != current) ||
            (state.LastCommitted is { } committed && committed.Candidate != current))
            throw new CryptographicException("Independent transition state is missing or inconsistent.");

        return state;
    }
}
