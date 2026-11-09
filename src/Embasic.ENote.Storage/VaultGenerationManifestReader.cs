using System.Security.Cryptography;
using Embasic.ENote.Emd;

namespace Embasic.ENote.Storage;

public sealed partial class VaultGenerationManifest
{
    /// <summary>
    /// Restores canonical generation data only after the entire bounded input
    /// matches the supplied stamp. The caller obtains that stamp independently
    /// when opening committed data. The stream remains open.
    /// </summary>
    public static VaultGenerationManifest ReadVerified(Stream source, VaultGenerationStamp expected)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(expected);
        if (!source.CanRead || !source.CanSeek)
            throw new ArgumentException("A readable, seekable generation stream is required.", nameof(source));

        long position = source.Position;
        long length = source.Length;
        if (position < 0 || length < position ||
            length - position < HeaderBytes + EntryFixedBytes + 2 || length - position > MaximumBytes)
            throw new InvalidDataException("Invalid generation manifest length.");

        byte[] bytes = new byte[(int)(length - position)];
        source.ReadExactly(bytes);
        if (source.ReadByte() != -1)
            throw new InvalidDataException("Generation source length changed during the read.");
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes),
                Convert.FromHexString(expected.ManifestSha256)))
            throw new CryptographicException("Generation manifest digest does not match the supplied stamp.");

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = new BinaryReader(stream, Utf8);
            string domain = ReadString(reader, Utf8.GetByteCount(Domain));
            if (domain is not (Domain or BoundDomain))
                throw new InvalidDataException("Unknown generation manifest version.");
            var workspace = new Guid(ReadBytes(reader, 16));
            long epoch = reader.ReadInt64();
            if (workspace != expected.WorkspaceId || epoch != expected.Epoch)
                throw new CryptographicException("Generation workspace or epoch does not match the supplied stamp.");

            VaultGenerationBindings? bindings = null;
            if (domain == BoundDomain)
            {
                string catalog = Convert.ToHexString(ReadBytes(reader, 32));
                string? rekey = reader.ReadByte() switch
                {
                    0 => null,
                    1 => Convert.ToHexString(ReadBytes(reader, 32)),
                    _ => throw new InvalidDataException("Invalid manifest binding flag.")
                };
                bindings = new VaultGenerationBindings(catalog, rekey);
            }
            int count = reader.ReadInt32();
            if (count <= 0 || count > (stream.Length - stream.Position) / (EntryFixedBytes + 2))
                throw new InvalidDataException("Invalid generation grant count.");

            var grants = new List<VaultAnchorGrant>();
            for (int i = 0; i < count; i++)
            {
                var id = new Guid(ReadBytes(reader, 16));
                var kind = (LayerKind)reader.ReadByte();
                string path = ReadString(reader, MaximumPathBytes);
                var identity = new WindowsVaultFileIdentity(reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadUInt64());
                var pin = VaultSnapshotPin.RestoreVerifiedDigest(id, kind, epoch, ReadBytes(reader, 32));
                grants.Add(new VaultAnchorGrant(workspace, id, kind, pin, epoch, path)
                    { BoundVaultFileIdentity = identity });
            }
            if (stream.Position != stream.Length)
                throw new InvalidDataException("Trailing bytes in generation manifest.");

            var manifest = Create(workspace, epoch, grants, bindings);
            if (manifest.Stamp != expected)
                throw new InvalidDataException("Noncanonical generation manifest.");
            return manifest;
        }
        catch (Exception error) when (error is ArgumentException or EndOfStreamException or FormatException)
        {
            throw new InvalidDataException("Malformed generation manifest.", error);
        }
    }

    private static string ReadString(BinaryReader reader, int maximumBytes)
    {
        int bytes = reader.Read7BitEncodedInt();
        if (bytes <= 0 || bytes > maximumBytes)
            throw new InvalidDataException("Invalid manifest string length.");
        return Utf8.GetString(ReadBytes(reader, bytes));
    }

    private static byte[] ReadBytes(BinaryReader reader, int count)
    {
        if (count > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException("Truncated generation manifest.");
        byte[] bytes = new byte[count];
        reader.BaseStream.ReadExactly(bytes);
        return bytes;
    }
}
