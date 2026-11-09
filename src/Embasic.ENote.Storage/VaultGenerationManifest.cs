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
public sealed partial class VaultGenerationManifest
{
    private const string Domain = "Embasic.ENote.VaultGeneration.1";
    private const int MaximumBytes = 16 * 1024 * 1024;
    private const int MaximumPathBytes = 128 * 1024;
    private const int EntryFixedBytes = 16 + 1 + 3 * sizeof(ulong) + 32;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly int HeaderBytes = StringSize(Utf8.GetByteCount(Domain)) + 16 + sizeof(long) + sizeof(int);
    private readonly Dictionary<Guid, VaultAnchorGrant> _grants;
    public VaultGenerationStamp Stamp { get; }

    private VaultGenerationManifest(Guid workspaceId, long epoch, Dictionary<Guid, VaultAnchorGrant> grants)
    {
        _grants = grants;
        Stamp = new VaultGenerationStamp(workspaceId, epoch, Digest(workspaceId, epoch));
    }

    public static VaultGenerationManifest Create(Guid workspaceId, long epoch, IEnumerable<VaultAnchorGrant> grants)
    {
        if (workspaceId == Guid.Empty) throw new ArgumentException("Missing workspace.", nameof(workspaceId));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(epoch);
        ArgumentNullException.ThrowIfNull(grants);

        var indexed = new Dictionary<Guid, VaultAnchorGrant>();
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var identities = new HashSet<WindowsVaultFileIdentity>();
        int encodedBytes = HeaderBytes;
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
            int pathBytes = Utf8.GetByteCount(fullPath);
            if (pathBytes > MaximumPathBytes)
                throw new ArgumentException("Vault path exceeds the manifest byte limit.", nameof(grants));
            encodedBytes += EntryFixedBytes + StringSize(pathBytes);
            if (encodedBytes > MaximumBytes)
                throw new ArgumentException("Vault generation exceeds the manifest byte limit.", nameof(grants));
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

    internal void WriteCanonical(Stream destination) =>
        WriteCanonical(destination, Stamp.WorkspaceId, Stamp.Epoch);

    private string Digest(Guid workspaceId, long epoch)
    {
        using var hash = SHA256.Create();
        using var stream = new CryptoStream(Stream.Null, hash, CryptoStreamMode.Write);
        WriteCanonical(stream, workspaceId, epoch);
        stream.FlushFinalBlock();
        return Convert.ToHexString(hash.Hash!);
    }

    private void WriteCanonical(Stream destination, Guid workspaceId, long epoch)
    {
        using var writer = new BinaryWriter(destination, Utf8, leaveOpen: true);
        writer.Write(Domain);
        writer.Write(workspaceId.ToByteArray());
        writer.Write(epoch);
        writer.Write(_grants.Count);
        foreach (var grant in _grants.Values.OrderBy(grant => grant.LockId))
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

    private static int StringSize(int utf8Bytes) =>
        utf8Bytes + (utf8Bytes < 128 ? 1 : utf8Bytes < 16_384 ? 2 : 3);
}
