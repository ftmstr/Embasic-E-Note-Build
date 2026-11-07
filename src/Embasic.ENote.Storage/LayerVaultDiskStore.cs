using System.IO;
using System.Security.Cryptography;
using Embasic.ENote.Emd;

namespace Embasic.ENote.Storage;

/// <summary>Experimental create-only persistent encrypted ELV1 lock factors.
/// No raw keys written. Not a Windows ACL or cross-process security boundary.</summary>
public sealed class LayerVaultDiskStore
{
    public void CreateNew(string path, LayerSecret factor, string password, string recoveryPassword)
    {
        string full = Validate(path);
        byte[] envelope = LayerKeyVault.Create(factor, password, recoveryPassword);
        try
        {
            // Validate the encrypted result before persistence.
            using var verified = LayerKeyVault.Open(envelope, factor.Id, factor.Kind, password);
            if (File.Exists(full)) throw new IOException("Vault already exists.");
            string temp = Path.Combine(Path.GetDirectoryName(full)!,
                "." + Path.GetFileName(full) + "." + Guid.NewGuid().ToString("N") + ".elkey-tmp");
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    stream.Write(envelope);
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temp, full, overwrite: false);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        finally { CryptographicOperations.ZeroMemory(envelope); }
    }

    public LayerSecret Unlock(string path, Guid id, LayerKind kind, string credential,
        bool recovery = false)
    {
        string full = Validate(path);
        byte[] encrypted = File.ReadAllBytes(full);
        try { return LayerKeyVault.Open(encrypted, id, kind, credential, recovery); }
        finally { CryptographicOperations.ZeroMemory(encrypted); }
    }

    /// <summary>
    /// Optional experimental pinned read. The caller MUST obtain the pin and
    /// monotonic trustedMinimumEpoch from independently rollback-protected
    /// storage. This class does not provide such storage, and the legacy
    /// Unlock method remains unguarded. NOT a production security boundary.
    /// </summary>
    public LayerSecret UnlockPinned(string path, VaultSnapshotPin trustedPin,
        string credential, long trustedMinimumEpoch, bool recovery = false)
    {
        ArgumentNullException.ThrowIfNull(trustedPin);
        string full = Validate(path);
        byte[] encrypted = File.ReadAllBytes(full);
        try { return trustedPin.OpenPinned(encrypted, credential, trustedMinimumEpoch, recovery); }
        finally { CryptographicOperations.ZeroMemory(encrypted); }
    }

    private static string Validate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string full = Path.GetFullPath(path);
        if (!Path.GetExtension(full).Equals(".elkey", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Only encrypted .elkey vaults.");
        string dir = Path.GetDirectoryName(full)!;
        if (!Directory.Exists(dir)) throw new DirectoryNotFoundException(dir);
        if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0 ||
            (File.Exists(full) && (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0))
            throw new UnauthorizedAccessException("Linked vault path rejected.");
        return full;
    }
}
