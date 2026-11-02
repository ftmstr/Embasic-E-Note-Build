using System.IO;
using System.Security.Cryptography;
using Embasic.ENote.Emd;

namespace Embasic.ENote.Storage;

/// <summary>Experimental create-only ELH1 encrypted workspace ancestry persistence.
/// No plaintext paths, labels or keys are written. Not a Windows ACL guarantee.</summary>
public sealed class WorkspaceLockIndexDiskStore
{
    public void CreateNew(string path, Guid workspaceId, LayerSecret rootFactor,
        IReadOnlyList<WorkspaceLockEntry> nodes)
    {
        string full = CheckPath(path);
        byte[] encrypted = WorkspaceLockIndex.Protect(workspaceId, rootFactor, nodes);
        try
        {
            // Require successful authentication BEFORE persistence.
            _ = WorkspaceLockIndex.Open(encrypted, workspaceId, rootFactor);
            if (File.Exists(full)) throw new IOException("Encrypted workspace index already exists.");
            string staging = Path.Combine(Path.GetDirectoryName(full)!,
                "." + Path.GetFileName(full) + "." + Guid.NewGuid().ToString("N") + ".elh-tmp");
            try
            {
                using (var stream = new FileStream(staging, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    stream.Write(encrypted);
                    stream.Flush(flushToDisk: true);
                }
                File.Move(staging, full, overwrite: false);
            }
            finally { if (File.Exists(staging)) File.Delete(staging); }
        }
        finally { CryptographicOperations.ZeroMemory(encrypted); }
    }

    public IReadOnlyList<WorkspaceLockEntry> Open(string path, Guid workspaceId,
        LayerSecret rootFactor)
    {
        string full = CheckPath(path);
        byte[] encrypted = File.ReadAllBytes(full);
        try { return WorkspaceLockIndex.Open(encrypted, workspaceId, rootFactor); }
        finally { CryptographicOperations.ZeroMemory(encrypted); }
    }

    private static string CheckPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string full = Path.GetFullPath(path);
        if (!Path.GetExtension(full).Equals(".elh", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Encrypted workspace index requires .elh extension.");
        string directory = Path.GetDirectoryName(full)!;
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0 ||
            (File.Exists(full) && (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0))
            throw new UnauthorizedAccessException("Linked index paths are rejected.");
        return full;
    }
}
