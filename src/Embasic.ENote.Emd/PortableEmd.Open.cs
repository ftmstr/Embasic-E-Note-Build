using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Embasic.ENote.Emd;

public static partial class PortableEmd
{
    /// <summary>Choose exactly one credential. Recovery and Windows-local
    /// modes do not silently fall back to a different identity.</summary>
    public static string Decrypt(ReadOnlySpan<byte> envelope,
        string? password = null, byte[]? recoverySecret = null,
        IFileKeyProtector? windowsKeyProtector = null)
    {
        int methods = (password is null ? 0 : 1) +
            (recoverySecret is null ? 0 : 1) + (windowsKeyProtector is null ? 0 : 1);
        if (methods != 1)
            throw new ArgumentException("Select exactly one key-unwrapping credential.");
        if (envelope.Length < 4 + 1 + 1 + 16 + SaltBytes + 4 + 4 + 1 +
            NonceBytes + KeyBytes + TagBytes + 1 + 1 + NonceBytes + 4 + TagBytes ||
            envelope.Length > MaxClearBytes + MaxWrappedBytes + 512)
            throw new InvalidDataException("Invalid EMD2 envelope length.");

        byte[] source = envelope.ToArray();
        byte[]? dataKey = null, plain = null;
        try
        {
            using var stream = new MemoryStream(source, writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            if (!ReadExact(reader, 4).AsSpan().SequenceEqual(Signature))
                throw new InvalidDataException("Invalid EMD2 magic.");
            if (reader.ReadByte() != 2)
                throw new NotSupportedException("Unknown EMD schema version.");
            if (reader.ReadByte() != 1)
                throw new NotSupportedException("Unknown AEAD algorithm.");
            _ = ReadExact(reader, 16); // UUID included in AAD
            byte[] salt = ReadExact(reader, SaltBytes);
            if (reader.ReadInt32() != MemoryKiB || reader.ReadInt32() != Passes ||
                reader.ReadByte() != Parallelism)
                throw new NotSupportedException("Unsupported Argon2id parameters.");
            byte[] passNonce = ReadExact(reader, NonceBytes);
            byte[] passCipher = ReadExact(reader, KeyBytes);
            byte[] passTag = ReadExact(reader, TagBytes);

            int hasRecovery = reader.ReadByte();
            if (hasRecovery is not (0 or 1))
                throw new InvalidDataException("Invalid recovery slot.");
            byte[]? rNonce = null, rCipher = null, rTag = null;
            if (hasRecovery == 1)
            {
                rNonce = ReadExact(reader, NonceBytes);
                rCipher = ReadExact(reader, KeyBytes);
                rTag = ReadExact(reader, TagBytes);
            }
            int hasLocal = reader.ReadByte();
            if (hasLocal is not (0 or 1))
                throw new InvalidDataException("Invalid local key slot.");
            byte[]? local = null;
            if (hasLocal == 1)
            {
                int n = reader.ReadUInt16();
                if (n is < 1 or > MaxWrappedBytes)
                    throw new InvalidDataException("Invalid local key size.");
                local = ReadExact(reader, n);
            }

            byte[] nonce = ReadExact(reader, NonceBytes);
            int clearLength = reader.ReadInt32();
            if (clearLength < 0 || clearLength > MaxClearBytes ||
                stream.Length - stream.Position != (long)clearLength + TagBytes)
                throw new InvalidDataException("Invalid EMD2 ciphertext length.");
            byte[] aad = source.AsSpan(0, (int)stream.Position).ToArray();
            byte[] ciphertext = ReadExact(reader, clearLength);
            byte[] tag = ReadExact(reader, TagBytes);

            if (password is not null)
            {
                byte[] passKey = Derive(password, salt);
                try
                {
                    dataKey = new byte[KeyBytes];
                    using var aead = new AesGcm(passKey, TagBytes);
                    aead.Decrypt(passNonce, passCipher, passTag, dataKey);
                }
                finally { CryptographicOperations.ZeroMemory(passKey); }
            }
            else if (recoverySecret is not null)
            {
                if (hasRecovery == 0 || recoverySecret.Length != KeyBytes)
                    throw new CryptographicException("No valid recovery credential.");
                dataKey = new byte[KeyBytes];
                using var aead = new AesGcm(recoverySecret, TagBytes);
                aead.Decrypt(rNonce!, rCipher!, rTag!, dataKey);
            }
            else
            {
                if (hasLocal == 0)
                    throw new CryptographicException("No Windows local key slot.");
                dataKey = windowsKeyProtector!.Unprotect(local!);
            }

            if (dataKey.Length != KeyBytes)
                throw new CryptographicException("Invalid file key.");
            plain = new byte[clearLength];
            using (var payload = new AesGcm(dataKey, TagBytes))
                payload.Decrypt(nonce, ciphertext, tag, plain, aad);
            return new UTF8Encoding(false, true).GetString(plain);
        }
        finally
        {
            if (dataKey is not null) CryptographicOperations.ZeroMemory(dataKey);
            if (plain is not null) CryptographicOperations.ZeroMemory(plain);
            CryptographicOperations.ZeroMemory(source);
        }
    }

    private static byte[] ReadExact(BinaryReader reader, int count)
    {
        byte[] value = reader.ReadBytes(count);
        if (value.Length != count)
            throw new InvalidDataException("Truncated encrypted document.");
        return value;
    }
}
