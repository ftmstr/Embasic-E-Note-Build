using System.Security.Cryptography;
using Embasic.ENote.Emd;

namespace Embasic.ENote.Storage;

/// <summary>
/// Experimental contract for independently trusted, rollback-resistant
/// policy. There is NO implementation of such a trust anchor in this repo.
/// An ordinary local file, memory object, DPAPI blob or cloned disk does
/// NOT establish a monotonic anti-rollback guarantee.
/// </summary>
public interface IExternalVaultAnchorReader
{
    bool TryRead(Guid workspaceId, Guid lockId, LayerKind kind,
        out VaultAnchorGrant? grant);
}

/// <summary>
/// A caller-provided, NOT SELF-AUTHENTICATING policy claim.
/// It must only be treated as trusted if the reader is backed by an
/// independently reviewed rollback-resistant source.
/// </summary>
public sealed record VaultAnchorGrant(
    Guid WorkspaceId, Guid LockId, LayerKind Kind,
    VaultSnapshotPin Pin, long MinimumEpoch,
    string? BoundVaultPath = null)
{
    public WindowsVaultFileIdentity? BoundVaultFileIdentity { get; init; }
}

/// <summary>
/// Fail-closed entry point when an external trust anchor is absent,
/// unavailable, or inconsistent. Proof of implementation is NOT proof
/// that the external reader is trusted or that old ELV1 backups are
/// cryptographically revoked. Historical unguarded APIs remain available.
/// </summary>
public sealed class TrustedVaultOpenGate
{
    private enum Binding { None, Path, WindowsFile }

    private readonly IExternalVaultAnchorReader? _anchor;
    private readonly LayerVaultDiskStore _disk;
    private readonly bool _requireOperationScope;

    public TrustedVaultOpenGate(IExternalVaultAnchorReader? anchor, LayerVaultDiskStore? disk = null)
        : this(anchor, requireOperationScope: false, disk) { }

    public TrustedVaultOpenGate(IExternalVaultAnchorReader? anchor, bool requireOperationScope,
        LayerVaultDiskStore? disk = null)
    {
        _anchor = anchor;
        _disk = disk ?? new LayerVaultDiskStore();
        _requireOperationScope = requireOperationScope;
    }

    /// <summary>
    /// Legacy opt-in policy gate. Does NOT bind a filename to the policy:
    /// historical Tur17 callers and copied encrypted vaults remain possible.
    /// New callers must opt into UnlockPathBound, which itself is NOT
    /// NTFS file identity, handle-bound ancestry, or anti-rollback.
    /// </summary>
    public LayerSecret Unlock(string path, Guid workspaceId, Guid lockId,
        LayerKind kind, string credential, bool recovery = false) =>
        UnlockCore(path, workspaceId, lockId, kind, credential, recovery,
            Binding.None);

    /// <summary>
    /// EXTRA experimental guard: fail closed without an absolute path
    /// supplied by the external trusted policy; require its canonical
    /// path string to match the requested filename before loading ELV1.
    /// This is NOT a physical volume/file-ID binding. Symlinks, junctions,
    /// reparse races and cloned old policy+data remain unresolved.
    /// </summary>
    public LayerSecret UnlockPathBound(string path, Guid workspaceId,
        Guid lockId, LayerKind kind, string credential,
        bool recovery = false) =>
        UnlockCore(path, workspaceId, lockId, kind, credential, recovery,
            Binding.Path);

