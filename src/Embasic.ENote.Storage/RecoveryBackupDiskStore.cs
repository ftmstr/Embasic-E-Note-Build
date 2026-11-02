using System.IO;
using System.Security.Cryptography;
using Embasic.ENote.Emd;

namespace Embasic.ENote.Storage;

/// <summary>
/// EXPERIMENTAL encrypted recovery-backup file store. Refuses overwrite and
/// writes ONLY authenticated ERB1 bytes. No user recovery UI or disk guarantee.
/// </summary>
public sealed class RecoveryBackupDiskStore
{
    public void CreateNew(string path, ReadOnlySpan<byte> protectedBackup, string verificationPassword)
    {
        string target = ValidatePath(path);
        byte[] encrypted = protectedBackup.ToArray();
        try
        {
            // Verify before a single byte is persisted. Never persist raw secret.
            byte[] restored = RecoveryBackupCodec.Open(encrypted, verificationPassword);
            CryptographicOperations.ZeroMemory(restored);

            if (File.Exists(target))
                throw new IOException("Backup exists; overwrite prohibited.");
            string temp = Path.Combine(Path.GetDirectoryName(target)!,
                "." + Path.GetFileName(target) + "." + Guid.NewGuid().ToString("N") + ".erbk-tmp");
            try
            {
                using (var file = new FileStream(temp, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    file.Write(encrypted);
                    file.Flush(flushToDisk: true);
                }
                File.Move(temp, target, overwrite: false);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }
        finally { CryptographicOperations.ZeroMemory(encrypted); }
    }

    public byte[] Restore(string path, string password)
    {
        string full = ValidatePath(path);
        byte[] encrypted = File.ReadAllBytes(full);
        try
        {
            return RecoveryBackupCodec.Open(encrypted, password);
        }
        finally { CryptographicOperations.ZeroMemory(encrypted); }
    }

    private static string ValidatePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string full = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(full), ".erbk", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Recovery backup extension must be .erbk.");
        string directory = Path.GetDirectoryName(full)!;
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException("Backup directory does not exist.");
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0 ||
            (File.Exists(full) && (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0))
            throw new UnauthorizedAccessException("Linked backup paths are rejected.");
        return full;
    }
}
