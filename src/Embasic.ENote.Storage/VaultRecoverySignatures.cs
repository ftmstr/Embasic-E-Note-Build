using System.Security.Cryptography;

namespace Embasic.ENote.Storage;

internal static class VaultRecoverySignatures
{
    internal static byte[] Sign(ECDsa key, string domain, Action<BinaryWriter> write)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!VaultAuthorityPipeProtocol.IsP256(key)) throw new CryptographicException("Expected a P-256 approval key.");
        return key.SignData(Encode(domain, write), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    internal static void Verify(ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> signature,
        string domain, Action<BinaryWriter> write)
    {
        using var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(publicKey, out int consumed);
        if (consumed != publicKey.Length || signature.Length != 64 || !VaultAuthorityPipeProtocol.IsP256(key) ||
            !key.VerifyData(Encode(domain, write), signature, HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            throw new CryptographicException("Independent approval signature is invalid.");
    }

    internal static void WriteStamp(BinaryWriter writer, VaultGenerationStamp stamp)
    {
        writer.Write(stamp.WorkspaceId.ToByteArray());
        writer.Write(stamp.Epoch);
        WriteDigest(writer, stamp.ManifestSha256);
    }

    internal static void WriteDigest(BinaryWriter writer, string digest) =>
        writer.Write(Convert.FromHexString(VaultRekeyInventory.NormalizeDigest(digest)));

    private static byte[] Encode(string domain, Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(domain);
        write(writer);
        return stream.ToArray();
    }
}