    /// <summary>
    /// Experimental Windows-only unlock. The external policy must supply
    /// an exact absolute path and an independently enrolled file identity.
    /// Identity and ciphertext are acquired from the same open file handle.
    /// The policy still needs independent rollback protection.
    /// </summary>
    public LayerSecret UnlockFileBound(string path, Guid workspaceId, Guid lockId,
        LayerKind kind, string credential, bool recovery = false)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("File-bound vault unlock requires Windows.");
        return UnlockCore(path, workspaceId, lockId, kind, credential, recovery,
            Binding.WindowsFile);
    }

    /// <summary>
    /// Use the enrolled key synchronously under a transition-aware provider fence.
    /// The supplied key is disposed before releasing the fence. The callback
    /// must not copy key material, start asynchronous work or re-enter the provider.
    /// </summary>
    public void UseFileBound(string path, Guid workspaceId, Guid lockId, LayerKind kind,
        string credential, Action<LayerSecret> use, bool recovery = false)
    {
        RejectRawKeyAccess();
        ArgumentNullException.ThrowIfNull(use);
        var (generation, stamp, grant) = SelectFencedGrant(workspaceId, lockId, kind);
        using var fence = generation.EnterKeyUseFence(stamp);
        using var secret = UnlockWithGrant(path, workspaceId, lockId, kind, credential,
            recovery, Binding.WindowsFile, grant);
        use(secret);
    }

    /// <summary>Run the built-in ELK1 read inside the fence without exposing the enrolled key.</summary>
    public string ReadLayeredMarkdownFileBound(string path, Guid workspaceId, Guid lockId, LayerKind kind,
        string vaultCredential, ReadOnlySpan<byte> document, string documentPassword,
        IReadOnlyList<LayerSecret>? innerLayers = null, bool recovery = false)
    {
        var (generation, stamp, grant) = SelectFencedGrant(workspaceId, lockId, kind);
        using var fence = generation.EnterKeyUseFence(stamp);
        using var secret = UnlockWithGrant(path, workspaceId, lockId, kind, vaultCredential,
            recovery, Binding.WindowsFile, grant);
        LayerSecret[] factors = innerLayers is null ? [secret] : [secret, .. innerLayers];
        return LayeredEmd3.Open(document, factors, documentPassword);
    }

    private (VaultGenerationRecovery Generation, VaultGenerationStamp Stamp, VaultAnchorGrant Grant)
        SelectFencedGrant(Guid workspaceId, Guid lockId, LayerKind kind)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("File-bound vault use requires Windows.");
        if (workspaceId == Guid.Empty || lockId == Guid.Empty)
            throw new CryptographicException("Missing vault policy identity.");
        if (_anchor is not VaultGenerationRecovery generation)
            throw new NotSupportedException("A generation-backed policy is required.");

        var manifest = generation.ReadForKeyDelivery(workspaceId);
        var grant = manifest.Find(lockId, kind)
            ?? throw new CryptographicException("No independently trusted vault policy.");
        return (generation, manifest.Stamp, grant);
    }

    private void RejectRawKeyAccess()
    {
        if (_requireOperationScope)
            throw new NotSupportedException("This gate only permits built-in fenced operations.");
    }

    private LayerSecret UnlockCore(string path, Guid workspaceId, Guid lockId,
        LayerKind kind, string credential, bool recovery, Binding binding)
    {
        RejectRawKeyAccess();
        if (workspaceId == Guid.Empty || lockId == Guid.Empty)
            throw new CryptographicException("Missing vault policy identity.");
        if (binding == Binding.WindowsFile && _anchor is VaultGenerationRecovery generation)
        {
            var manifest = generation.ReadForKeyDelivery(workspaceId);
            var bound = manifest.Find(lockId, kind)
                ?? throw new CryptographicException("No independently trusted vault policy.");
            var secret = UnlockWithGrant(path, workspaceId, lockId, kind, credential, recovery, binding, bound);
            try
            {
                generation.RequireCurrent(manifest.Stamp);
                return secret;
            }
            catch
            {
                secret.Dispose();
                throw;
            }
        }
        if (_anchor is null ||
            !_anchor.TryRead(workspaceId, lockId, kind, out var grant) ||
            grant is null)
            throw new CryptographicException("No independently trusted vault policy.");

        return UnlockWithGrant(path, workspaceId, lockId, kind, credential, recovery, binding, grant);
    }

    private LayerSecret UnlockWithGrant(string path, Guid workspaceId, Guid lockId, LayerKind kind,
        string credential, bool recovery, Binding binding, VaultAnchorGrant grant)
    {
        if (grant.WorkspaceId != workspaceId || grant.LockId != lockId ||
            grant.Kind != kind || grant.Pin is null ||
            grant.Pin.LockId != lockId || grant.Pin.LockKind != kind ||
            grant.MinimumEpoch <= 0 ||
            grant.Pin.TrustedEpoch < grant.MinimumEpoch)
            throw new CryptographicException("Untrusted or stale vault policy.");

        if (binding != Binding.None)
        {
            if (string.IsNullOrWhiteSpace(grant.BoundVaultPath) ||
                !Path.IsPathFullyQualified(grant.BoundVaultPath))
                throw new CryptographicException(
                    "Missing independently trusted absolute vault path.");

            // Lexical canonicalization only; reparse points and malicious
            // parent-directory swaps are NOT resolved by Path.GetFullPath.
            if (binding == Binding.WindowsFile && !Path.IsPathFullyQualified(path))
                throw new CryptographicException("Requested vault path must be absolute.");
            string expected = Path.GetFullPath(grant.BoundVaultPath);
            string actual = Path.GetFullPath(path);
            var comparison = binding == Binding.Path && OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!string.Equals(expected, actual, comparison))
                throw new CryptographicException(
                    "Vault path does not match trusted policy.");

            if (binding == Binding.WindowsFile)
            {
                if (grant.BoundVaultFileIdentity is not { IsUsable: true } identity)
                    throw new CryptographicException("No independently enrolled vault file identity.");
                return _disk.UnlockPinnedAtIdentity(actual, grant.Pin, credential,
                    grant.MinimumEpoch, recovery, identity);
            }
        }

        return _disk.UnlockPinned(path, grant.Pin, credential,
            grant.MinimumEpoch, recovery);
    }
}
