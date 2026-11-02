using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Embasic.ENote.Emd;

/// <summary>Experimental in-memory EMD1 codec. Not a production-ready file format.</summary>
public static partial class EmdDraftCodec
{
    internal const int KeySize = 32;
    internal const int NonceSize = 12;
    internal const int TagSize = 16;
    internal const int MaxCleartextBytes = 16 * 1024 * 1024;
    internal const int MaxWrappedBytes = 4096;
    internal static readonly byte[] Magic = "EMD1"u8.ToArray();

    public static EmdDraftCreation Encrypt(
        string markdown, IFileKeyProtector protector, bool createRecoveryKey = false)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(protector);
        byte[] clear = Encoding.UTF8.GetBytes(markdown);
        byte[] dataKey = RandomNumberGenerator.GetBytes(KeySize);
        byte[]? recoveryKey = null;
        try
        {
            if (clear.Length > MaxCleartextBytes)
                throw new ArgumentOutOfRangeException(nameof(markdown));
            byte[] wrapped = protector.Protect(dataKey);
            if (wrapped.Length is < 1 or > MaxWrappedBytes)
                throw new CryptographicException("Invalid wrapped key size.");

            using var headerBuffer = new MemoryStream();
            using (var header = new BinaryWriter(headerBuffer, Encoding.UTF8, leaveOpen: true))
            {
                header.Write(Magic);
                header.Write((byte)1); // Draft version
                header.Write((byte)1); // DPAPI CurrentUser slot
                header.Write(Guid.NewGuid().ToByteArray());
                header.Write((ushort)wrapped.Length);
                header.Write(wrapped);
                header.Write((byte)(createRecoveryKey ? 1 : 0));

                if (createRecoveryKey)
                {
                    recoveryKey = RandomNumberGenerator.GetBytes(KeySize);
                    byte[] recoveryNonce = RandomNumberGenerator.GetBytes(NonceSize);
                    byte[] recoveryCipher = new byte[KeySize];
                    byte[] recoveryTag = new byte[TagSize];
                    using (var aead = new AesGcm(recoveryKey, TagSize))
                        aead.Encrypt(recoveryNonce, dataKey, recoveryCipher, recoveryTag);
                    header.Write(recoveryNonce);
                    header.Write(recoveryCipher);
                    header.Write(recoveryTag);
                }

                byte[] payloadNonce = RandomNumberGenerator.GetBytes(NonceSize);
                header.Write(payloadNonce);
                header.Write(clear.Length);
                header.Flush();

                byte[] aad = headerBuffer.ToArray();
                byte[] ciphertext = new byte[clear.Length];
                byte[] tag = new byte[TagSize];
                using (var aead = new AesGcm(dataKey, TagSize))
                    aead.Encrypt(payloadNonce, clear, ciphertext, tag, aad);
                using var output = new MemoryStream();
                output.Write(aad);
                output.Write(ciphertext);
                output.Write(tag);
                return new EmdDraftCreation(output.ToArray(), recoveryKey);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }
}

public sealed record EmdDraftCreation(byte[] Envelope, byte[]? RecoveryKey);
