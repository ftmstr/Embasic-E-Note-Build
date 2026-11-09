using System.Security.Cryptography;

namespace Embasic.ENote.Storage;

/// <summary>Independently selected backup digest and one exact vacant authority revision.</summary>
public sealed record VaultRootReEnrollmentApproval(VaultGenerationStamp Generation,
    long TombstoneRevision, string ArchiveSha256);

/// <summary>The approval must survive separately and cannot be derived from the restored archive.</summary>
public interface IVaultRootReEnrollmentApprovalReader
{
    VaultRootReEnrollmentApproval? Read(VaultGenerationStamp generation);
}

/// <summary>Archives root authorization metadata without exporting the root key or provider state.</summary>
public static class VaultRootAuthorizationArchive
{
    private const int Size = 4 + 16 + 8 + 32 + 8 + 16 + 32 + 32;

    public static byte[] Capture(IVaultRootRecoveryLifecycle authority, VaultGenerationStamp generation)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(generation);
        var state = authority.ObserveAuthorization(generation);
        if (state.Revision <= 0 || state.Proof is not { } proof || proof.Generation != generation ||
            proof.RootFactorId == Guid.Empty)
            throw new CryptographicException("There is no active root authorization to archive.");

        using var stream = new MemoryStream(Size);
        using var writer = new BinaryWriter(stream);
        writer.Write("ERA1"u8);
        writer.Write(generation.WorkspaceId.ToByteArray());
        writer.Write(generation.Epoch);
        WriteDigest(writer, generation.ManifestSha256);
        writer.Write(state.Revision);
        writer.Write(proof.RootFactorId.ToByteArray());
        WriteDigest(writer, proof.BackupSha256);
        WriteDigest(writer, proof.IndexSha256);
        return stream.ToArray();
    }

    public static VaultRootRecoveryUpdateState ReEnroll(IVaultRootRecoveryLifecycle authority,
        IVaultRootReEnrollmentApprovalReader approvals, VaultGenerationStamp generation, ReadOnlySpan<byte> archive)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(approvals);
        ArgumentNullException.ThrowIfNull(generation);
        if (archive.Length != Size) throw new InvalidDataException("Unexpected root authorization archive length.");
        byte[] bytes = archive.ToArray();
        var approval = approvals.Read(generation);
        if (approval is null || approval.Generation != generation ||
            !string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), approval.ArchiveSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new CryptographicException("Root archive is not independently approved.");

        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(stream);
        if (!reader.ReadBytes(4).AsSpan().SequenceEqual("ERA1"u8)) throw new InvalidDataException("Unknown root archive.");
        var stamp = new VaultGenerationStamp(new Guid(reader.ReadBytes(16)), reader.ReadInt64(),
            Convert.ToHexString(reader.ReadBytes(32)));
        long priorRevision = reader.ReadInt64();
        var proof = new VaultRootRecoveryProof(stamp, new Guid(reader.ReadBytes(16)),
            Convert.ToHexString(reader.ReadBytes(32)), Convert.ToHexString(reader.ReadBytes(32)));
        if (stamp != generation || proof.RootFactorId == Guid.Empty || priorRevision <= 0 ||
            approval.TombstoneRevision <= priorRevision || approval.TombstoneRevision == long.MaxValue)
            throw new CryptographicException("Root archive does not precede the approved vacant revision.");

        var observed = authority.ObserveAuthorization(generation);
        if (observed.Revision == approval.TombstoneRevision + 1 && observed.Proof == proof)
            return VaultRootRecoveryUpdateState.Applied;
        if (observed.Revision != approval.TombstoneRevision || observed.Proof is not null ||
            approvals.Read(generation) != approval)
            throw new CryptographicException("Root authority or approval has changed.");

        return VaultRootRecoveryUpdates.Change(authority, generation, approval.TombstoneRevision, proof);
    }

    private static void WriteDigest(BinaryWriter writer, string digest) =>
        writer.Write(Convert.FromHexString(VaultRekeyInventory.NormalizeDigest(digest)));
}
