using System.Security.Cryptography;

namespace Embasic.ENote.Storage;

public sealed partial class VaultGenerationRecovery
{
    private VaultGenerationAdvanceState StageBound(IExternalVaultTransitionAnchor anchor,
        VaultRetryIntent intent, VaultGenerationManifest next)
    {
        var expected = intent.Expected;
        var state = ReadTransition(anchor, expected.WorkspaceId);
        if (state.Current != expected) return ResolveBound(intent, state);
        if (state.Pending is not null && state.Pending != intent)
            throw new CryptographicException("A different candidate is independently reserved.");

        CheckLocalIntent(intent);
        VerifyFactorContinuity(expected, next);
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
        return CommitBound(anchor, intent);
    }

    private VaultGenerationAdvanceState ResumeBound(IExternalVaultTransitionAnchor anchor,
        VaultGenerationStamp expected)
    {
        var state = ReadTransition(anchor, expected.WorkspaceId);
        if (state.Current != expected)
        {
            if (state.LastCommitted is { } receipt && receipt.Expected == expected)
                return ResolveBound(receipt, state);
            throw new CryptographicException("No matching independent commit receipt.");
        }

        var intent = state.Pending ?? throw new CryptographicException("No independently reserved retry intent.");
        CheckLocalIntent(intent);
        VerifyFactorContinuity(expected, ReadExact(intent.Candidate));
        return CommitBound(anchor, intent);
    }

    private VaultGenerationAdvanceState CommitBound(IExternalVaultTransitionAnchor anchor, VaultRetryIntent intent)
    {
        var expected = intent.Expected;
        var state = ReadTransition(anchor, expected.WorkspaceId);
        if (state.Current != expected) return ResolveBound(intent, state);
        if (state.Pending is null)
            throw new VaultGenerationOutcomeUnknownException("Independent reservation is not confirmed.");
        if (state.Pending != intent)
            throw new CryptographicException("A different candidate is independently reserved.");

        _intents?.Record(intent);
        if (_intents is not null && _intents.Read(expected.WorkspaceId, expected.Epoch) != intent)
            throw new CryptographicException("The local retry intent failed readback.");
        ReadExact(intent.Candidate);

        try { anchor.CommitTransition(intent); }
        catch (VaultGenerationOutcomeUnknownException)
        {
            // Resolve against the authoritative transition state.
        }
        return ResolveBound(intent, ReadTransition(anchor, expected.WorkspaceId));
    }

    private VaultGenerationAdvanceState ResolveBound(VaultRetryIntent intent, VaultTransitionState state)
    {
        if (state.Current == intent.Candidate && state.LastCommitted != intent)
            throw new CryptographicException("The independent commit receipt does not match the retry intent.");
        if (state.Current == intent.Expected && state.Pending != intent)
            throw new CryptographicException("The independent reservation changed before commit was confirmed.");

        return Resolve(intent.Expected, intent.Candidate, state.Current);
    }

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
