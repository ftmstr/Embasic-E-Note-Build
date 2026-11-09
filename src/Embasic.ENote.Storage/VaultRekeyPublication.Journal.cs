using System.Security.Cryptography;

namespace Embasic.ENote.Storage;

public sealed partial class VaultRekeyPublication
{
    private const int MaximumPlanBytes = 4 * 1024 * 1024;

    private void SavePlan(VaultGenerationManifest source, VaultRekeyCommitment commitment, VaultGenerationManifest next)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var factors = commitment.Inventory.Artifacts.Where(x => x.Kind == VaultRekeyArtifactKind.FactorVault)
            .Select(x => x.RequiredFactors.Single()).OrderBy(x => x).ToArray();
        using var payload = new MemoryStream();
        using (var writer = new BinaryWriter(payload, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("ERJ1"u8);
            writer.Write(next.Stamp.WorkspaceId.ToByteArray());
            WriteStamp(writer, source.Stamp);
            WriteStamp(writer, next.Stamp);
            writer.Write(factors.Length);
            foreach (var factor in factors) writer.Write(factor.ToByteArray());
            writer.Write(commitment.Targets.Count);
            foreach (var item in commitment.Targets.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                writer.Write(item.Key);
                writer.Write(Convert.FromHexString(item.Value));
                if (payload.Length > MaximumPlanBytes - 32) throw new InvalidDataException("Rekey plan exceeds its bound.");
            }
        }
        byte[] bytes = payload.ToArray();
        byte[] tag = HMACSHA256.HashData(_journalKey, bytes);
        byte[] sealedPlan = [.. bytes, .. tag];
        string name = JournalName(next.Stamp);
        _store.Stage(next);
        _journal.Publish(name, file => file.Write(sealedPlan), () =>
        {
            using var file = _journal.OpenRead(name);
            if (file is null) return false;
            if (!ReadBounded(file).AsSpan().SequenceEqual(sealedPlan))
                throw new CryptographicException("Existing rekey plan differs from its exact registration.");
            return true;
        });
    }

    private Plan ReadPlan(VaultGenerationStamp candidate)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var file = _journal.OpenRead(JournalName(candidate)) ?? throw new InvalidDataException("Candidate has no registered restart plan.");
        byte[] bytes = ReadBounded(file);
        int payloadSize = bytes.Length - 32;
        if (!CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(_journalKey, bytes.AsSpan(0, payloadSize)),
                bytes.AsSpan(payloadSize)))
            throw new CryptographicException("Rekey restart plan authentication failed.");
        using var stream = new MemoryStream(bytes, 0, payloadSize, writable: false);
        using var reader = new BinaryReader(stream);
        if (!reader.ReadBytes(4).AsSpan().SequenceEqual("ERJ1"u8) ||
            new Guid(reader.ReadBytes(16)) != candidate.WorkspaceId)
            throw new InvalidDataException("Unknown rekey restart plan.");
        var expected = ReadStamp(reader, candidate.WorkspaceId);
        if (ReadStamp(reader, candidate.WorkspaceId) != candidate)
            throw new CryptographicException("Restart plan refers to another candidate.");
        _ = new VaultRetryIntent(expected, candidate);
        int factorCount = ReadCount(reader, 16);
        var factors = new Guid[factorCount];
        for (int i = 0; i < factors.Length; i++) factors[i] = new Guid(reader.ReadBytes(16));
        int targetCount = ReadCount(reader, 33);
        var targets = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < targetCount; i++)
            if (!targets.TryAdd(reader.ReadString(), Convert.ToHexString(reader.ReadBytes(32))))
                throw new InvalidDataException("Duplicate rekey plan target.");
        if (stream.Position != stream.Length || factors.Distinct().Count() != factors.Length)
            throw new InvalidDataException("Invalid rekey plan tail.");
        return new Plan(expected, factors, targets);
    }

    private static byte[] ReadBounded(FileStream file)
    {
        long size = file.Length;
        if (size < 140 || size > MaximumPlanBytes) throw new InvalidDataException("Invalid rekey restart plan size.");
        byte[] bytes = new byte[(int)size];
        file.ReadExactly(bytes);
        if (file.ReadByte() != -1) throw new InvalidDataException("Rekey plan grew during read.");
        return bytes;
    }

    private static int ReadCount(BinaryReader reader, int minimumItemSize)
    {
        int count = reader.ReadInt32();
        if (count <= 0 || count > (reader.BaseStream.Length - reader.BaseStream.Position) / minimumItemSize)
            throw new InvalidDataException("Invalid rekey plan item count.");
        return count;
    }

    private static string JournalName(VaultGenerationStamp stamp) =>
        stamp.WorkspaceId.ToString("N") + "-" + stamp.ManifestSha256 + ".erj";

    private static void WriteStamp(BinaryWriter writer, VaultGenerationStamp stamp)
    {
        writer.Write(stamp.Epoch);
        writer.Write(Convert.FromHexString(stamp.ManifestSha256));
    }

    private static VaultGenerationStamp ReadStamp(BinaryReader reader, Guid workspace) =>
        new(workspace, reader.ReadInt64(), Convert.ToHexString(reader.ReadBytes(32)));

    private sealed record Plan(VaultGenerationStamp Expected, Guid[] Factors, Dictionary<string, string> Targets);
}
