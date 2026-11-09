using System.Security.Cryptography;

namespace Embasic.ENote.Storage;

public sealed partial class VaultTransitionFilePrototype
{
    private const int DigestSize = 32;
    private const int StampSize = sizeof(long) + DigestSize;
    private const int HeaderSize = 4 + 16;
    private const int MinimumStateBytes = HeaderSize + StampSize + 3 + sizeof(long) + DigestSize;
    private const int MaximumStateBytes = MinimumStateBytes + 2 * StampSize + 16 + 2 * DigestSize;

    private ProviderState Read(Guid workspaceId)
    {
        using var file = new FileStream(Slot(workspaceId, "state"), FileMode.Open, FileAccess.Read, FileShare.Read);
        long length = file.Length;
        if (length is < MinimumStateBytes or > MaximumStateBytes)
            throw new InvalidDataException("Invalid provider state length.");
        byte[] bytes = new byte[(int)length];
        file.ReadExactly(bytes);
        if (file.ReadByte() != -1) throw new InvalidDataException("Provider state grew during read.");

        int payloadLength = bytes.Length - DigestSize;
        if (!CryptographicOperations.FixedTimeEquals(Authenticate(bytes.AsSpan(0, payloadLength)),
                bytes.AsSpan(payloadLength)))
            throw new CryptographicException("Provider state authentication failed.");

        using var stream = new MemoryStream(bytes, 0, payloadLength, writable: false);
        using var reader = new BinaryReader(stream);
        if (!reader.ReadBytes(4).AsSpan().SequenceEqual("EVP1"u8) ||
            new Guid(reader.ReadBytes(16)) != workspaceId)
            throw new InvalidDataException("Invalid provider state identity.");

        var current = ReadStamp(reader, workspaceId);
        var pending = ReadFlag(reader) ? new VaultRetryIntent(current, ReadStamp(reader, workspaceId)) : null;
        var committed = ReadFlag(reader) ? new VaultRetryIntent(ReadStamp(reader, workspaceId), current) : null;
        long revision = reader.ReadInt64();
        VaultRootRecoveryProof? proof = ReadFlag(reader) ? new(current, new Guid(reader.ReadBytes(16)),
            ReadDigest(reader), ReadDigest(reader)) : null;
        if (revision < 0 || (proof is not null && (revision == 0 || proof.RootFactorId == Guid.Empty)) ||
            stream.Position != stream.Length)
            throw new InvalidDataException("Invalid provider recovery state.");

        return new ProviderState(new VaultTransitionState(current, pending, committed),
            new VaultRootRecoveryAuthorization(revision, proof));
    }

    private void Publish(ProviderState state, bool replace = true)
    {
        var transition = state.Transition;
        string target = Slot(transition.Current.WorkspaceId, "state");
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using var payload = new MemoryStream(MaximumStateBytes);
        using (var writer = new BinaryWriter(payload, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("EVP1"u8);
            writer.Write(transition.Current.WorkspaceId.ToByteArray());
            WriteStamp(writer, transition.Current);
            writer.Write(transition.Pending is not null);
            if (transition.Pending is { } pending) WriteStamp(writer, pending.Candidate);
            writer.Write(transition.LastCommitted is not null);
            if (transition.LastCommitted is { } committed) WriteStamp(writer, committed.Expected);
            writer.Write(state.Recovery.Revision);
            writer.Write(state.Recovery.Proof is not null);
            if (state.Recovery.Proof is { } proof)
            {
                writer.Write(proof.RootFactorId.ToByteArray());
                writer.Write(Convert.FromHexString(proof.BackupSha256));
                writer.Write(Convert.FromHexString(proof.IndexSha256));
            }
        }

        var bytes = payload.GetBuffer().AsSpan(0, (int)payload.Length);
        byte[] tag = Authenticate(bytes);
        bool ownsTemporary = false;
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                ownsTemporary = true;
                file.Write(bytes);
                file.Write(tag);
                file.Flush(flushToDisk: true);
            }
            try { File.Move(temporary, target, overwrite: replace); }
            catch (IOException) when (replace)
            {
                throw new VaultGenerationOutcomeUnknownException("Provider publication was not acknowledged.");
            }
        }
        finally
        {
            try { if (ownsTemporary) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private byte[] Authenticate(ReadOnlySpan<byte> payload)
    {
        lock (_keyGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return HMACSHA256.HashData(_key, payload);
        }
    }

    private static bool ReadFlag(BinaryReader reader) => reader.ReadByte() switch
    {
        0 => false,
        1 => true,
        _ => throw new InvalidDataException("Invalid provider state flag.")
    };

    private static VaultGenerationStamp ReadStamp(BinaryReader reader, Guid workspaceId) =>
        new(workspaceId, reader.ReadInt64(), ReadDigest(reader));

    private static string ReadDigest(BinaryReader reader) => Convert.ToHexString(reader.ReadBytes(DigestSize));

    private static void WriteStamp(BinaryWriter writer, VaultGenerationStamp stamp)
    {
        writer.Write(stamp.Epoch);
        writer.Write(Convert.FromHexString(stamp.ManifestSha256));
    }

    private static void ValidateProof(VaultGenerationStamp expected, VaultRootRecoveryProof proof)
    {
        if (proof.Generation != expected || proof.RootFactorId == Guid.Empty ||
            !IsDigest(proof.BackupSha256) || !IsDigest(proof.IndexSha256))
            throw new ArgumentException("Root recovery proof must match the selected generation.", nameof(proof));

        static bool IsDigest(string? digest) => digest is { Length: 64 } &&
            digest.All(c => Uri.IsHexDigit(c) && !char.IsLower(c));
    }
}
