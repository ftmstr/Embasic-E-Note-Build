namespace Embasic.ENote.Storage;

/// <summary>The one permitted candidate for retrying a generation advance.</summary>
public sealed record VaultRetryIntent
{
    public VaultGenerationStamp Expected { get; }
    public VaultGenerationStamp Candidate { get; }

    public VaultRetryIntent(VaultGenerationStamp expected, VaultGenerationStamp candidate)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(candidate);
        if (expected.WorkspaceId != candidate.WorkspaceId || expected.Epoch == long.MaxValue ||
            candidate.Epoch != expected.Epoch + 1)
            throw new ArgumentException("Candidate must be the next epoch of the same workspace.", nameof(candidate));

        Expected = expected;
        Candidate = candidate;
    }
}

/// <summary>
/// A configured journal must authenticate records and atomically reserve one
/// immutable candidate per workspace and expected epoch.
/// </summary>
public interface IVaultRetryIntentStore
{
    VaultRetryIntent? Read(Guid workspaceId, long expectedEpoch);
    void Record(VaultRetryIntent intent);
}
