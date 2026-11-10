using System.Security.Cryptography;

namespace Embasic.ENote.Storage;

/// <summary>Independent selection of a quiescent generation and a fresh replacement provider.</summary>
public sealed record VaultProviderRecoveryCheckpoint(Guid LostProvider, Guid ReplacementProvider,
    VaultGenerationStamp Selected, long VacantRevision);

/// <summary>Supplies current recovery authorization outside the lost or replacement provider storage.</summary>
public interface IVaultProviderRecoveryWitness
{
    VaultProviderRecoveryCheckpoint? Read(Guid workspace);
}

public sealed partial class VaultTransitionFilePrototype
{
    public static byte[] SignRecoveryCheckpoint(VaultProviderRecoveryCheckpoint checkpoint, ECDsa approvalKey)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        return VaultRecoverySignatures.Sign(approvalKey, "Embasic.VaultProviderRecovery.v1",
            writer => WriteRecoveryCheckpoint(writer, checkpoint));
    }

    /// <summary>Recover only into empty storage under a new identity, with no inherited receipt or root proof.</summary>
    public void Recover(VaultProviderRecoveryCheckpoint checkpoint, ReadOnlySpan<byte> signature,
        ReadOnlySpan<byte> approvalPublicKey, IVaultProviderRecoveryWitness witness, IVaultGenerationStore manifests)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(witness);
        ArgumentNullException.ThrowIfNull(manifests);
        if (_providerIdentity != checkpoint.ReplacementProvider || checkpoint.LostProvider == Guid.Empty ||
            checkpoint.LostProvider == checkpoint.ReplacementProvider ||
            checkpoint.VacantRevision is < 1 or long.MaxValue)
            throw new CryptographicException("Recovery requires a distinct, pinned replacement and vacant revision.");
        VaultRecoverySignatures.Verify(approvalPublicKey, signature, "Embasic.VaultProviderRecovery.v1",
            writer => WriteRecoveryCheckpoint(writer, checkpoint));
        if (witness.Read(checkpoint.Selected.WorkspaceId) != checkpoint ||
            manifests.Read(checkpoint.Selected)?.Stamp != checkpoint.Selected)
            throw new CryptographicException("Recovery checkpoint is not current or its manifest is unavailable.");

        Initialize(checkpoint.Selected, checkpoint.VacantRevision, () =>
        {
            if (Directory.EnumerateFileSystemEntries(_directory).Any())
                throw new IOException("Recovery requires an empty replacement provider directory.");
            if (witness.Read(checkpoint.Selected.WorkspaceId) != checkpoint)
                throw new CryptographicException("Independent recovery checkpoint changed.");
        });
    }

    private static void WriteRecoveryCheckpoint(BinaryWriter writer, VaultProviderRecoveryCheckpoint checkpoint)
    {
        writer.Write(checkpoint.LostProvider.ToByteArray());
        writer.Write(checkpoint.ReplacementProvider.ToByteArray());
        VaultRecoverySignatures.WriteStamp(writer, checkpoint.Selected);
        writer.Write(checkpoint.VacantRevision);
    }
}

/// <summary>Adapts an offline signature to the existing ERA1 approval reader. The provider still checks the live revision.</summary>
public sealed class VaultSignedRootApproval : IVaultRootReEnrollmentApprovalReader
{
    private const string Domain = "Embasic.VaultEraApproval.v1";
    private readonly VaultRootReEnrollmentApproval _approval;

    public VaultSignedRootApproval(VaultRootReEnrollmentApproval approval, ReadOnlySpan<byte> signature,
        ReadOnlySpan<byte> publicKey)
    {
        ArgumentNullException.ThrowIfNull(approval);
        VaultRecoverySignatures.Verify(publicKey, signature, Domain, writer => Write(writer, approval));
        _approval = approval;
    }

    public static byte[] Sign(VaultRootReEnrollmentApproval approval, ECDsa approvalKey)
    {
        ArgumentNullException.ThrowIfNull(approval);
        return VaultRecoverySignatures.Sign(approvalKey, Domain, writer => Write(writer, approval));
    }

    public VaultRootReEnrollmentApproval? Read(VaultGenerationStamp generation) =>
        _approval.Generation == generation ? _approval : null;

    private static void Write(BinaryWriter writer, VaultRootReEnrollmentApproval approval)
    {
        VaultRecoverySignatures.WriteStamp(writer, approval.Generation);
        writer.Write(approval.TombstoneRevision);
        VaultRecoverySignatures.WriteDigest(writer, approval.ArchiveSha256);
    }
}
