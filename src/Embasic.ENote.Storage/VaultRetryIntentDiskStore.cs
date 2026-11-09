using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;

namespace Embasic.ENote.Storage;

/// <summary>
/// Authenticated local retry journal for a protected directory and independently
/// protected 32-byte key. The journal cannot detect deletion or supply monotonicity.
/// </summary>
public sealed class VaultRetryIntentDiskStore : IVaultRetryIntentStore, IDisposable
{
    private const int PayloadSize = 104;
    private const int TagSize = 32;
    private const int RecordSize = PayloadSize + TagSize;
    private static ReadOnlySpan<byte> Domain => "ENRTY001"u8;

    private readonly VaultAtomicFileDirectory _files;
    private readonly byte[] _key;
    private bool _disposed;

    public VaultRetryIntentDiskStore(string directory, ReadOnlySpan<byte> key)
    {
        if (key.Length != TagSize) throw new ArgumentException("A 32-byte journal key is required.", nameof(key));
        _files = new VaultAtomicFileDirectory(directory);
        _key = key.ToArray();
    }

    public VaultRetryIntent? Read(Guid workspaceId, long expectedEpoch)
    {
        lock (_key) ObjectDisposedException.ThrowIf(_disposed, this);
        if (workspaceId == Guid.Empty) throw new ArgumentException("Missing workspace.", nameof(workspaceId));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedEpoch);
        using var file = _files.OpenRead(NameFor(workspaceId, expectedEpoch));
        if (file is null) return null;
        if (file.Length != RecordSize) throw new InvalidDataException("Invalid retry intent length.");
        Span<byte> bytes = stackalloc byte[RecordSize];
        file.ReadExactly(bytes);
        if (file.ReadByte() != -1) throw new InvalidDataException("Retry intent length changed during the read.");

        Span<byte> tag = stackalloc byte[TagSize];
        ComputeTag(bytes[..PayloadSize], tag);
        if (!CryptographicOperations.FixedTimeEquals(tag, bytes[PayloadSize..]))
            throw new CryptographicException("Retry intent authentication failed.");

        if (!bytes[..8].SequenceEqual(Domain) ||
            new Guid(bytes[8..24]) != workspaceId ||
            BinaryPrimitives.ReadInt64LittleEndian(bytes[24..32]) != expectedEpoch ||
            expectedEpoch == long.MaxValue ||
            BinaryPrimitives.ReadInt64LittleEndian(bytes[32..40]) != expectedEpoch + 1)
            throw new CryptographicException("Retry intent does not match its slot or version.");

        return new VaultRetryIntent(
            new VaultGenerationStamp(workspaceId, expectedEpoch, Convert.ToHexString(bytes[40..72])),
            new VaultGenerationStamp(workspaceId, expectedEpoch + 1, Convert.ToHexString(bytes[72..104])));
    }

    public void Record(VaultRetryIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        var expected = intent.Expected;
        byte[] bytes = new byte[RecordSize];
        Domain.CopyTo(bytes);
        expected.WorkspaceId.TryWriteBytes(bytes.AsSpan(8, 16));
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(24, 8), expected.Epoch);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(32, 8), intent.Candidate.Epoch);
        Convert.FromHexString(expected.ManifestSha256).CopyTo(bytes, 40);
        Convert.FromHexString(intent.Candidate.ManifestSha256).CopyTo(bytes, 72);
        ComputeTag(bytes.AsSpan(0, PayloadSize), bytes.AsSpan(PayloadSize));

        _files.Publish(NameFor(expected.WorkspaceId, expected.Epoch), file => file.Write(bytes), () =>
        {
            var published = Read(expected.WorkspaceId, expected.Epoch);
            if (published is null) return false;
            if (published != intent)
                throw new CryptographicException("A different retry intent already occupies this generation.");
            return true;
        });
    }

    public void Dispose()
    {
        lock (_key)
        {
            CryptographicOperations.ZeroMemory(_key);
            _disposed = true;
        }
    }

    private void ComputeTag(ReadOnlySpan<byte> payload, Span<byte> tag)
    {
        lock (_key)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            HMACSHA256.HashData(_key, payload, tag);
        }
    }

    private static string NameFor(Guid workspaceId, long epoch) =>
        $"{workspaceId:N}-{epoch.ToString(CultureInfo.InvariantCulture)}.eri";
}
