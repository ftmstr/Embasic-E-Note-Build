using System.Security.Cryptography;

namespace Embasic.ENote.Storage;

public sealed record VaultRootRecoveryAuthorization(long Revision, VaultRootRecoveryProof? Proof);

/// <summary>
/// The provider must serialize these operations against generation changes.
/// Each update consumes an exact revision, including a revocation to a null proof.
/// </summary>
public interface IVaultRootRecoveryLifecycle : IExternalVaultRootRecoveryAuthority
{
    VaultRootRecoveryAuthorization ReadAuthorization(Guid workspaceId);

    /// <summary>Read the revision only while the exact generation is selected and quiescent.</summary>
    VaultRootRecoveryAuthorization ObserveAuthorization(VaultGenerationStamp expected) =>
        throw new NotSupportedException("An atomic generation-bound authorization observation is required.");

    VaultRootRecoveryAuthorization CompareAndSetAuthorization(VaultGenerationStamp expected,
        long expectedRevision, VaultRootRecoveryProof? replacement);

    VaultRootRecoveryProof? IExternalVaultRootRecoveryAuthority.ReadRootRecovery(Guid workspaceId) =>
        ReadAuthorization(workspaceId).Proof;
}

public enum VaultRootRecoveryUpdateState { Applied, PriorObserved }

public static class VaultRootRecoveryUpdates
{
    /// <summary>Resolve an ambiguous acknowledgement without retrying or accepting another generation.</summary>
    public static VaultRootRecoveryUpdateState Change(IVaultRootRecoveryLifecycle authority,
        VaultGenerationStamp expected, long expectedRevision, VaultRootRecoveryProof? replacement)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedRevision);
        if (replacement is not null && replacement.Generation != expected)
            throw new ArgumentException("Recovery proof must match the selected generation.", nameof(replacement));
        long nextRevision = checked(expectedRevision + 1);
        VaultRootRecoveryAuthorization observed;
        try { observed = authority.CompareAndSetAuthorization(expected, expectedRevision, replacement); }
        catch (VaultGenerationOutcomeUnknownException)
        {
            observed = authority.ObserveAuthorization(expected);
            if (observed?.Revision == expectedRevision) return VaultRootRecoveryUpdateState.PriorObserved;
        }
        if (observed?.Revision != nextRevision || observed.Proof != replacement)
            throw new CryptographicException("Recovery authorization outcome does not match the requested update.");
        return VaultRootRecoveryUpdateState.Applied;
    }
}
