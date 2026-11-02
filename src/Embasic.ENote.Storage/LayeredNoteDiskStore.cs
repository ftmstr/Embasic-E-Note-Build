using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using Embasic.ENote.Emd;

namespace Embasic.ENote.Storage;

/// <summary>Experimental ELK1 ciphertext-only storage, not UI-approved.
/// SHA-256 compare-before-replace is NOT cross-process atomic CAS.</summary>
public sealed class LayeredNoteDiskStore
{
    private const int MaxBytes = 32 * 1024 * 1024;
    private static readonly ConcurrentDictionary<string, object> Gates = new(StringComparer.OrdinalIgnoreCase);

    public EncryptedNoteSnapshot Read(string path)
    {
        string full = PathCheck(path);
        byte[] data = File.ReadAllBytes(full);
        CheckEnvelope(data);
        return new EncryptedNoteSnapshot(data, Digest(data));
    }

    public string Save(string path, PortableEmdResult emd2, IReadOnlyList<LayerSecret> factors,
        string? expectedSha256 = null)
    {
        string full = PathCheck(path);
        byte[] encrypted = LayeredEmd3.Seal(emd2, factors);
        try
        {
            CheckEnvelope(encrypted);
            lock (Gates.GetOrAdd(full, _ => new object()))
            {
                Verify(full, expectedSha256);
                string temp = Path.Combine(Path.GetDirectoryName(full)!,
                    "." + Path.GetFileName(full) + "." + Guid.NewGuid().ToString("N") + ".elk-tmp");
                try
                {
                    using (var stream = new FileStream(temp, FileMode.CreateNew,
                        FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                    {
                        stream.Write(encrypted);
                        stream.Flush(flushToDisk: true);
                    }
                    Verify(full, expectedSha256);
                    if (expectedSha256 is null) File.Move(temp, full, overwrite: false);
                    else File.Replace(temp, full, null, ignoreMetadataErrors: false);
                    return Digest(encrypted);
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
        }
        finally { CryptographicOperations.ZeroMemory(encrypted); }
    }

    private static void Verify(string path, string? expected)
    {
        if (expected is null)
        {
            if (File.Exists(path)) throw new IOException("Create-only: file already exists.");
            return;
        }
        if (expected.Length != 64 || !expected.All(Uri.IsHexDigit))
            throw new ArgumentException("Invalid expected SHA-256.");
        if (!File.Exists(path)) throw new IOException("Encrypted note disappeared.");
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("Reparse-point target denied.");
        byte[] current = File.ReadAllBytes(path);
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(expected), SHA256.HashData(current)))
                throw new IOException("Encrypted note changed; refusing overwrite.");
        }
        finally { CryptographicOperations.ZeroMemory(current); }
    }

    private static string PathCheck(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string full = Path.GetFullPath(path);
        if (!Path.GetExtension(full).Equals(".emd", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Only .emd layered notes.");
        string dir = Path.GetDirectoryName(full)!;
        if (!Directory.Exists(dir)) throw new DirectoryNotFoundException(dir);
        if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0 ||
            (File.Exists(full) && (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0))
            throw new UnauthorizedAccessException("Linked path is not supported.");
        return full;
    }

    private static void CheckEnvelope(ReadOnlySpan<byte> data)
    {
        if (data.Length < 60 || data.Length > MaxBytes || !data[..4].SequenceEqual("ELK1"u8))
            throw new InvalidDataException("Layered encrypted note expected.");
    }

    private static string Digest(ReadOnlySpan<byte> data) => Convert.ToHexString(SHA256.HashData(data));
}
