using System.Security.Cryptography;

namespace Embasic.ENote.Storage;

/// <summary>The authorized identity and digest pins for a single service key epoch.</summary>
public sealed record VaultAuthorityKeyEpoch(Guid Workspace, long Epoch, string SigningSha256,
    string ReaderSha256, string PublisherSha256);

/// <summary>Validates separately provisioned keys against an independently selected signed epoch.</summary>
public sealed class VaultAuthorityKeyCustody : IDisposable
{
    private const string Domain = "Embasic.VaultAuthorityKeys.v1";
    private readonly ECDsa _signer;
    private readonly byte[] _reader;
    private readonly byte[] _publisher;
    private bool _disposed;
    public VaultAuthorityKeyEpoch Epoch { get; }

    private VaultAuthorityKeyCustody(VaultAuthorityKeyEpoch epoch, ECDsa signer, byte[] reader, byte[] publisher)
    {
        Epoch = epoch;
        _signer = signer;
        _reader = reader;
        _publisher = publisher;
    }

    public static VaultAuthorityKeyEpoch Describe(Guid workspace, long epoch, ECDsa signer,
        ReadOnlySpan<byte> reader, ReadOnlySpan<byte> publisher)
    {
        ArgumentNullException.ThrowIfNull(signer);
        VaultAuthorityPipeProtocol.RequireKey(reader);
        VaultAuthorityPipeProtocol.RequireKey(publisher);
        if (workspace == Guid.Empty || epoch < 1 || !VaultAuthorityPipeProtocol.IsP256(signer) ||
            CryptographicOperations.FixedTimeEquals(reader, publisher))
            throw new ArgumentException("Invalid workspace key epoch.");
        return new VaultAuthorityKeyEpoch(workspace, epoch, Digest(signer.ExportSubjectPublicKeyInfo()),
            Digest(reader), Digest(publisher));
    }

    public static byte[] Approve(VaultAuthorityKeyEpoch epoch, ECDsa approvalKey)
    {
        ArgumentNullException.ThrowIfNull(epoch);
        return VaultRecoverySignatures.Sign(approvalKey, Domain, writer => WriteEpoch(writer, epoch));
    }

    public static VaultAuthorityKeyCustody Open(VaultAuthorityKeyEpoch approved, Guid expectedWorkspace,
        long expectedEpoch, ReadOnlySpan<byte> approvalSignature, ReadOnlySpan<byte> approvalPublicKey,
        ReadOnlySpan<byte> servicePrivateKey, ReadOnlySpan<byte> reader, ReadOnlySpan<byte> publisher)
    {
        ArgumentNullException.ThrowIfNull(approved);
        if (approved.Workspace != expectedWorkspace || approved.Epoch != expectedEpoch)
            throw new CryptographicException("Service keys do not match the independently selected epoch.");
        VaultRecoverySignatures.Verify(approvalPublicKey, approvalSignature, Domain, writer => WriteEpoch(writer, approved));
        var signer = ECDsa.Create();
        try
        {
            signer.ImportPkcs8PrivateKey(servicePrivateKey, out int consumed);
            if (consumed != servicePrivateKey.Length || Describe(expectedWorkspace, expectedEpoch, signer, reader, publisher) != approved)
                throw new CryptographicException("Service signing or role key digest differs.");
            return new VaultAuthorityKeyCustody(approved, signer, reader.ToArray(), publisher.ToArray());
        }
        catch
        {
            signer.Dispose();
            throw;
        }
    }

    public VaultAuthorityPipeService CreateWindowsService(string pipeName, VaultWindowsPipeBoundary boundary,
        VaultGenerationRecovery recovery, VaultRekeyPublication publication, Embasic.ENote.Emd.LayerSecret root)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new VaultAuthorityPipeService(pipeName, Epoch.Workspace, _reader, _publisher, _signer,
            recovery, publication, boundary, root);
    }

    public static void VerifySuccessor(VaultAuthorityKeyEpoch current, VaultAuthorityKeyEpoch next)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(next);
        if (current.Workspace != next.Workspace || current.Epoch == long.MaxValue || next.Epoch != current.Epoch + 1 ||
            string.Equals(current.SigningSha256, next.SigningSha256, StringComparison.OrdinalIgnoreCase) ||
            new[] { current.ReaderSha256, current.PublisherSha256 }.Intersect(
                new[] { next.ReaderSha256, next.PublisherSha256 }, StringComparer.OrdinalIgnoreCase).Any())
            throw new CryptographicException("Rotation requires the next epoch and fresh signing and role keys.");
    }

    private static string Digest(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static void WriteEpoch(BinaryWriter writer, VaultAuthorityKeyEpoch epoch)
    {
        writer.Write(epoch.Workspace.ToByteArray());
        writer.Write(epoch.Epoch);
        VaultRecoverySignatures.WriteDigest(writer, epoch.SigningSha256);
        VaultRecoverySignatures.WriteDigest(writer, epoch.ReaderSha256);
        VaultRecoverySignatures.WriteDigest(writer, epoch.PublisherSha256);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _signer.Dispose();
        CryptographicOperations.ZeroMemory(_reader);
        CryptographicOperations.ZeroMemory(_publisher);
    }
}
