using System.Collections.ObjectModel;
using System.Security.Cryptography;

namespace Embasic.ENote.Storage;

/// <summary>Immutable affected set, target digests and resulting full catalog commitment.</summary>
public sealed class VaultRekeyCommitment
{
    public VaultRekeyInventory Inventory { get; }
    public IReadOnlyDictionary<string, string> Targets { get; }
    public VaultGenerationBindings Destination { get; }

    public VaultRekeyCommitment(VaultRekeyInventory inventory, IReadOnlyDictionary<string, string> targets)
    {
        var exact = VaultRekeyStaging.ValidateTargets(inventory, targets);
        Inventory = inventory;
        Targets = new ReadOnlyDictionary<string, string>(exact);
        string nextIndex = exact.GetValueOrDefault("index", inventory.IndexSha256);
        var documents = inventory.Documents.Select(doc =>
            doc with { SourceSha256 = exact.GetValueOrDefault("note:" + doc.RelativePath, doc.SourceSha256) });
        string nextCatalog = VaultRekeyInventory.ComputeCatalogSha256(inventory.Generation.WorkspaceId,
            inventory.RootFactorId, nextIndex, documents);
        string rekey = VaultRekeyInventory.HashCanonical(writer =>
        {
            writer.Write("Embasic.ENote.RekeyCommitment.1");
            var expected = inventory.Generation;
            writer.Write(expected.WorkspaceId.ToByteArray());
            writer.Write(expected.Epoch);
            writer.Write(Convert.FromHexString(expected.ManifestSha256));
            writer.Write(Convert.FromHexString(inventory.CatalogSha256));
            writer.Write(Convert.FromHexString(nextCatalog));
            writer.Write(inventory.Artifacts.Count);
            foreach (var item in inventory.Artifacts)
            {
                writer.Write(item.Id);
                writer.Write((byte)item.Kind);
                writer.Write(Convert.FromHexString(item.SourceSha256));
                writer.Write(item.RequiredFactors.Count);
                foreach (Guid id in item.RequiredFactors) writer.Write(id.ToByteArray());
                writer.Write(Convert.FromHexString(exact[item.Id]));
            }
        });
        Destination = new VaultGenerationBindings(nextCatalog, rekey);
    }

    internal void RequireMatch(VaultGenerationManifest source, VaultGenerationManifest next)
    {
        if (source.Stamp != Inventory.Generation || source.Bindings?.CatalogSha256 != Inventory.CatalogSha256 ||
            next.Bindings != Destination || source.Grants.Count() != next.Grants.Count())
            throw new CryptographicException("Rekey commitment does not match the source or candidate generation.");

        foreach (var previous in source.Grants)
        {
            bool changed = Targets.TryGetValue("vault:" + previous.LockId.ToString("N"), out string? target);
            var candidate = next.Find(previous.LockId, previous.Kind);
            if (candidate is null || candidate.Pin.CiphertextSha256 != (target ?? previous.Pin.CiphertextSha256) ||
                (!changed && (candidate.BoundVaultPath != previous.BoundVaultPath ||
                    candidate.BoundVaultFileIdentity != previous.BoundVaultFileIdentity)))
                throw new CryptographicException("A candidate vault pin or unchanged grant differs from the rekey commitment.");
        }
    }
}
