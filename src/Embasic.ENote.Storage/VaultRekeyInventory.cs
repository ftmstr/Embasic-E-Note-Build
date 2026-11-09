using System.Security.Cryptography;
using Embasic.ENote.Emd;

namespace Embasic.ENote.Storage;

public enum VaultRekeyArtifactKind { RootIndex, FactorVault, Document }

public sealed record VaultRekeyDocument(string RelativePath, string SourceSha256);

public sealed record VaultRekeyArtifact(string Id, VaultRekeyArtifactKind Kind, string? RelativePath,
    string SourceSha256, IReadOnlyList<Guid> RequiredFactors);

/// <summary>
/// Builds the affected set from an authenticated ELH1 and an explicitly supplied
/// catalog. The caller must establish completeness of inherited-only documents.
/// This inventory neither enumerates the live disk nor publishes a generation.
/// </summary>
public sealed class VaultRekeyInventory
{
    public VaultGenerationStamp Generation { get; }
    public string IndexSha256 { get; }
    public string CatalogSha256 { get; }
    public IReadOnlyList<VaultRekeyArtifact> Artifacts { get; }
    internal Guid RootFactorId { get; }
    internal IReadOnlyList<VaultRekeyDocument> Documents { get; }

    private VaultRekeyInventory(VaultGenerationStamp generation, Guid rootFactorId, string indexSha256,
        string catalogSha256, List<VaultRekeyArtifact> artifacts, List<VaultRekeyDocument> documents)
    {
        Generation = generation;
        RootFactorId = rootFactorId;
        IndexSha256 = indexSha256;
        CatalogSha256 = catalogSha256;
        Artifacts = artifacts.AsReadOnly();
        Documents = documents.AsReadOnly();
    }

