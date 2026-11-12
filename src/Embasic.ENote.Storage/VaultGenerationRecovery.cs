using System.Security.Cryptography;
using Embasic.ENote.Emd;

namespace Embasic.ENote.Storage;

/// <summary>
/// Experimental adapter contract. A real provider must perform a linearizable
/// read and atomic CAS over the full workspace, epoch and digest, independently
/// of the rollbackable local store. No real provider is shipped here.
/// </summary>
public interface IExternalVaultGenerationAnchor
{
    VaultGenerationStamp? Read(Guid workspaceId);

    // A mismatched expected stamp must leave the anchor untouched. An adapter
    // reports an uncertain outcome with VaultGenerationOutcomeUnknownException.
    void CompareAndAdvance(VaultGenerationStamp expected, VaultGenerationStamp next);
}

/// <summary>
/// Local generation storage. Stage must durably retain the exact immutable
/// generation on success, be idempotent for that stamp and preserve other
/// generations. Concurrent writers may stage competing digests independently.
/// </summary>
public interface IVaultGenerationStore
{
    VaultGenerationManifest? Read(VaultGenerationStamp stamp);
    void Stage(VaultGenerationManifest manifest);
}

public sealed class VaultGenerationOutcomeUnknownException : IOException
{
    public VaultGenerationOutcomeUnknownException(string message) : base(message) { }

    internal VaultGenerationOutcomeUnknownException(string message, Exception cause) : base(message, cause) { }
}

public enum VaultGenerationAdvanceState
{
    Committed,
    // Observation only. An uncertain outstanding write may still commit.
    PriorObserved,
    Superseded
}

/// <summary>
/// Experimental restart selection and stage-before-CAS protocol. The
/// coordinator provides neither anchor trust nor a durable store.
/// </summary>
public sealed partial class VaultGenerationRecovery : IExternalVaultAnchorReader
{
    private readonly IExternalVaultGenerationAnchor _anchor;
    private readonly IVaultGenerationStore _store;
    private readonly IVaultRetryIntentStore? _intents;

    public VaultGenerationRecovery(IExternalVaultGenerationAnchor anchor, IVaultGenerationStore store)
        : this(anchor, store, null) { }

    public VaultGenerationRecovery(IExternalVaultGenerationAnchor anchor, IVaultGenerationStore store,
        IVaultRetryIntentStore? intents)
    {
        _anchor = anchor ?? throw new ArgumentNullException(nameof(anchor));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _intents = intents;
    }

    public VaultGenerationManifest ReadCommitted(Guid workspaceId)
    {
        var selected = ReadStamp(workspaceId);
        var manifest = ReadExact(selected);
        if (ReadStamp(workspaceId) != selected)
            throw new CryptographicException("The trusted generation changed while loading its manifest.");
        return manifest;
    }

    public bool TryRead(Guid workspaceId, Guid lockId, LayerKind kind, out VaultAnchorGrant? grant)
    {
        grant = ReadCommitted(workspaceId).Find(lockId, kind);
        return grant is not null;
    }

    /// <summary>
    /// Observe the exact outcome without rolling back or deleting staged data.
    /// A retry must use the same expected stamp and the same candidate.
    /// </summary>
    public VaultGenerationAdvanceState StageAndAdvance(VaultGenerationStamp expected, VaultGenerationManifest next)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(next);
        if (next.Bindings is not null)
            throw new CryptographicException("Catalog-bound candidates require the bound rekey entry point.");
        var intent = new VaultRetryIntent(expected, next.Stamp);
        if (_anchor is IExternalVaultTransitionAnchor transition)
            return StageBound(transition, intent, next);

        CheckLocalIntent(intent);

        var observed = ReadStamp(expected.WorkspaceId);
        if (observed != expected) return Resolve(expected, next.Stamp, observed);

