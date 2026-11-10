using System.Security.Cryptography;
using Embasic.ENote.Emd;

namespace Embasic.ENote.Storage;

/// <summary>
/// Protected-store reference for publishing exact versioned ciphertext and resuming a registered
/// rekey after process restart. The caller owns physical permissions and snapshot lifetime.
/// </summary>
public sealed partial class VaultRekeyPublication : IDisposable
{
    private readonly string _publishedPath;
    private readonly VaultAtomicFileDirectory _published;
    private readonly VaultAtomicFileDirectory _journal;
    private readonly IVaultGenerationStore _store;
    private readonly VaultRekeyStaging _staging;
    private readonly string _snapshotPath;
    private readonly string _liveRoot;
    private readonly byte[] _journalKey;
    private bool _disposed;

    public VaultRekeyPublication(string publishedDirectory, string journalDirectory,
        IVaultGenerationStore store, VaultRekeyStaging staging, string snapshotDirectory,
        string liveRoot, ReadOnlySpan<byte> journalKey)
    {
        if (journalKey.Length != 32) throw new ArgumentException("Expected a 32-byte journal key.", nameof(journalKey));
        _publishedPath = Path.GetFullPath(publishedDirectory);
        _published = new VaultAtomicFileDirectory(_publishedPath);
        _journal = new VaultAtomicFileDirectory(journalDirectory);
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _staging = staging ?? throw new ArgumentNullException(nameof(staging));
        _snapshotPath = Path.GetFullPath(snapshotDirectory);
        _liveRoot = Path.GetFullPath(liveRoot);
        _journalKey = journalKey.ToArray();
    }

    /// <summary>
    /// Copy prepared objects to immutable names and construct their actual file grants.
    /// This does not move the provider pointer. Linux uses fixture identities for changed vaults.
    /// </summary>
    public VaultGenerationManifest PrepareCandidate(VaultRekeyCommitment commitment,
        LayerSecret root, Func<Guid, WindowsVaultFileIdentity>? nonWindowsIdentity = null)
    {
        ArgumentNullException.ThrowIfNull(commitment);
        using var fence = EnterLiveWrite();
        var source = ReadExact(commitment.Inventory.Generation);
        var snapshot = VaultWorkspaceSnapshot.Open(_snapshotPath, source.Stamp.WorkspaceId, root);
        snapshot.VerifyAgainst(source, root);
        snapshot.VerifyLive(_liveRoot, source);
        if (source.Bindings?.CatalogSha256 != commitment.Inventory.CatalogSha256)
            throw new CryptographicException("Candidate requires the selected source catalog.");
        _staging.RequirePrepared(commitment.Inventory, commitment.Targets, snapshot.OpenSource);
        foreach (var item in commitment.Inventory.Artifacts)
        {
            string digest = commitment.Targets[item.Id];
            _published.Publish(NameFor(item, digest), destination =>
            {
                using var prepared = _staging.OpenPrepared(item, digest);
                VaultRekeyStaging.Verify(item, prepared, digest, destination);
            }, () => VerifyObject(item, digest));
        }

        long epoch = checked(source.Stamp.Epoch + 1);
        var grants = source.Grants.Select(prior =>
        {
            string itemId = "vault:" + prior.LockId.ToString("N");
            string digest = prior.Pin.CiphertextSha256;
            string path = prior.BoundVaultPath!;
            var identity = prior.BoundVaultFileIdentity!.Value;
            if (commitment.Targets.TryGetValue(itemId, out string? target))
            {
                var item = commitment.Inventory.Artifacts.Single(x => x.Id == itemId);
                digest = target;
                path = Path.Combine(_publishedPath, NameFor(item, digest));
                using var file = OpenObject(item, digest);
                VerifyVaultHeader(file, prior);
                identity = OperatingSystem.IsWindows()
                    ? WindowsVaultFileIdentity.Read(file.SafeFileHandle)
                    : nonWindowsIdentity?.Invoke(prior.LockId)
                        ?? throw new PlatformNotSupportedException("Changed vault identity enrollment requires Windows.");
            }
            return prior with
            {
                MinimumEpoch = epoch,
                Pin = VaultSnapshotPin.RestoreVerifiedDigest(prior.LockId, prior.Kind, epoch,
                    Convert.FromHexString(digest)),
                BoundVaultPath = path,
                BoundVaultFileIdentity = identity
            };
        });
        var next = VaultGenerationManifest.Create(source.Stamp.WorkspaceId, epoch, grants, commitment.Destination);
        commitment.RequireMatch(source, next);
        VerifyPublication(commitment, next);
        SavePlan(source, commitment, next);
        return next;
    }

