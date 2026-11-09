namespace Embasic.ENote.Storage;

/// <summary>One atomically readable workspace state, including the latest commit receipt.</summary>
public sealed record VaultTransitionState(
    VaultGenerationStamp Current, VaultRetryIntent? Pending = null, VaultRetryIntent? LastCommitted = null);

/// <summary>
/// A trusted provider must protect this whole state independently of local
/// manifest and journal storage. Reserve must only fill an empty pending
/// slot for the exact current stamp. Commit must match that reservation,
/// advance the current stamp, clear pending and record the receipt atomically.
/// Implementations must reject unbound CompareAndAdvance. No real provider
/// is shipped by this prototype.
/// </summary>
public interface IExternalVaultTransitionAnchor : IExternalVaultGenerationAnchor
{
    VaultTransitionState? ReadTransition(Guid workspaceId);
    void ReserveTransition(VaultRetryIntent intent);
    void CommitTransition(VaultRetryIntent intent);

    VaultGenerationStamp? IExternalVaultGenerationAnchor.Read(Guid workspaceId) =>
        ReadTransition(workspaceId)?.Current;

    void IExternalVaultGenerationAnchor.CompareAndAdvance(VaultGenerationStamp expected, VaultGenerationStamp next) =>
        throw new NotSupportedException("Transition-aware providers require an independent reservation.");
}
