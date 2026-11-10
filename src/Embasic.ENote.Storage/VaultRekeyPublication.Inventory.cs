using System.Security.Cryptography;
using Embasic.ENote.Emd;

namespace Embasic.ENote.Storage;

public sealed record VaultPublicationInventory(int SelectedGenerations, int RegisteredPlans,
    IReadOnlyList<string> VerifiedObjects, IReadOnlyList<string> MissingObjects,
    IReadOnlyList<string> UnclassifiedEntries);

public sealed record VaultSelectedCatalogInventory(int Documents, int Vaults, IReadOnlyList<string> UnclassifiedEntries);

public sealed partial class VaultRekeyPublication
{
    /// <summary>Report the complete top-level publication namespace against selected ancestry and every retained ERJ1 plan.</summary>
    public VaultPublicationInventory InspectReachability(VaultGenerationRecovery recovery, Guid workspace)
    {
        ArgumentNullException.ThrowIfNull(recovery);
        using var fence = EnterLiveWrite();
        var state = recovery.ReadAuthorityState(workspace);
        if (state.Pending is not null) throw new InvalidOperationException("A quiescent authority is required for inventory.");
        var entries = ReadWorkspacePublicationNames(workspace);
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
        var unclassified = entries.Where(name => !expected.ContainsKey(name)).Order(StringComparer.Ordinal).ToArray();
        if (recovery.ReadAuthorityState(workspace) != state)
            throw new CryptographicException("The independent selection changed during reachability inspection.");
        return new VaultPublicationInventory(ancestry.Count, retained.Count, verified.AsReadOnly(),
            missing.AsReadOnly(), Array.AsReadOnly(unclassified));
    }

    /// <summary>Inspect the nested contents of the exact selected encrypted snapshot without deleting unknown entries.</summary>
    public VaultSelectedCatalogInventory InspectSelectedCatalog(VaultGenerationRecovery recovery, Guid workspace, LayerSecret root)
    {
        ArgumentNullException.ThrowIfNull(recovery);
        using var fence = EnterLiveWrite();
        var state = recovery.ReadAuthorityState(workspace);
        if (state.Pending is not null) throw new InvalidOperationException("A quiescent authority is required for inspection.");
        _ = ReadWorkspacePublicationNames(workspace);
        _ = ReadRetainedPlans(workspace);
        var manifest = ReadExact(state.Current);
        if (manifest.Bindings is null) throw new InvalidDataException("The selected generation has no catalog.");
        var snapshot = manifest.Bindings.RekeySha256 is null ? OpenSourceSnapshot(manifest, root) :
            OpenRegisteredSnapshot(manifest, root) ?? throw new InvalidDataException("Selected snapshot is unavailable.");
        snapshot.VerifyAgainst(manifest, root);
        var result = snapshot.InspectEntries(manifest);
        if (recovery.ReadAuthorityState(workspace) != state)
            throw new CryptographicException("The independent selection changed during catalog inspection.");
        return result;
    }

    private string[] ReadWorkspacePublicationNames(Guid workspace)
    {
        string prefix = "catalog-" + workspace.ToString("N") + "-";
        var entries = Directory.EnumerateFileSystemEntries(_publishedPath).Select(Path.GetFileName).OfType<string>().ToArray();
        if (entries.Any(name => name.StartsWith("catalog-", StringComparison.OrdinalIgnoreCase) &&
                                !name.StartsWith(prefix, StringComparison.Ordinal)))
            throw new InvalidDataException("Publication contains a catalog from another or unidentified workspace.");
        return entries;
    }
}
