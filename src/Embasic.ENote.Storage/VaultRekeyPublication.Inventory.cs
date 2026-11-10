using System.Security.Cryptography;

namespace Embasic.ENote.Storage;

public sealed record VaultPublicationInventory(int SelectedGenerations, int RegisteredPlans,
    IReadOnlyList<string> VerifiedObjects, IReadOnlyList<string> MissingObjects,
    IReadOnlyList<string> UnclassifiedEntries);

public sealed partial class VaultRekeyPublication
{
    /// <summary>Report the complete top-level publication namespace against selected ancestry and every retained ERJ1 plan.</summary>
    public VaultPublicationInventory InspectReachability(VaultGenerationRecovery recovery, Guid workspace)
    {
        ArgumentNullException.ThrowIfNull(recovery);
        using var fence = EnterLiveWrite();
        var state = recovery.ReadAuthorityState(workspace);
        if (state.Pending is not null) throw new InvalidOperationException("A quiescent authority is required for inventory.");
        var retained = ReadRetainedPlans(workspace);
        var ancestry = ReadSelectedAncestry(state.Current, retained);
        var expected = new Dictionary<string, (VaultRekeyArtifact Item, string Digest)>(StringComparer.Ordinal);
        foreach (var (stamp, plan) in retained)
        {
            _ = ReadExact(stamp);
            _ = ReadExact(plan.Expected);
            foreach (var (id, digest) in plan.Targets)
            {
                string name = ObjectName(id, digest);
                var kind = Path.GetExtension(name) switch
                {
                    ".elh" => VaultRekeyArtifactKind.RootIndex,
                    ".elkey" => VaultRekeyArtifactKind.FactorVault,
                    _ => VaultRekeyArtifactKind.Document
                };
                expected.TryAdd(name, (new(id, kind, null, digest, []), digest));
            }
        }

        var verified = new List<string>();
        var missing = new List<string>();
        foreach (var (name, target) in expected.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (VerifyObject(target.Item, target.Digest)) verified.Add(name);
            else missing.Add(name);
        }

        // A catalog directory is deliberately left unclassified. Only a root secret can authenticate
        // its nested ELH1 and ciphertext set. An unclassified entry is never a deletion instruction.
        var unclassified = Directory.EnumerateFileSystemEntries(_publishedPath).Select(Path.GetFileName)
            .OfType<string>().Where(name => !expected.ContainsKey(name)).Order(StringComparer.Ordinal).ToArray();
        if (recovery.ReadAuthorityState(workspace) != state)
            throw new CryptographicException("The independent selection changed during reachability inspection.");
        return new VaultPublicationInventory(ancestry.Count, retained.Count, verified.AsReadOnly(),
            missing.AsReadOnly(), Array.AsReadOnly(unclassified));
    }
}
