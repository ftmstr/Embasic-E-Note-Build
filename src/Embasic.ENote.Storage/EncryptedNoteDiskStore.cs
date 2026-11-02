using System.IO;
using System.Security.Cryptography;
using Embasic.ENote.Emd;

namespace Embasic.ENote.Storage;

/// <summary>
/// Experimental ciphertext-only .emd file store. Compare-before-replace is
/// optimistic, not an OS-wide atomic compare-and-swap. Do not use for user
/// documents or keys until real filesystem/backup acceptance is complete.
/// </summary>
public sealed class EncryptedNoteDiskStore
{
    private const int MaxEnvelopeBytes = 16 * 1024 * 1024 + 4096 + 512;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object>
        Gates = new(StringComparer.OrdinalIgnoreCase);

    public EncryptedNoteSnapshot Read(string path)
    {
        string full = ValidatePath(path);
        byte[] envelope = File.ReadAllBytes(full);
        ValidateEnvelope(envelope);
        return new EncryptedNoteSnapshot(envelope, Digest(envelope));
    }

    /// <param name="expectedDigest">
    /// Null means CREATE ONLY; non-null means REPLACE ONLY when digest matches.
    /// This only protects cooperating writers in one process; external changes
    /// between comparison and File.Replace are a documented residual race.
    /// </param>
    public string Save(string path, PortableEmdResult encryptedDocument, string? expectedDigest = null)
    {
        string full = ValidatePath(path);
        ArgumentNullException.ThrowIfNull(encryptedDocument);
        byte[] ciphertext = encryptedDocument.Envelope;
        ValidateEnvelope(ciphertext);
        var gate = Gates.GetOrAdd(full, _ => new object());
        try
        {
            lock (gate)
            {
                VerifyExpected(full, expectedDigest);
                string directory = Path.GetDirectoryName(full)!;
                string temporary = Path.Combine(directory, "." + Path.GetFileName(full) +
                    "." + Guid.NewGuid().ToString("N") + ".enc-tmp");
                try
                {
                    using (var writer = new FileStream(temporary, FileMode.CreateNew,
                        FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                    {
                        writer.Write(ciphertext);
                        writer.Flush(flushToDisk: true);
                    }

                    VerifyExpected(full, expectedDigest);
                    if (expectedDigest is null)
                        File.Move(temporary, full, overwrite: false);
                    else
                        File.Replace(temporary, full, null, ignoreMetadataErrors: false);
                    return Digest(ciphertext);
                }
                finally
                {
                    // Temp files contain ONLY authenticated encrypted envelope bytes.
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
            }
        }
        finally { CryptographicOperations.ZeroMemory(ciphertext); }
    }

    private static void VerifyExpected(string path, string? expectedDigest)
    {
        if (expectedDigest is null)
        {
            if (File.Exists(path))
                throw new IOException("Target already exists; refusing implicit overwrite.");
            return;
        }
        if (expectedDigest.Length != 64 ||
            !expectedDigest.All(Uri.IsHexDigit))
            throw new ArgumentException("Expected SHA-256 must be 64 hexadecimal characters.");

        if (!File.Exists(path))
            throw new IOException("Target disappeared before replacement.");

        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("Symbolic file links are not supported.");

        byte[] current = File.ReadAllBytes(path);
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(expectedDigest), SHA256.HashData(current)))
                throw new IOException("File changed outside this edit; refusing overwrite.");
        }
        finally { CryptographicOperations.ZeroMemory(current); }
    }

    private static string ValidatePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string full = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(full), ".emd", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Encrypted store only accepts .emd targets.");

        string parent = Path.GetDirectoryName(full)!;
        if (!Directory.Exists(parent))
            throw new DirectoryNotFoundException("Target folder does not exist.");
        // This does not provide a race-free handle-based sandbox.
        if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("Linked parent directories not supported.");
        if (File.Exists(full) &&
            (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("Linked targets not supported.");
        return full;
    }

    private static void ValidateEnvelope(ReadOnlySpan<byte> data)
    {
        if (data.Length < 64 || data.Length > MaxEnvelopeBytes ||
            !data[..4].SequenceEqual("EMD2"u8) || data[4] != 2)
            throw new InvalidDataException("Only experimental encrypted EMD2 envelopes are accepted.");
    }

    private static string Digest(ReadOnlySpan<byte> buffer) =>
        Convert.ToHexString(SHA256.HashData(buffer));
}

public sealed record EncryptedNoteSnapshot(byte[] Envelope, string Sha256);
