namespace Embasic.ENote.Storage;

public sealed record VaultRootRecoveryAuthorization(long Revision, VaultRootRecoveryProof? Proof);

/// <summary>
/// The provider must serialize these operations against generation changes.
/// Each update consumes an exact revision, including a revocation to a null proof.
/// </summary>
public interface IVaultRootRecoveryLifecycle : IExternalVaultRootRecoveryAuthority
{
    VaultRootRecoveryAuthorization ReadAuthorization(Guid workspaceId);

    VaultRootRecoveryAuthorization CompareAndSetAuthorization(VaultGenerationStamp expected,
        long expectedRevision, VaultRootRecoveryProof? replacement);

    VaultRootRecoveryProof? IExternalVaultRootRecoveryAuthority.ReadRootRecovery(Guid workspaceId) =>
        ReadAuthorization(workspaceId).Proof;
}