        VerifyFactorContinuity(expected, next);
        _store.Stage(next);
        ReadExact(next.Stamp);
        _intents?.Record(intent);
        return Advance(intent);
    }

    /// <summary>Resume only an authenticated, already recorded transition.</summary>
    public VaultGenerationAdvanceState Resume(VaultGenerationStamp expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (_anchor is IExternalVaultTransitionAnchor transition)
            return ResumeBound(transition, expected);

        var intent = _intents?.Read(expected.WorkspaceId, expected.Epoch);
        if (intent is null || intent.Expected != expected)
            throw new CryptographicException("No matching retry intent for this generation.");

        var observed = ReadStamp(expected.WorkspaceId);
        if (observed != expected) return Resolve(expected, intent.Candidate, observed);

        VerifyFactorContinuity(expected, ReadExact(intent.Candidate));
        return Advance(intent);
    }

    internal VaultGenerationManifest ReadForKeyDelivery(Guid workspaceId)
    {
        if (_anchor is not IExternalVaultTransitionAnchor transition)
            return ReadCommitted(workspaceId);

        var state = ReadTransition(transition, workspaceId);
        if (state.Pending is not null)
            throw new CryptographicException("A trusted transition is pending.");

        var manifest = ReadExact(state.Current);
        RequireCurrent(state.Current);
        return manifest;
    }

    internal void RequireCurrent(VaultGenerationStamp expected)
    {
        if (_anchor is IExternalVaultTransitionAnchor transition)
        {
            var state = ReadTransition(transition, expected.WorkspaceId);
            if (state.Current == expected && state.Pending is null) return;
        }
        else if (ReadStamp(expected.WorkspaceId) == expected) return;

        throw new CryptographicException("The trusted generation changed or a transition is pending before key delivery.");
    }

    private void CheckLocalIntent(VaultRetryIntent intent)
    {
        if (_intents?.Read(intent.Expected.WorkspaceId, intent.Expected.Epoch) is { } existing && existing != intent)
            throw new CryptographicException("The candidate differs from the recorded retry intent.");
    }

    private void VerifyFactorContinuity(VaultGenerationStamp expected, VaultGenerationManifest candidate,
        bool boundRekey = false)
    {
        var prior = ReadExact(expected);
        if (!boundRekey && (prior.Bindings is not null || candidate.Bindings is not null))
            throw new CryptographicException("A catalog-bound generation requires a verified rekey commitment.");
        if (!candidate.RetainsFactorsFrom(prior))
            throw new ArgumentException("Candidate dropped an enrolled factor.", nameof(candidate));
    }

    private VaultGenerationAdvanceState Advance(VaultRetryIntent intent)
    {
        var expected = intent.Expected;
        var candidate = intent.Candidate;
        if (_intents is not null && _intents.Read(expected.WorkspaceId, expected.Epoch) != intent)
            throw new CryptographicException("The retry intent is missing or changed before compare-and-advance.");

        try { _anchor.CompareAndAdvance(expected, candidate); }
        catch (VaultGenerationOutcomeUnknownException)
        {
            // A lost reply cannot prove success or failure. Consult the anchor.
        }
        return Resolve(expected, candidate, ReadStamp(expected.WorkspaceId));
    }

    private VaultGenerationStamp ReadStamp(Guid workspaceId)
    {
        if (workspaceId == Guid.Empty) throw new ArgumentException("Missing workspace.", nameof(workspaceId));
        if (_anchor is IExternalVaultTransitionAnchor transition)
            return ReadTransition(transition, workspaceId).Current;

        var stamp = _anchor.Read(workspaceId);
        if (stamp is null || stamp.WorkspaceId != workspaceId)
            throw new CryptographicException("Trusted generation anchor is absent or mismatched.");
        return stamp;
    }

    private VaultGenerationManifest ReadExact(VaultGenerationStamp stamp)
    {
        var manifest = _store.Read(stamp);
        if (manifest is null || manifest.Stamp != stamp)
            throw new CryptographicException("The committed or staged manifest is unavailable or mismatched.");
        return manifest;
    }

    private VaultGenerationAdvanceState Resolve(
        VaultGenerationStamp expected, VaultGenerationStamp next, VaultGenerationStamp observed)
    {
        if (observed.Epoch <= expected.Epoch && observed != expected)
            throw new CryptographicException("Anchor moved backwards or changed the expected epoch digest.");

        ReadExact(observed);
        if (observed == next) return VaultGenerationAdvanceState.Committed;
        if (observed == expected) return VaultGenerationAdvanceState.PriorObserved;
        return VaultGenerationAdvanceState.Superseded;
    }
}
