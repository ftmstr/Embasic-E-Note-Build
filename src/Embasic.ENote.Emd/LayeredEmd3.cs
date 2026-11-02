using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Embasic.ENote.Emd;

/// <summary>
/// Experimental EMD3 outer encryption. Each independently supplied 256-bit
/// layer secret encrypts the ENTIRE inner container. The inner EMD2 key
/// slots (including DPAPI) cannot be reached without every outer layer.
/// NOT a production workspace key hierarchy or a disk storage API.
/// </summary>
public static class LayeredEmd3
{
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private const int HeaderBytes = 4 + 1 + 1 + 16 + NonceBytes + 4;
    private const int MaxBytes = 32 * 1024 * 1024;

    public static byte[] Seal(PortableEmdResult document, IReadOnlyList<LayerSecret> outerToInner)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidateLayers(outerToInner);
        byte[] working = document.Envelope;
        try
        {
            if (!working.AsSpan(0, 4).SequenceEqual("EMD2"u8))
                throw new InvalidDataException("Expected inner EMD2.");
            for (int i = outerToInner.Count - 1; i >= 0; i--)
            {
                LayerSecret layer = outerToInner[i];
                byte[] nonce = RandomNumberGenerator.GetBytes(NonceBytes);
                byte[] header = new byte[HeaderBytes];
                using (var headerStream = new MemoryStream(header, writable: true))
                using (var writer = new BinaryWriter(headerStream, Encoding.UTF8))
                {
                    writer.Write("ELK1"u8.ToArray());
                    writer.Write((byte)1);
                    writer.Write((byte)layer.Kind);
                    writer.Write(layer.Id.ToByteArray());
                    writer.Write(nonce);
                    writer.Write(working.Length);
                }

                byte[] cipher = new byte[working.Length];
                byte[] tag = new byte[TagBytes];
                byte[] key = layer.CopySecret();
                try
                {
                    using var aead = new AesGcm(key, TagBytes);
                    aead.Encrypt(nonce, working, cipher, tag, header);
                }
                finally { CryptographicOperations.ZeroMemory(key); }

                byte[] packed = new byte[HeaderBytes + cipher.Length + TagBytes];
                Buffer.BlockCopy(header, 0, packed, 0, HeaderBytes);
                Buffer.BlockCopy(cipher, 0, packed, HeaderBytes, cipher.Length);
                Buffer.BlockCopy(tag, 0, packed, HeaderBytes + cipher.Length, TagBytes);
                CryptographicOperations.ZeroMemory(working);
                working = packed;
                if (working.Length > MaxBytes)
                    throw new InvalidDataException("Experimental wrapper size limit exceeded.");
            }
            byte[] result = working;
            working = Array.Empty<byte>();
            return result;
        }
        finally { CryptographicOperations.ZeroMemory(working); }
    }

    public static string Open(ReadOnlySpan<byte> wrapped, IReadOnlyList<LayerSecret> outerToInner,
        string? password = null, byte[]? recoverySecret = null,
        IFileKeyProtector? windowsKeyProtector = null)
    {
        ValidateLayers(outerToInner);
        if (wrapped.Length < HeaderBytes + TagBytes || wrapped.Length > MaxBytes)
            throw new InvalidDataException("Invalid outer envelope length.");
        byte[] current = wrapped.ToArray();
        try
        {
            foreach (LayerSecret layer in outerToInner)
            {
                if (current.Length < HeaderBytes + TagBytes ||
                    !current.AsSpan(0, 4).SequenceEqual("ELK1"u8))
                    throw new CryptographicException("Required layer missing.");
                using var reader = new BinaryReader(new MemoryStream(current, writable: false),
                    Encoding.UTF8);
                _ = reader.ReadBytes(4);
                if (reader.ReadByte() != 1)
                    throw new NotSupportedException("Unknown layer schema.");
                if (reader.ReadByte() != (byte)layer.Kind)
                    throw new CryptographicException("Wrong layer kind.");
                if (new Guid(reader.ReadBytes(16)) != layer.Id)
                    throw new CryptographicException("Wrong layer identity.");
                byte[] nonce = reader.ReadBytes(NonceBytes);
                int length = reader.ReadInt32();
                if (length < 0 || length > MaxBytes ||
                    current.Length != HeaderBytes + (long)length + TagBytes)
                    throw new InvalidDataException("Invalid layer length.");

                byte[] plain = new byte[length];
                byte[] key = layer.CopySecret();
                try
                {
                    using var aead = new AesGcm(key, TagBytes);
                    aead.Decrypt(nonce, current.AsSpan(HeaderBytes, length),
                        current.AsSpan(HeaderBytes + length, TagBytes), plain,
                        current.AsSpan(0, HeaderBytes));
                }
                catch
                {
                    CryptographicOperations.ZeroMemory(plain);
                    throw;
                }
                finally { CryptographicOperations.ZeroMemory(key); }
                CryptographicOperations.ZeroMemory(current);
                current = plain;
            }
            // No alternate EMD2 DPAPI/recovery slot can bypass any outer layer.
            if (current.Length < 4 || !current.AsSpan(0, 4).SequenceEqual("EMD2"u8))
                throw new CryptographicException("Incomplete layer authorization.");
            return PortableEmd.Decrypt(current, password, recoverySecret, windowsKeyProtector);
        }
        finally { CryptographicOperations.ZeroMemory(current); }
    }

    private static void ValidateLayers(IReadOnlyList<LayerSecret> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        if (layers.Count == 0)
            throw new ArgumentException("An independent outer lock is required.", nameof(layers));
        var seen = new HashSet<Guid>();
        for (int i = 0; i < layers.Count; i++)
        {
            LayerSecret layer = layers[i] ?? throw new ArgumentException("Null lock.");
            if (!seen.Add(layer.Id)) throw new ArgumentException("Duplicate lock identity.");
            if (layer.Kind == LayerKind.Subfolder &&
                !layers.Take(i).Any(x => x.Kind is LayerKind.Folder or LayerKind.Subfolder))
                throw new ArgumentException("A subfolder lock needs its folder ancestor.");
            if (layer.Kind == LayerKind.NoteAdditional && i != layers.Count - 1)
                throw new ArgumentException("Extra note lock must be the innermost layer.");
            // Accessing an already disposed layer is not an acceptable grant.
            byte[] check = layer.CopySecret();
            CryptographicOperations.ZeroMemory(check);
        }
    }
}

