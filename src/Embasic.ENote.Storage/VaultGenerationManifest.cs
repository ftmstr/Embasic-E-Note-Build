using System.Security.Cryptography;
using System.Text;
using Embasic.ENote.Emd;

namespace Embasic.ENote.Storage;

/// <summary>An exact workspace generation and manifest commitment.</summary>
public sealed record VaultGenerationStamp
{
    public Guid WorkspaceId { get; }
    public long Epoch { get; }
    public string ManifestSha256 { get; }

    public VaultGenerationStamp(Guid workspaceId, long epoch, string manifestSha256)
    {
        if (workspaceId == Guid.Empty) throw new ArgumentException("Missing workspace.", nameof(workspaceId));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(epoch);
        ArgumentNullException.ThrowIfNull(manifestSha256);
        if (manifestSha256.Length != 64 || !manifestSha256.All(Uri.IsHexDigit))
            throw new ArgumentException("Expected a SHA-256 digest.", nameof(manifestSha256));
        WorkspaceId = workspaceId;
        Epoch = epoch;
        ManifestSha256 = manifestSha256.ToUpperInvariant();
    }
}

/// <summary>
/// Experimental immutable collection of Windows vault grants. Its digest authenticates
/// the collection only when matched against an independently trusted stamp.
/// </summary>
public sealed class VaultGenerationManifest
{
    private readonly Dictionary<Guid, VaultAnchorGrant> _grants;
    public VaultGenerationStamp Stamp { get; }

    private VaultGenerationManifest(Guid workspaceId, long epoch, Dictionary<Guid, VaultAnchorGrant> grants)
    {
        _grants = grants;
        Stamp = new VaultGenerationStamp(workspaceId, epoch, Digest(workspaceId, epoch, grants));
    }

    public static VaultGenerationManifest Create(Guid workspaceId, long epoch, IEnumerable<VaultAnchorGrant> grants)
    {
        if (workspaceId == Guid.Empty) throw new ArgumentException("Missing workspace.", nameof(workspaceId));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(epoch);
        ArgumentNullException.ThrowIfNull(grants);

        var indexed = new Dictionary<Guid, VaultAnchorGrant>();
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var identities = new HashSet<WindowsVaultFileIdentity>();
        foreach (var grant in grants)
        {
            if (grant is null || grant.WorkspaceId != workspaceId || grant.LockId == Guid.Empty ||
                !Enum.IsDefined(grant.Kind) || grant.Pin is null ||
                grant.Pin.LockId != grant.LockId || grant.Pin.LockKind != grant.Kind ||
                grant.Pin.TrustedEpoch != epoch || grant.MinimumEpoch != epoch ||
                grant.BoundVaultPath is not { } path || !Path.IsPathFullyQualified(path) ||
                grant.BoundVaultFileIdentity is not { IsUsable: true } identity)
                throw new ArgumentException("Invalid generation grant.", nameof(grants));

            string fullPath = Path.GetFullPath(path);
            if (!Path.GetExtension(fullPath).Equals(".elkey", StringComparison.OrdinalIgnoreCase) ||
                !paths.Add(fullPath) || !identities.Add(identity) ||
                !indexed.TryAdd(grant.LockId, grant with { BoundVaultPath = fullPath }))
                throw new ArgumentException("Invalid or duplicate vault enrollment.", nameof(grants));
        }
        if (indexed.Count == 0) throw new ArgumentException("Generation has no grants.", nameof(grants));
        return new VaultGenerationManifest(workspaceId, epoch, indexed);
    }

    public VaultAnchorGrant? Find(Guid lockId, LayerKind kind) =>
        _grants.TryGetValue(lockId, out var grant) && grant.Kind == kind ? grant : null;

    internal bool RetainsFactorsFrom(VaultGenerationManifest prior) =>
        prior._grants.Values.All(grant => Find(grant.LockId, grant.Kind) is not null);

    private static string Digest(Guid workspaceId, long epoch, Dictionary<Guid, VaultAnchorGrant> grants)
    {
        using var hash = SHA256.Create();
        using var stream = new CryptoStream(Stream.Null, hash, CryptoStreamMode.Write);
        using (var writer = new BinaryWriter(stream, new UTF8Encoding(false, true), leaveOpen: true))
        {
            writer.Write("Embasic.ENote.VaultGeneration.1");
            writer.Write(workspaceId.ToByteArray());
            writer.Write(epoch);
            writer.Write(grants.Count);
            foreach (var grant in grants.Values.OrderBy(grant => grant.LockId))
            {
                var identity = grant.BoundVaultFileIdentity!.Value;
                writer.Write(grant.LockId.ToByteArray());
                writer.Write((byte)grant.Kind);
                writer.Write(grant.BoundVaultPath!);
                writer.Write(identity.VolumeSerialNumber);
                writer.Write(identity.FileIdPart1);
                writer.Write(identity.FileIdPart2);
                writer.Write(Convert.FromHexString(grant.Pin.CiphertextSha256));
            }
        }
        stream.FlushFinalBlock();
        return Convert.ToHexString(hash.Hash!);
    }
}
