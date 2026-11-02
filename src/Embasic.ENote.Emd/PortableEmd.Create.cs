using System.IO;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Embasic.ENote.Emd;

/// <summary>Experimental EMD2. Distinct from Tur 03 EMD1; NEVER enable real notes yet.</summary>
public static partial class PortableEmd
{
    internal const int KeyBytes = 32, NonceBytes = 12, TagBytes = 16, SaltBytes = 16;
    internal const int MaxClearBytes = 16 * 1024 * 1024, MaxWrappedBytes = 4096;
    internal const int MemoryKiB = 32768, Passes = 3, Parallelism = 1;
    internal static readonly byte[] Signature = "EMD2"u8.ToArray();

    public static PortableEmdResult Encrypt(
        string markdown, string password, bool createRecoveryKey = true,
        IFileKeyProtector? windowsKeyProtector = null)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        if (string.IsNullOrWhiteSpace(password) || password.Length < 12)
            throw new ArgumentException("A passphrase of at least 12 characters is required.", nameof(password));

        byte[] clear = new UTF8Encoding(false, true).GetBytes(markdown);
        byte[] dataKey = RandomNumberGenerator.GetBytes(KeyBytes);
        byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
        byte[]? recoverySecret = null;
        try
        {
            if (clear.Length > MaxClearBytes)
                throw new ArgumentOutOfRangeException(nameof(markdown), "Experimental document size cap.");
            byte[] passKey = Derive(password, salt);
            try
            {
                byte[] passNonce = RandomNumberGenerator.GetBytes(NonceBytes);
                byte[] encryptedKey = new byte[KeyBytes], passTag = new byte[TagBytes];
                using (var wrap = new AesGcm(passKey, TagBytes))
                    wrap.Encrypt(passNonce, dataKey, encryptedKey, passTag);

                byte[]? localWrap = windowsKeyProtector?.Protect(dataKey);
                if (localWrap is { Length: < 1 or > MaxWrappedBytes })
                    throw new CryptographicException("Invalid local key wrapper length.");

                using var headerStream = new MemoryStream();
                using var writer = new BinaryWriter(headerStream, Encoding.UTF8, leaveOpen: true);
                writer.Write(Signature);
                writer.Write((byte)2);        // schema version
                writer.Write((byte)1);        // AES-256-GCM algorithm ID
                writer.Write(Guid.NewGuid().ToByteArray());
                writer.Write(salt);
                writer.Write(MemoryKiB);
                writer.Write(Passes);
                writer.Write((byte)Parallelism);
                writer.Write(passNonce);
                writer.Write(encryptedKey);
                writer.Write(passTag);
                writer.Write((byte)(createRecoveryKey ? 1 : 0));
                if (createRecoveryKey)
                {
                    recoverySecret = RandomNumberGenerator.GetBytes(KeyBytes);
                    byte[] rNonce = RandomNumberGenerator.GetBytes(NonceBytes);
                    byte[] rCipher = new byte[KeyBytes], rTag = new byte[TagBytes];
                    using (var wrap = new AesGcm(recoverySecret, TagBytes))
                        wrap.Encrypt(rNonce, dataKey, rCipher, rTag);
                    writer.Write(rNonce);
                    writer.Write(rCipher);
                    writer.Write(rTag);
                }

                writer.Write((byte)(localWrap is null ? 0 : 1));
                if (localWrap is not null)
                {
                    writer.Write((ushort)localWrap.Length);
                    writer.Write(localWrap);
                }
                byte[] payloadNonce = RandomNumberGenerator.GetBytes(NonceBytes);
                writer.Write(payloadNonce);
                writer.Write(clear.Length);
                writer.Flush();
                byte[] aad = headerStream.ToArray();
                byte[] cipher = new byte[clear.Length], tag = new byte[TagBytes];
                using (var payload = new AesGcm(dataKey, TagBytes))
                    payload.Encrypt(payloadNonce, clear, cipher, tag, aad);
                using var result = new MemoryStream();
                result.Write(aad);
                result.Write(cipher);
                result.Write(tag);
                return new PortableEmdResult(result.ToArray(), recoverySecret);
            }
            finally { CryptographicOperations.ZeroMemory(passKey); }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    internal static byte[] Derive(string password, byte[] salt)
    {
        byte[] passwordBytes = new UTF8Encoding(false, true).GetBytes(password);
        try
        {
            using var kdf = new Argon2id(passwordBytes)
            {
                Salt = salt,
                MemorySize = MemoryKiB,
                Iterations = Passes,
                DegreeOfParallelism = Parallelism
            };
            return kdf.GetBytes(KeyBytes);
        }
        finally { CryptographicOperations.ZeroMemory(passwordBytes); }
    }
}

public sealed record PortableEmdResult(byte[] Envelope, byte[]? RecoverySecret);