public enum LayerKind : byte { Folder = 1, Subfolder = 2, NoteAdditional = 3 }

/// <summary>Owns an independent cryptographic factor. Caller controls
/// derivation/persistence, which are NOT implemented in this draft.</summary>
public sealed class LayerSecret : IDisposable
{
    private readonly object _gate = new();
    private readonly byte[] _secret;
    private bool _disposed;
    public Guid Id { get; }
    public LayerKind Kind { get; }

    public LayerSecret(Guid id, LayerKind kind, ReadOnlySpan<byte> secret)
    {
        if (id == Guid.Empty || secret.Length != 32 ||
            kind is not (LayerKind.Folder or LayerKind.Subfolder or LayerKind.NoteAdditional))
            throw new ArgumentException("Invalid lock key metadata.");
        Id = id;
        Kind = kind;
        _secret = secret.ToArray();
    }

    public static LayerSecret Generate(LayerKind kind)
    {
        byte[] random = RandomNumberGenerator.GetBytes(32);
        try { return new LayerSecret(Guid.NewGuid(), kind, random); }
        finally { CryptographicOperations.ZeroMemory(random); }
    }

    internal byte[] CopySecret()
    {
        lock (_gate)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LayerSecret));
            return _secret.ToArray();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            CryptographicOperations.ZeroMemory(_secret);
        }
        GC.SuppressFinalize(this);
    }
}
