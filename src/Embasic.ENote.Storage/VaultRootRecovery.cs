using System.Security.Cryptography;
using Embasic.ENote.Emd;

namespace Embasic.ENote.Storage;

/// <summary>Exact enrollment values that a separate authority must protect.</summary>
public sealed record VaultRootRecoveryProof(
    VaultGenerationStamp Generation, Guid RootFactorId, string BackupSha256, string IndexSha256);

public sealed record VaultRootRecoveryBackup(byte[] Envelope, VaultRootRecoveryProof Proof);

/// <summary>Independent recovery authorization. No production authority is shipped.</summary>
public interface IExternalVaultRootRecoveryAuthority
{
    VaultRootRecoveryProof? ReadRootRecovery(Guid workspaceId);
}

/// <summary>Experimental workspace root recovery using existing ERB1 and ELH1.</summary>
public sealed class VaultRootRecovery
{
    private readonly VaultGenerationRecovery _generation;
    private readonly IExternalVaultRootRecoveryAuthority _authority;

    public VaultRootRecovery(VaultGenerationRecovery generation, IExternalVaultRootRecoveryAuthority authority)
    {
        _generation = generation ?? throw new ArgumentNullException(nameof(generation));
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
    }

    /// <summary>Prepare using the exact root vault pinned by a trusted, committed generation.</summary>
    public static VaultRootRecoveryBackup Prepare(VaultGenerationManifest manifest, Guid rootFactorId,
        ReadOnlySpan<byte> rootVault, string rootCredential, ReadOnlySpan<byte> index, string backupPassword,
        bool useRecoverySlot = false)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var grant = manifest.Find(rootFactorId, LayerKind.Folder)
            ?? throw new CryptographicException("The workspace root is not registered in this generation.");
        if (rootVault.Length != LayerKeyVault.Size)
            throw new InvalidDataException("Invalid root vault size.");

        byte[] encryptedIndex = SnapshotIndex(index);
        using var root = grant.Pin.OpenPinned(rootVault.ToArray(), rootCredential,
            manifest.Stamp.Epoch, recovery: useRecoverySlot);
        WorkspaceLockIndex.Open(encryptedIndex, manifest.Stamp.WorkspaceId, root);
        byte[] key = root.CopySecret();
        try
        {
            byte[] backup = RecoveryBackupCodec.Create(key, backupPassword);
            return new VaultRootRecoveryBackup(backup, new VaultRootRecoveryProof(manifest.Stamp, root.Id,
                Digest(backup), Digest(encryptedIndex)));
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    public LayerSecret Restore(Guid workspaceId, ReadOnlySpan<byte> backup, ReadOnlySpan<byte> index,
        string backupPassword)
    {
        var manifest = _generation.ReadForKeyDelivery(workspaceId);
        var proof = _authority.ReadRootRecovery(workspaceId);
        if (proof is null || proof.Generation != manifest.Stamp ||
            manifest.Find(proof.RootFactorId, LayerKind.Folder) is null)
            throw new CryptographicException("No authorized root recovery for the selected generation.");
        if (backup.Length != RecoveryBackupCodec.TotalBytes)
            throw new InvalidDataException("Invalid root recovery backup size.");

        byte[] encryptedBackup = backup.ToArray();
        byte[] encryptedIndex = SnapshotIndex(index);
        if (!string.Equals(Digest(encryptedBackup), proof.BackupSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Digest(encryptedIndex), proof.IndexSha256, StringComparison.OrdinalIgnoreCase))
            throw new CryptographicException("Recovery backup or hierarchy differs from the authorized proof.");

        byte[] raw = RecoveryBackupCodec.Open(encryptedBackup, backupPassword);
        try
        {
            var root = new LayerSecret(proof.RootFactorId, LayerKind.Folder, raw);
            try
            {
                WorkspaceLockIndex.Open(encryptedIndex, workspaceId, root);
                if (_authority.ReadRootRecovery(workspaceId) != proof)
                    throw new CryptographicException("Root recovery authorization changed before delivery.");
                _generation.RequireCurrent(manifest.Stamp);
                return root;
            }
            catch
            {
                root.Dispose();
                throw;
            }
        }
        finally { CryptographicOperations.ZeroMemory(raw); }
    }

    private static byte[] SnapshotIndex(ReadOnlySpan<byte> index)
    {
        if (index.Length > WorkspaceLockIndex.MaximumEncodedBytes)
            throw new InvalidDataException("Workspace index exceeds the recovery bound.");
        return index.ToArray();
    }

    private static string Digest(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