    /// <summary>Reopen all inputs from a protected journal and immutable stores before mutating the authority.</summary>
    public VaultGenerationAdvanceState Resume(VaultGenerationRecovery recovery, VaultGenerationStamp candidate,
        LayerSecret root)
    {
        ArgumentNullException.ThrowIfNull(recovery);
        ArgumentNullException.ThrowIfNull(candidate);
        using var fence = EnterLiveWrite();
        var plan = ReadPlan(candidate);
        var source = ReadExact(plan.Expected);
        var next = ReadExact(candidate);
        var snapshot = VaultWorkspaceSnapshot.Open(_snapshotPath, candidate.WorkspaceId, root);
        var inventory = snapshot.BuildInventory(source, root, plan.Factors);
        var commitment = new VaultRekeyCommitment(inventory, plan.Targets);
        return recovery.StageAndAdvanceRekey(commitment, next, _staging, snapshot.OpenSource, () =>
        {
            snapshot.VerifyAgainst(source, root);
            snapshot.VerifyLive(_liveRoot, source);
            VerifyPublication(commitment, next);
        });
    }

    /// <summary>Open only an exact authenticated object in the versioned publication.</summary>
    public Stream OpenPublished(VaultRekeyArtifact artifact, string expectedSha256)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        return OpenObject(artifact, VaultRekeyInventory.NormalizeDigest(expectedSha256));
    }

    private void VerifyPublication(VaultRekeyCommitment commitment, VaultGenerationManifest next)
    {
        foreach (var item in commitment.Inventory.Artifacts)
        {
            string digest = commitment.Targets[item.Id];
            using var file = OpenObject(item, digest);
            if (item.Kind != VaultRekeyArtifactKind.FactorVault) continue;
            var grant = next.Grants.Single(x => x.LockId == item.RequiredFactors[0]);
            VerifyVaultHeader(file, grant);
            if (grant.BoundVaultPath != Path.Combine(_publishedPath, NameFor(item, digest)) ||
                (OperatingSystem.IsWindows() && WindowsVaultFileIdentity.Read(file.SafeFileHandle) != grant.BoundVaultFileIdentity))
                throw new CryptographicException("Published vault path or Windows identity differs from the candidate.");
        }
    }

    private static void VerifyVaultHeader(Stream file, VaultAnchorGrant grant)
    {
        Span<byte> header = stackalloc byte[22];
        file.ReadExactly(header);
        if (header[4] != 1 || header[5] != (byte)grant.Kind || new Guid(header[6..]) != grant.LockId)
            throw new CryptographicException("Published vault header does not identify the expected factor.");
    }

    private FileStream OpenObject(VaultRekeyArtifact item, string digest)
    {
        var file = _published.OpenRead(NameFor(item, digest)) ?? throw new InvalidDataException("Missing published object.");
        try
        {
            VaultRekeyStaging.Verify(item, file, digest);
            file.Position = 0;
            return file;
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    private bool VerifyObject(VaultRekeyArtifact item, string digest)
    {
        using var file = _published.OpenRead(NameFor(item, digest));
        if (file is null) return false;
        VaultRekeyStaging.Verify(item, file, digest);
        return true;
    }

    private static string NameFor(VaultRekeyArtifact item, string digest) => digest + (item.Kind switch
    {
        VaultRekeyArtifactKind.FactorVault => ".elkey",
        VaultRekeyArtifactKind.RootIndex => ".elh",
        VaultRekeyArtifactKind.Document => ".emd",
        _ => throw new InvalidDataException("Unexpected publication artifact.")
    });

    private VaultGenerationManifest ReadExact(VaultGenerationStamp stamp)
    {
        var manifest = _store.Read(stamp);
        return manifest?.Stamp == stamp ? manifest : throw new InvalidDataException("Missing exact generation manifest.");
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(_journalKey);
        _disposed = true;
    }
}
