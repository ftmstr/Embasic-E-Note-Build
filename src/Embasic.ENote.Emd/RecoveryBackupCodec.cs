using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Embasic.ENote.Emd;

/// <summary>
/// Experimental, password-protected recovery-secret backup. Returns encrypted
/// bytes only; caller must securely store its password and encrypted copy.
/// No GUI, persistence or secret logging is provided.
/// </summary>
public static class RecoveryBackupCodec
{
    private static readonly byte[] Signature = "ERB1"u8.ToArray();
    private const int HeaderBytes = 4 + 1 + PortableEmd.SaltBytes + 4 + 4 + 1 + PortableEmd.NonceBytes;
    internal const int TotalBytes = HeaderBytes + PortableEmd.KeyBytes + PortableEmd.TagBytes;

    public static byte[] Create(ReadOnlySpan<byte> recoverySecret, string backupPassword)
    {
        if (recoverySecret.Length != PortableEmd.KeyBytes)
            throw new ArgumentException("Recovery secret must be 256 bits.", nameof(recoverySecret));
        ValidatePassword(backupPassword);
        byte[] salt = RandomNumberGenerator.GetBytes(PortableEmd.SaltBytes);
        byte[] passKey = PortableEmd.Derive(backupPassword, salt);
        try
        {
            byte[] nonce = RandomNumberGenerator.GetBytes(PortableEmd.NonceBytes);
            using var writerStream = new MemoryStream();
            using (var writer = new BinaryWriter(writerStream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(Signature);
                writer.Write((byte)1);
                writer.Write(salt);
                writer.Write(PortableEmd.MemoryKiB);
                writer.Write(PortableEmd.Passes);
                writer.Write((byte)PortableEmd.Parallelism);
                writer.Write(nonce);
            }
            byte[] header = writerStream.ToArray();
            byte[] ciphertext = new byte[PortableEmd.KeyBytes];
            byte[] tag = new byte[PortableEmd.TagBytes];
            using (var aead = new AesGcm(passKey, PortableEmd.TagBytes))
                aead.Encrypt(nonce, recoverySecret, ciphertext, tag, header);
            using var output = new MemoryStream(TotalBytes);
            output.Write(header);
            output.Write(ciphertext);
            output.Write(tag);
            return output.ToArray();
        }
        finally { CryptographicOperations.ZeroMemory(passKey); }
    }

    public static byte[] Open(ReadOnlySpan<byte> backup, string backupPassword)
    {
        ValidatePassword(backupPassword);
        if (backup.Length != TotalBytes)
            throw new InvalidDataException("Invalid recovery backup size.");
        byte[] raw = backup.ToArray();
        try
        {
            using var stream = new MemoryStream(raw, writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8);
            if (!reader.ReadBytes(4).AsSpan().SequenceEqual(Signature))
                throw new InvalidDataException("Unknown recovery backup magic.");
            if (reader.ReadByte() != 1)
                throw new NotSupportedException("Unsupported recovery backup version.");
            byte[] salt = reader.ReadBytes(PortableEmd.SaltBytes);
            if (reader.ReadInt32() != PortableEmd.MemoryKiB ||
                reader.ReadInt32() != PortableEmd.Passes ||
                reader.ReadByte() != PortableEmd.Parallelism)
                throw new NotSupportedException("Unsupported recovery backup KDF parameters.");
            byte[] nonce = reader.ReadBytes(PortableEmd.NonceBytes);
            byte[] ciphertext = reader.ReadBytes(PortableEmd.KeyBytes);
            byte[] tag = reader.ReadBytes(PortableEmd.TagBytes);
            byte[] passKey = PortableEmd.Derive(backupPassword, salt);
            try
            {
                byte[] secret = new byte[PortableEmd.KeyBytes];
                try
                {
                    using var aead = new AesGcm(passKey, PortableEmd.TagBytes);
                    aead.Decrypt(nonce, ciphertext, tag, secret, raw.AsSpan(0, HeaderBytes));
                    return secret;
                }
                catch
                {
                    CryptographicOperations.ZeroMemory(secret);
                    throw;
                }
            }
            finally { CryptographicOperations.ZeroMemory(passKey); }
        }
        finally { CryptographicOperations.ZeroMemory(raw); }
    }

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 12)
            throw new ArgumentException("Backup password must contain at least 12 characters.");
    }
}
