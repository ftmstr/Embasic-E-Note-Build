using System.Security.Cryptography;
using Embasic.ENote.Emd;

namespace Embasic.ENote.Storage;

/// <summary>
/// Verifies source bytes and stages caller-prepared ciphertext by exact digest.
/// Successful per-object publication is retryable; this does not commit a
/// workspace generation or prove that the supplied ciphertext is fresh.
/// </summary>
public sealed class VaultRekeyStaging
{
    private readonly VaultAtomicFileDirectory _files;

    public VaultRekeyStaging(string directory) => _files = new VaultAtomicFileDirectory(directory);

    public void Prepare(VaultRekeyInventory inventory, IReadOnlyDictionary<string, string> candidateDigests,
        Func<VaultRekeyArtifact, Stream?> openSource, Func<VaultRekeyArtifact, Stream?> openCandidate)
    {
        ArgumentNullException.ThrowIfNull(openCandidate);
        var targets = ValidateTargets(inventory, candidateDigests);
        VerifySources(inventory, openSource);
        foreach (var item in inventory.Artifacts)
        {
            var digest = targets[item.Id];
            _files.Publish(NameFor(digest), destination =>
            {
                using var candidate = openCandidate(item)
                    ?? throw new InvalidDataException("An exact prepared artifact is missing.");
                Verify(item, candidate, digest, destination);
            }, () => VerifyStaged(item, digest));
        }
    }

    public void RequirePrepared(VaultRekeyInventory inventory, IReadOnlyDictionary<string, string> candidateDigests,
        Func<VaultRekeyArtifact, Stream?> openSource)
    {
        var targets = ValidateTargets(inventory, candidateDigests);
        VerifySources(inventory, openSource);
        foreach (var item in inventory.Artifacts)
        {
            if (!VerifyStaged(item, targets[item.Id]))
                throw new InvalidDataException("The staged rekey set is incomplete.");
        }
    }

    private static Dictionary<string, string> ValidateTargets(VaultRekeyInventory inventory,
        IReadOnlyDictionary<string, string> candidateDigests)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(candidateDigests);
        var targets = new Dictionary<string, string>(candidateDigests, StringComparer.Ordinal);
        if (targets.Count != inventory.Artifacts.Count)
            throw new InvalidDataException("Rekey candidate map has missing or unexpected entries.");
        foreach (var item in inventory.Artifacts)
        {
            if (!targets.TryGetValue(item.Id, out var target))
                throw new InvalidDataException("Rekey candidate map omitted a required artifact.");
            var digest = VaultRekeyInventory.NormalizeDigest(target);
            if (digest == item.SourceSha256)
                throw new InvalidDataException("Prepared ciphertext equals its source.");
            targets[item.Id] = digest;
        }
        return targets;
    }

    private static void VerifySources(VaultRekeyInventory inventory, Func<VaultRekeyArtifact, Stream?> openSource)
    {
        ArgumentNullException.ThrowIfNull(openSource);
        foreach (var item in inventory.Artifacts)
        {
            using var source = openSource(item) ?? throw new InvalidDataException("A rekey source is missing.");
            Verify(item, source, item.SourceSha256);
        }
    }

    private bool VerifyStaged(VaultRekeyArtifact artifact, string digest)
    {
        using var staged = _files.OpenRead(NameFor(digest));
        if (staged is null) return false;
        Verify(artifact, staged, digest);
        return true;
    }

    private static string NameFor(string digest) => digest + ".rekey";

    private static void Verify(VaultRekeyArtifact item, Stream source, string digest, Stream? destination = null)
    {
        long maximum = item.Kind switch
        {
            VaultRekeyArtifactKind.RootIndex => WorkspaceLockIndex.MaximumEncodedBytes,
            VaultRekeyArtifactKind.FactorVault => LayerKeyVault.Size,
            VaultRekeyArtifactKind.Document => LayeredEmd3.MaxBytes,
            _ => throw new InvalidDataException("Unknown rekey artifact.")
        };
        ReadOnlySpan<byte> magic = item.Kind switch
        {
            VaultRekeyArtifactKind.RootIndex => "ELH1"u8,
            VaultRekeyArtifactKind.FactorVault => "ELV1"u8,
            VaultRekeyArtifactKind.Document => "ELK1"u8,
            _ => throw new InvalidDataException("Unknown rekey artifact.")
        };
        Span<byte> header = stackalloc byte[4];
        if (source.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) != header.Length ||
            !header.SequenceEqual(magic))
            throw new InvalidDataException("Unexpected rekey ciphertext format.");

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(header);
        destination?.Write(header);
        byte[] buffer = new byte[8192];
        long total = header.Length;
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) != 0)
        {
            total += read;
            if (total > maximum) throw new InvalidDataException("Rekey ciphertext exceeds its bound.");
            hash.AppendData(buffer.AsSpan(0, read));
            destination?.Write(buffer, 0, read);
        }
        if ((item.Kind == VaultRekeyArtifactKind.FactorVault && total != maximum) ||
            Convert.ToHexString(hash.GetHashAndReset()) != digest)
            throw new CryptographicException("Rekey ciphertext does not match its expected digest or size.");
    }
}