    public static VaultRekeyInventory Create(VaultGenerationManifest manifest, ReadOnlySpan<byte> encryptedIndex,
        LayerSecret root, IEnumerable<Guid> changedFactors, IEnumerable<VaultRekeyDocument> catalog)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(changedFactors);
        ArgumentNullException.ThrowIfNull(catalog);
        if (encryptedIndex.Length > WorkspaceLockIndex.MaximumEncodedBytes)
            throw new InvalidDataException("Workspace index exceeds the rekey bound.");
        byte[] index = encryptedIndex.ToArray();
        var entries = WorkspaceLockIndex.Open(index, manifest.Stamp.WorkspaceId, root);
        var byId = entries.ToDictionary(x => x.Id);
        var byPath = entries.ToDictionary(x => x.RelativePath, StringComparer.OrdinalIgnoreCase);
        string rootPath = byId[root.Id].RelativePath + "/";
        if (manifest.Find(root.Id, LayerKind.Folder) is null)
            throw new CryptographicException("Workspace root vault is absent from the source generation.");
        foreach (var entry in entries.Where(x => x.RelativePath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase)))
        {
            if (FindOwner(entry.RelativePath[..entry.RelativePath.LastIndexOf('/')]).Id != entry.ParentId)
                throw new InvalidDataException("An enrolled node skips a locked ancestor.");
        }
        var changes = new HashSet<Guid>();
        var artifacts = new List<VaultRekeyArtifact>();
        string indexDigest = Convert.ToHexString(SHA256.HashData(index));

        foreach (var id in changedFactors)
        {
            if (!changes.Add(id) || !byId.TryGetValue(id, out var entry) ||
                (id != root.Id && !entry.RelativePath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Unknown, duplicate or out-of-root changed factor.");
            var pin = manifest.Find(id, entry.Kind)?.Pin
                ?? throw new CryptographicException("Changed factor vault is absent from the source generation.");
            artifacts.Add(new VaultRekeyArtifact("vault:" + id.ToString("N"),
                VaultRekeyArtifactKind.FactorVault, null, pin.CiphertextSha256, Array.AsReadOnly(new[] { id })));
        }
        if (changes.Count == 0) throw new ArgumentException("At least one factor must change.", nameof(changedFactors));

        if (changes.Contains(root.Id))
            artifacts.Add(new VaultRekeyArtifact("index", VaultRekeyArtifactKind.RootIndex,
                null, indexDigest, Array.AsReadOnly(new[] { root.Id })));

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var documents = new List<VaultRekeyDocument>();
        foreach (var doc in catalog)
        {
            if (doc is null || !IsDocumentPath(doc.RelativePath, rootPath) || !paths.Add(doc.RelativePath))
                throw new InvalidDataException("Invalid or duplicate rekey catalog path.");

            string digest = NormalizeDigest(doc.SourceSha256);
            documents.Add(doc with { SourceSha256 = digest });
            var owner = FindOwner(doc.RelativePath);
            if (doc.RelativePath.Equals(owner.RelativePath, StringComparison.OrdinalIgnoreCase) !=
                (owner.Kind == LayerKind.NoteAdditional))
                throw new InvalidDataException("Catalog path conflicts with the authenticated hierarchy.");
            var chain = Chain(owner);
            if (!chain.Any(changes.Contains)) continue;
            foreach (var id in chain)
            {
                if (manifest.Find(id, byId[id].Kind) is null)
                    throw new CryptographicException("Affected document has an unenrolled dependency.");
            }
            artifacts.Add(new VaultRekeyArtifact("note:" + doc.RelativePath, VaultRekeyArtifactKind.Document,
                doc.RelativePath, digest, chain.AsReadOnly()));
        }

        foreach (var locked in entries.Where(x => x.Kind == LayerKind.NoteAdditional &&
            x.RelativePath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase)))
        {
            if (Chain(locked).Any(changes.Contains) && !paths.Contains(locked.RelativePath))
                throw new InvalidDataException("An explicitly locked affected document is absent from the catalog.");
        }

        artifacts.Sort((left, right) => StringComparer.Ordinal.Compare(left.Id, right.Id));
        string catalogDigest = ComputeCatalogSha256(manifest.Stamp.WorkspaceId, root.Id, indexDigest, documents);
        if (manifest.Bindings is { } binding && binding.CatalogSha256 != catalogDigest)
            throw new CryptographicException("The rekey catalog or index differs from the source generation.");
        return new VaultRekeyInventory(manifest.Stamp, root.Id, indexDigest, catalogDigest, artifacts, documents);

        WorkspaceLockEntry FindOwner(string path)
        {
            string segment = path;
            while (true)
            {
                if (byPath.TryGetValue(segment, out var owner)) return owner;
                int separator = segment.LastIndexOf('/');
                if (separator < 0) throw new InvalidDataException("Path has no enrolled ancestor.");
                segment = segment[..separator];
            }
        }

        List<Guid> Chain(WorkspaceLockEntry entry)
        {
            var ids = new List<Guid> { entry.Id };
            while (entry.ParentId != Guid.Empty)
            {
                entry = byId[entry.ParentId];
                ids.Add(entry.Id);
            }
            if (entry.Id != root.Id)
                throw new InvalidDataException("Catalog path resolves to a different hierarchy root.");
            ids.Reverse();
            return ids;
        }
    }

    /// <summary>Canonical commitment for an authority-enrolled, separately verified catalog.</summary>
    public static string ComputeCatalogSha256(Guid workspaceId, Guid rootFactorId, string indexSha256,
        IEnumerable<VaultRekeyDocument> documents)
    {
        if (workspaceId == Guid.Empty || rootFactorId == Guid.Empty)
            throw new ArgumentException("Catalog needs workspace and root identities.");
        ArgumentNullException.ThrowIfNull(documents);
        var ordered = documents.OrderBy(x => x?.RelativePath, StringComparer.Ordinal).ToArray();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return HashCanonical(writer =>
        {
            writer.Write("Embasic.ENote.RekeyCatalog.1");
            writer.Write(workspaceId.ToByteArray());
            writer.Write(rootFactorId.ToByteArray());
            writer.Write(Convert.FromHexString(NormalizeDigest(indexSha256)));
            writer.Write(ordered.Length);
            foreach (var doc in ordered)
            {
                if (doc is null || string.IsNullOrEmpty(doc.RelativePath) || !paths.Add(doc.RelativePath))
                    throw new InvalidDataException("Catalog paths must be present and unique.");
                writer.Write(doc.RelativePath);
                writer.Write(Convert.FromHexString(NormalizeDigest(doc.SourceSha256)));
            }
        });
    }

    internal static string HashCanonical(Action<BinaryWriter> write)
    {
        using var hash = SHA256.Create();
        using var sink = new CryptoStream(Stream.Null, hash, CryptoStreamMode.Write);
        using var writer = new BinaryWriter(sink, new System.Text.UTF8Encoding(false, true), leaveOpen: true);
        write(writer);
        writer.Flush();
        sink.FlushFinalBlock();
        return Convert.ToHexString(hash.Hash!);
    }

    internal static string NormalizeDigest(string? digest)
    {
        if (digest is not { Length: 64 } || !digest.All(Uri.IsHexDigit))
            throw new InvalidDataException("Expected an exact rekey SHA-256 digest.");
        return digest.ToUpperInvariant();
    }

    private static bool IsDocumentPath(string? path, string rootPath) =>
        path is not null && path.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase) &&
        path.EndsWith(".emd", StringComparison.OrdinalIgnoreCase) && path.Split('/').All(segment =>
            segment.Length > 0 && segment[^1] is not ('.' or ' ') &&
            !segment.Any(c => char.IsControl(c) || "\\:\"<>|?*".Contains(c)));
}
