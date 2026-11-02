using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Embasic.ENote.Emd;

public static partial class EmdDraftCodec
{
    public static string Decrypt(ReadOnlySpan<byte> envelope,
        IFileKeyProtector protector, byte[]? recoveryKey = null)
    {
        ArgumentNullException.ThrowIfNull(protector);
        if (envelope.Length < 4 + 1 + 1 + 16 + 2 + 1 + NonceSize + 4 + TagSize ||
            envelope.Length > MaxCleartextBytes + MaxWrappedBytes + 256)
            throw new InvalidDataException("Invalid draft EMD length.");

        byte[] source = envelope.ToArray();
        byte[]? dataKey = null;
        byte[]? plain = null;
        try
        {
            using var stream = new MemoryStream(source, writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            if (!reader.ReadBytes(4).AsSpan().SequenceEqual(Magic))
                throw new InvalidDataException("Invalid EMD signature.");
            if (reader.ReadByte() != 1)
                throw new NotSupportedException("Unsupported EMD draft version.");
            if (reader.ReadByte() != 1)
                throw new NotSupportedException("Unsupported EMD key slot.");
            _ = ReadExact(reader, 16); // File UUID
            int wrappedSize = reader.ReadUInt16();
            if (wrappedSize is < 1 or > MaxWrappedBytes)
                throw new InvalidDataException("Invalid wrapped key size.");
            byte[] wrapped = ReadExact(reader, wrappedSize);
            int recoverySlot = reader.ReadByte();
            if (recoverySlot is not (0 or 1))
                throw new InvalidDataException("Invalid recovery slot.");
            byte[]? rNonce = null;
            byte[]? rCipher = null;
            byte[]? rTag = null;
            if (recoverySlot == 1)
            {
                rNonce = ReadExact(reader, NonceSize);
                rCipher = ReadExact(reader, KeySize);
                rTag = ReadExact(reader, TagSize);
            }

            byte[] nonce = ReadExact(reader, NonceSize);
            int payloadLength = reader.ReadInt32();
            if (payloadLength < 0 || payloadLength > MaxCleartextBytes ||
                stream.Length - stream.Position != (long)payloadLength + TagSize)
                throw new InvalidDataException("Invalid payload length.");

            byte[] aad = source.AsSpan(0, (int)stream.Position).ToArray();
            byte[] ciphertext = ReadExact(reader, payloadLength);
            byte[] tag = ReadExact(reader, TagSize);

            if (recoveryKey is not null)
            {
                if (recoveryKey.Length != KeySize || recoverySlot != 1)
                    throw new CryptographicException("Invalid recovery slot or key.");
                dataKey = new byte[KeySize];
                using var recoveryAead = new AesGcm(recoveryKey, TagSize);
                recoveryAead.Decrypt(rNonce!, rCipher!, rTag!, dataKey);
            }
            else dataKey = protector.Unprotect(wrapped);

            if (dataKey.Length != KeySize)
                throw new CryptographicException("Invalid unwrapped key size.");
            plain = new byte[payloadLength];
            using (var aead = new AesGcm(dataKey, TagSize))
                aead.Decrypt(nonce, ciphertext, tag, plain, aad);
            return new UTF8Encoding(false, true).GetString(plain);
        }
        finally
        {
            if (plain is not null) CryptographicOperations.ZeroMemory(plain);
            if (dataKey is not null) CryptographicOperations.ZeroMemory(dataKey);
            CryptographicOperations.ZeroMemory(source);
        }
    }

    private static byte[] ReadExact(BinaryReader reader, int length)
    {
        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
            throw new InvalidDataException("Truncated EMD container.");
        return bytes;
    }
}
