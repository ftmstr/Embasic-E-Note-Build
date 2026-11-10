using System.Security.Cryptography;

namespace Embasic.ENote.Storage;

/// <summary>Exact floor attestation signed by an operator key separate from the selector and provider.</summary>
public sealed record VaultRecoveryOperatorKeyTransition(long FirstRevision, byte[] NewPublicKey,
    byte[] OutgoingSignature, byte[] IncomingSignature);

public static class VaultRecoveryPinFloor
{
    private const string Domain = "Embasic.VaultRecoveryPinFloor.v1";
    private const string RotationDomain = "Embasic.VaultRecoveryOperatorRotation.v1";
    private const string AttestationDomain = "Embasic.VaultRecoveryOperatorAttestation.v1";
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

    public static VaultRecoveryOperatorKeyTransition AuthorizeRotation(Guid workspace, long firstRevision,
        ECDsa outgoing, ECDsa incoming)
    {
        var transition = VaultRecoverySelectorKeySchedule.Sign(workspace, firstRevision, outgoing, incoming, RotationDomain);
        return new(transition.FirstRevision, transition.NewPublicKey, transition.OutgoingSignature, transition.IncomingSignature);
    }

    public static VaultRecoveryPolicyPin Read(string path, Guid workspace, ReadOnlySpan<byte> operatorPublicKey) =>
        ReadRequired(path, workspace, operatorPublicKey, 1);

    /// <summary>Validate a rotated floor against a minimum supplied independently of the floor and journal.</summary>
    public static VaultRecoveryPolicyPin ReadRequired(string path, Guid workspace, ReadOnlySpan<byte> initialOperatorPublicKey,
        long minimumRevision, params VaultRecoveryOperatorKeyTransition[] transitions)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumRevision);
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
        if (floor.Workspace != workspace || floor.Revision < minimumRevision || file.ReadByte() != -1)
            throw new CryptographicException("Operator floor workspace or tail differs.");
        var keys = new VaultRecoverySelectorKeySchedule(workspace, initialOperatorPublicKey,
            transitions.Select(x => new VaultRecoverySelectorKeyTransition(x.FirstRevision, x.NewPublicKey,
                x.OutgoingSignature, x.IncomingSignature)), RotationDomain);
        VaultRecoverySignatures.Verify(keys.KeyAt(floor.Revision), signature, Domain, writer => Write(writer, floor));
        return floor;
    }

    /// <summary>Sign a fresh challenge for a remotely administered floor transport.</summary>
    public static byte[] Attest(VaultRecoveryPolicyPin floor, ReadOnlySpan<byte> challenge, ECDsa operatorKey)
    {
        Validate(floor);
        byte[] nonce = RequireChallenge(challenge);
        return VaultRecoverySignatures.Sign(operatorKey, AttestationDomain, writer =>
        {
            Write(writer, floor);
            writer.Write(nonce);
        });
    }

    public static void VerifyAttestation(VaultRecoveryPolicyPin floor, ReadOnlySpan<byte> challenge,
        ReadOnlySpan<byte> signature, ReadOnlySpan<byte> operatorPublicKey)
    {
        Validate(floor);
        byte[] nonce = RequireChallenge(challenge);
        VaultRecoverySignatures.Verify(operatorPublicKey, signature, AttestationDomain, writer =>
        {
            Write(writer, floor);
            writer.Write(nonce);
        });
    }

    private static byte[] RequireChallenge(ReadOnlySpan<byte> challenge)
    {
        if (challenge.Length != 32) throw new ArgumentException("Operator challenge must be 32 bytes.", nameof(challenge));
        return challenge.ToArray();
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
