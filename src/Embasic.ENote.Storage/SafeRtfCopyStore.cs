using System.IO;
using System.Security.Cryptography;

namespace Embasic.ENote.Storage;

/// <summary>
/// RTF source is READ-ONLY. Edits can only be written to a NEW .rtf path.
/// No replace or implicit conversion to DOCX/ODT is permitted.
/// SHA-256 comparison is a best-effort accidental-conflict guard, not a
/// cross-process atomic compare-and-swap security guarantee.
/// </summary>
public sealed class SafeRtfCopyStore
{
    public RtfSourceSnapshot Read(string path)
    {
        string full = ValidatePath(path, mustExist: true);
        byte[] bytes = File.ReadAllBytes(full);
        RequireRtfHeader(bytes);
        return new RtfSourceSnapshot(full, bytes,
            Convert.ToHexString(SHA256.HashData(bytes)));
    }

    public string SaveEditedCopy(RtfSourceSnapshot source, string destination, byte[] editedRtf)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(editedRtf);
        RequireRtfHeader(editedRtf);
        string target = ValidatePath(destination, mustExist: false);
        if (string.Equals(source.FilePath, target, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Original RTF is protected: choose a different file name.");
        EnsureOriginalUnchanged(source);

        string temp = Path.Combine(Path.GetDirectoryName(target)!,
            "." + Path.GetFileName(target) + "." + Guid.NewGuid().ToString("N") + ".enote-tmp");
        try
        {
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.WriteThrough))
            {
                output.Write(editedRtf);
                output.Flush(flushToDisk: true);
            }
            // Fail closed if a second application edited the source during preparation.
            EnsureOriginalUnchanged(source);
            // Never overwrite a destination, including a concurrently created one.
            File.Move(temp, target, overwrite: false);
            return Convert.ToHexString(SHA256.HashData(editedRtf));
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private static void EnsureOriginalUnchanged(RtfSourceSnapshot source)
    {
        string path = ValidatePath(source.FilePath, mustExist: true);
        byte[] current = File.ReadAllBytes(path);
        try
        {
            var expected = Convert.FromHexString(source.Sha256);
            if (!CryptographicOperations.FixedTimeEquals(
                expected, SHA256.HashData(current)))
                throw new IOException("RTF changed externally; the copy was not saved.");
        }
        finally { CryptographicOperations.ZeroMemory(current); }
    }

    private static string ValidatePath(string path, bool mustExist)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string full = Path.GetFullPath(path);
        if (!Path.GetExtension(full).Equals(".rtf", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Only .rtf is supported. DOCX/ODT must not be silently converted.");
        var folder = Path.GetDirectoryName(full)!;
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException(folder);

        // Reject known reparse/junction components in the selected folder chain.
        var root = Path.GetPathRoot(folder)!;
        var cursor = root;
        foreach (var part in folder[root.Length..]
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            cursor = Path.Combine(cursor, part);
            if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Linked directories are not supported.");
        }

        if (File.Exists(full))
        {
            if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Linked file is not supported.");
        }
        else if (mustExist) throw new FileNotFoundException("RTF not found.", full);

        return full;
    }

    private static void RequireRtfHeader(byte[] data)
    {
        // This is a fail-closed format gate, NOT a full RTF parser or malware scanner.
        if (data.Length < 6 ||
            data[0] != (byte)'{' || data[1] != (byte)'\\' ||
            data[2] != (byte)'r' || data[3] != (byte)'t' ||
            data[4] != (byte)'f' || data[5] < (byte)'0' || data[5] > (byte)'9')
            throw new InvalidDataException("Not an RTF document with a valid RTF version header.");
    }
}

public sealed record RtfSourceSnapshot(string FilePath, byte[] Bytes, string Sha256);
