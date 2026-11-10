using System.Security.Cryptography;

namespace Embasic.ENote.Storage;

/// <summary>Exact floor attestation signed by an operator key separate from the selector and provider.</summary>
public static class VaultRecoveryPinFloor
{
    private const string Domain = "Embasic.VaultRecoveryPinFloor.v1";
    private const int Size = 4 + 16 + 8 + 32 + 64;

    public static byte[] Issue(VaultRecoveryPolicyPin floor, ECDsa operatorKey)
    {
        Validate(floor);
        byte[] signature = VaultRecoverySignatures.Sign(operatorKey, Domain, writer => Write(writer, floor));
        using var stream = new MemoryStream(Size);
        using var writer = new BinaryWriter(stream);
        writer.Write("EPF1"u8);
        Write(writer, floor);
        writer.Write(signature);
        return stream.ToArray();
    }

    public static VaultRecoveryPolicyPin Read(string path, Guid workspace, ReadOnlySpan<byte> operatorPublicKey)
    {
        string full = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(full) ?? throw new ArgumentException("Floor requires a directory.", nameof(path));
        var files = new VaultAtomicFileDirectory(directory);
        using var file = files.OpenRead(Path.GetFileName(full)) ?? throw new IOException("Operator floor is unavailable.");
        if (file.Length != Size) throw new InvalidDataException("Unexpected operator floor length.");
        using var reader = new BinaryReader(file);
        if (!reader.ReadBytes(4).AsSpan().SequenceEqual("EPF1"u8))
            throw new InvalidDataException("Unexpected operator floor format.");
        var floor = new VaultRecoveryPolicyPin(new Guid(reader.ReadBytes(16)), reader.ReadInt64(),
            Convert.ToHexString(reader.ReadBytes(32)));
        byte[] signature = reader.ReadBytes(64);
        Validate(floor);
        if (floor.Workspace != workspace || file.ReadByte() != -1)
            throw new CryptographicException("Operator floor workspace or tail differs.");
        VaultRecoverySignatures.Verify(operatorPublicKey, signature, Domain, writer => Write(writer, floor));
        return floor;
    }

    private static void Validate(VaultRecoveryPolicyPin floor)
    {
        ArgumentNullException.ThrowIfNull(floor);
        if (floor.Workspace == Guid.Empty || floor.Revision < 1 ||
            VaultRekeyInventory.NormalizeDigest(floor.Sha256) != floor.Sha256)
            throw new CryptographicException("Invalid operator floor.");
    }

    private static void Write(BinaryWriter writer, VaultRecoveryPolicyPin floor)
    {
        writer.Write(floor.Workspace.ToByteArray());
        writer.Write(floor.Revision);
        VaultRecoverySignatures.WriteDigest(writer, floor.Sha256);
    }
}
