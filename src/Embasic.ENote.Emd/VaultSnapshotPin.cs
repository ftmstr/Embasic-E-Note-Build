using System.IO;
using System.Security.Cryptography;

namespace Embasic.ENote.Emd;

/// <summary>
/// EXPERIMENTAL: authenticated snapshot pin for a specific ELV1 vault.
/// This is only effective while BOTH this pin and the minimum trusted epoch
/// are supplied from storage the attacker CANNOT replace or roll back.
/// The ELV1 format has no authenticated monotonic revision; no trusted anchor
/// or OS-level protection is implemented by this class. Direct LayerKeyVault.Open
/// and historical offline copies remain available. NOT an anti-rollback solution.
/// </summary>
public sealed class VaultSnapshotPin
{
    private readonly byte[] _digest;
    public Guid LockId { get; }
    public LayerKind LockKind { get; }
    public long TrustedEpoch { get; }

    private VaultSnapshotPin(Guid id, LayerKind kind, long epoch, byte[] digest)
    {
        LockId = id;
        LockKind = kind;
        TrustedEpoch = epoch;
        _digest = digest;
    }

    /// <summary>
    /// Authenticate the envelope before recording a digest. The epoch is a
    /// caller assertion, NOT verified by ELV1; securely persisting and
    /// advancing it is a separate, unresolved requirement.
    /// </summary>
    public static VaultSnapshotPin CaptureAuthenticated(ReadOnlySpan<byte> vault,
        Guid lockId, LayerKind kind, string credential, long trustedEpoch,
        bool recovery = false)
    {
        if (trustedEpoch <= 0) throw new ArgumentOutOfRangeException(nameof(trustedEpoch));
        using var factor = LayerKeyVault.Open(vault, lockId, kind, credential, recovery);
        return new VaultSnapshotPin(lockId, kind, trustedEpoch, SHA256.HashData(vault));
    }

    /// <summary>
    /// Refuse any different encrypted snapshot BEFORE password derivation or
    /// key extraction. This protects a pinned call path only; an attacker who
    /// can replace the trusted pin/floor or invoke legacy Open can bypass it.
    /// </summary>
    public LayerSecret OpenPinned(ReadOnlySpan<byte> vault, string credential,
        long trustedMinimumEpoch, bool recovery = false)
    {
        if (trustedMinimumEpoch <= 0)
            throw new ArgumentOutOfRangeException(nameof(trustedMinimumEpoch));
        if (TrustedEpoch < trustedMinimumEpoch)
            throw new CryptographicException("Layer vault snapshot epoch is below trusted minimum.");
        if (vault.Length != LayerKeyVault.Size)
            throw new InvalidDataException("Unexpected layer vault snapshot size.");
        byte[] actual = SHA256.HashData(vault);
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(actual, _digest))
                throw new CryptographicException("Layer vault snapshot mismatch or rollback.");
        }
        finally { CryptographicOperations.ZeroMemory(actual); }
        return LayerKeyVault.Open(vault, LockId, LockKind, credential, recovery);
    }
}
