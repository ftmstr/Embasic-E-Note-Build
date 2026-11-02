using System.IO;
using System.Security.Cryptography;

namespace Embasic.ENote.Emd;

// Experimental ELV1: each folder/subfolder/note factor has its OWN two
// password-derived slots; no Windows DPAPI slot is present.
public static class LayerKeyVault
{
    public const int Size = 174;
    public static byte[] Create(LayerSecret factor, string password, string recovery)
    {
        if (password == recovery) throw new ArgumentException("Independent recovery required.");
        CheckPassword(password);
        CheckPassword(recovery);
        byte[] raw = factor.CopySecret();
        try
        {
            byte[] data = new byte[Size];
            "ELV1"u8.CopyTo(data);
            data[4] = 1;
            data[5] = (byte)factor.Kind;
            factor.Id.TryWriteBytes(data.AsSpan(6, 16));
            WriteSlot(data, 22, raw, password, 1);
            WriteSlot(data, 98, raw, recovery, 2);
            return data;
        }
        finally { CryptographicOperations.ZeroMemory(raw); }
    }

    public static LayerSecret Open(ReadOnlySpan<byte> envelope, Guid id,
        LayerKind kind, string credential, bool recovery = false)
    {
        CheckPassword(credential);
        if (envelope.Length != Size || !envelope[..4].SequenceEqual("ELV1"u8))
            throw new InvalidDataException("Unknown layer vault.");
        if (envelope[4] != 1) throw new NotSupportedException("Unknown vault version.");
        if (envelope[5] != (byte)kind || new Guid(envelope.Slice(6,16)) != id)
            throw new CryptographicException("Wrong lock identity.");
        int offset = recovery ? 98 : 22;
        byte[] key = PortableEmd.Derive(credential, envelope.Slice(offset,16).ToArray());
        byte[] clear = new byte[32];
        try
        {
            using var gcm = new AesGcm(key, 16);
            gcm.Decrypt(envelope.Slice(offset+16,12), envelope.Slice(offset+28,32),
                envelope.Slice(offset+60,16), clear, Aad(envelope, recovery ? (byte)2 : (byte)1));
            return new LayerSecret(id, kind, clear);
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(clear); }
    }

    public static byte[] Rotate(ReadOnlySpan<byte> envelope, Guid id, LayerKind kind,
        string currentCredential, string nextPassword, string nextRecovery, bool isRecovery = false)
    {
        using var factor = Open(envelope, id, kind, currentCredential, isRecovery);
        return Create(factor, nextPassword, nextRecovery);
    }

    private static byte[] Aad(ReadOnlySpan<byte> source, byte index)
    {
        byte[] result = new byte[23];
        source[..22].CopyTo(result);
        result[22] = index;
        return result;
    }

    private static void WriteSlot(byte[] data, int offset, byte[] clear,
        string password, byte index)
    {
        RandomNumberGenerator.Fill(data.AsSpan(offset, 28));
        byte[] key = PortableEmd.Derive(password, data.AsSpan(offset,16).ToArray());
        try
        {
            using var gcm = new AesGcm(key, 16);
            gcm.Encrypt(data.AsSpan(offset+16,12), clear,
                data.AsSpan(offset+28,32), data.AsSpan(offset+60,16), Aad(data,index));
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private static void CheckPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 12)
            throw new ArgumentException("Password too short.");
    }
}
