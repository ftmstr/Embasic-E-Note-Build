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

public sealed class VaultGenerationOutcomeUnknownException(string message) : IOException(message);

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
public sealed class VaultGenerationRecovery : IExternalVaultAnchorReader
{
    private readonly IExternalVaultGenerationAnchor _anchor;
    private readonly IVaultGenerationStore _store;

    public VaultGenerationRecovery(IExternalVaultGenerationAnchor anchor, IVaultGenerationStore store)
    {
        _anchor = anchor ?? throw new ArgumentNullException(nameof(anchor));
        _store = store ?? throw new ArgumentNullException(nameof(store));
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
        if (expected.WorkspaceId != next.Stamp.WorkspaceId || expected.Epoch == long.MaxValue ||
            next.Stamp.Epoch != expected.Epoch + 1)
            throw new ArgumentException("Candidate must be the next epoch of the same workspace.", nameof(next));

        var observed = ReadStamp(expected.WorkspaceId);
        if (observed != expected) return Resolve(expected, next.Stamp, observed);

        var prior = ReadExact(expected);
        if (!next.RetainsFactorsFrom(prior))
            throw new ArgumentException("Candidate dropped an enrolled factor.", nameof(next));

        _store.Stage(next);
        ReadExact(next.Stamp);

        try { _anchor.CompareAndAdvance(expected, next.Stamp); }
        catch (VaultGenerationOutcomeUnknownException)
        {
            // A lost reply cannot prove success or failure. Consult the anchor.
        }
        return Resolve(expected, next.Stamp, ReadStamp(expected.WorkspaceId));
    }

    private VaultGenerationStamp ReadStamp(Guid workspaceId)
    {
        if (workspaceId == Guid.Empty) throw new ArgumentException("Missing workspace.", nameof(workspaceId));
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
