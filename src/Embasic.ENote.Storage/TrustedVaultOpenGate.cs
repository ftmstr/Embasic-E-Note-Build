using System.Security.Cryptography;
using Embasic.ENote.Emd;

namespace Embasic.ENote.Storage;

/// <summary>
/// Experimental contract for independently trusted, rollback-resistant
/// policy. There is NO implementation of such a trust anchor in this repo.
/// An ordinary local file, memory object, DPAPI blob or cloned disk does
/// NOT establish a monotonic anti-rollback guarantee.
/// </summary>
public interface IExternalVaultAnchorReader
{
    bool TryRead(Guid workspaceId, Guid lockId, LayerKind kind,
        out VaultAnchorGrant? grant);
}

/// <summary>
/// A caller-provided, NOT SELF-AUTHENTICATING policy claim.
/// It must only be treated as trusted if the reader is backed by an
/// independently reviewed rollback-resistant source.
/// </summary>
public sealed record VaultAnchorGrant(
    Guid WorkspaceId, Guid LockId, LayerKind Kind,
    VaultSnapshotPin Pin, long MinimumEpoch);

/// <summary>
/// Fail-closed entry point when an external trust anchor is absent,
/// unavailable, or inconsistent. Proof of implementation is NOT proof
/// that the external reader is trusted or that old ELV1 backups are
/// cryptographically revoked. Historical unguarded APIs remain available.
/// </summary>
public sealed class TrustedVaultOpenGate
{
    private readonly IExternalVaultAnchorReader? _anchor;
    private readonly LayerVaultDiskStore _disk;

    public TrustedVaultOpenGate(IExternalVaultAnchorReader? anchor,
        LayerVaultDiskStore? disk = null)
    {
        _anchor = anchor;
        _disk = disk ?? new LayerVaultDiskStore();
    }

    public LayerSecret Unlock(string path, Guid workspaceId, Guid lockId,
        LayerKind kind, string credential, bool recovery = false)
    {
        if (workspaceId == Guid.Empty || lockId == Guid.Empty)
            throw new CryptographicException("Missing vault policy identity.");
        if (_anchor is null ||
            !_anchor.TryRead(workspaceId, lockId, kind, out var grant) ||
            grant is null)
            throw new CryptographicException("No independently trusted vault policy.");
        if (grant.WorkspaceId != workspaceId || grant.LockId != lockId ||
            grant.Kind != kind || grant.Pin is null ||
            grant.Pin.LockId != lockId || grant.Pin.LockKind != kind ||
            grant.MinimumEpoch <= 0 ||
            grant.Pin.TrustedEpoch < grant.MinimumEpoch)
            throw new CryptographicException("Untrusted or stale vault policy.");
        return _disk.UnlockPinned(path, grant.Pin, credential,
            grant.MinimumEpoch, recovery);
    }
}
