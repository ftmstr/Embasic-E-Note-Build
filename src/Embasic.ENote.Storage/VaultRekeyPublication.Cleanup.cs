using System.Security.Cryptography;
using Embasic.ENote.Emd;

namespace Embasic.ENote.Storage;

public sealed partial class VaultRekeyPublication
{
    /// <summary>Prune only a losing sibling's unreferenced note and index objects in a workspace-private store.</summary>
    public int PruneUnreachableSiblingObjects(VaultGenerationRecovery recovery, VaultGenerationStamp candidate,
        LayerSecret sourceRoot)
    {
        ArgumentNullException.ThrowIfNull(recovery);
        ArgumentNullException.ThrowIfNull(candidate);
        using var fence = EnterLiveWrite();
        var state = recovery.ReadAuthorityState(candidate.WorkspaceId);
        if (state.Pending is not null || state.LastCommitted is not { } winner || state.Current != winner.Candidate ||
            winner.Candidate.Epoch != candidate.Epoch || winner.Candidate == candidate)
            throw new InvalidOperationException("Pruning requires a committed sibling and no pending transition.");
        var (source, _, loser) = ReadPublicationPlan(candidate, sourceRoot);
        if (source.Stamp != winner.Expected)
            throw new CryptographicException("Losing publication has a different parent.");
        loser.RequireMatch(source, ReadExact(candidate));

        var retained = ReadRetainedPlans(candidate);
        var ancestor = ReadExact(winner.Candidate);
        if (!retained.TryGetValue(ancestor.Stamp, out var winningPlan) || winningPlan.Expected != winner.Expected)
            throw new CryptographicException("The independently selected receipt is missing its exact plan.");
        while (ancestor.Bindings?.RekeySha256 is not null)
        {
            if (!retained.TryGetValue(ancestor.Stamp, out var plan))
                throw new CryptographicException("Cannot establish complete selected ancestry for pruning.");
            ancestor = ReadExact(plan.Expected);
        }
        var reachable = retained.Values.SelectMany(plan => plan.Targets)
            .Select(target => ObjectName(target.Key, target.Value)).ToHashSet(StringComparer.Ordinal);
        var deletable = loser.Inventory.Artifacts.Where(item => item.Kind != VaultRekeyArtifactKind.FactorVault)
            .Select(item => (Item: item, Digest: loser.Targets[item.Id]))
            .Where(target => !reachable.Contains(NameFor(target.Item, target.Digest))).ToArray();
        foreach (var target in deletable) _ = VerifyObject(target.Item, target.Digest);

        int removed = 0;
        foreach (var target in deletable)
        {
            if (recovery.ReadAuthorityState(candidate.WorkspaceId) != state)
                throw new CryptographicException("The independent selection changed during pruning.");
            if (_published.DeleteFile(NameFor(target.Item, target.Digest))) removed++;
        }
        return removed;
    }

    private Dictionary<VaultGenerationStamp, Plan> ReadRetainedPlans(VaultGenerationStamp excluded)
    {
        var plans = new Dictionary<VaultGenerationStamp, Plan>();
        foreach (string path in Directory.EnumerateFiles(_journalPath, "*.erj"))
        {
            string name = Path.GetFileName(path);
            using var file = _journal.OpenRead(name) ?? throw new IOException("A retained restart plan disappeared.");
            byte[] bytes = ReadBounded(file);
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = new BinaryReader(stream);
            _ = reader.ReadBytes(4);
            var workspace = new Guid(reader.ReadBytes(16));
            // This API assumes a dedicated workspace store. Refuse mixed storage even if its MAC verifies.
            if (workspace != excluded.WorkspaceId)
                throw new InvalidDataException("Pruning requires a workspace-private journal directory.");
            _ = ReadStamp(reader, workspace);
            var stamp = ReadStamp(reader, workspace);
            var plan = DecodePlan(stamp, bytes);
            if (!string.Equals(name, JournalName(stamp), StringComparison.Ordinal))
                throw new InvalidDataException("Restart plan is stored under an unexpected name.");
            if (stamp != excluded) plans.Add(stamp, plan);
        }
        return plans;
    }

    private static string ObjectName(string id, string digest) => digest + (id switch
    {
        "index" => ".elh",
        _ when id.StartsWith("note:", StringComparison.Ordinal) => ".emd",
        _ when id.StartsWith("vault:", StringComparison.Ordinal) => ".elkey",
        _ => throw new InvalidDataException("Unknown restart plan artifact.")
    });
}
