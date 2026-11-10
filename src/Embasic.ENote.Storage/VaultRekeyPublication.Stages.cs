using System.Security.Cryptography;
using System.Text;
using Embasic.ENote.Emd;

namespace Embasic.ENote.Storage;

public sealed partial class VaultRekeyPublication
{
    private const string LiveStageSuffix = ".enote-sync";

    internal string StageName(VaultGenerationStamp stamp, string relativePath, Guid nonce)
    {
        using var encoded = new MemoryStream();
        using (var writer = new BinaryWriter(encoded, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("Embasic.LiveSync.Stage.v1");
            VaultRecoverySignatures.WriteStamp(writer, stamp);
            writer.Write(relativePath);
            writer.Write(nonce.ToByteArray());
        }
        return "." + nonce.ToString("N") + "." +
            Convert.ToHexString(HMACSHA256.HashData(_journalKey, encoded.ToArray())) + LiveStageSuffix;
    }

    // Reclaim the selected generation and its authenticated direct parent only.
    private int ReclaimLiveStages(VaultGenerationStamp stamp, VaultGenerationStamp parent, VaultWorkspaceSnapshot snapshot, Action requireCurrent)
    {
        int count = 0;
        var byDirectory = snapshot.Documents.GroupBy(document =>
            Path.GetDirectoryName(snapshot.LiveDocumentPath(_liveRoot, document))!, StringComparer.Ordinal);
        foreach (var directory in byDirectory)
        {
            var files = new VaultAtomicFileDirectory(directory.Key);
            foreach (string path in Directory.EnumerateFiles(directory.Key, "*" + LiveStageSuffix))
            {
                string name = Path.GetFileName(path);
                if (name.Length != 98 + LiveStageSuffix.Length || name[0] != '.' || name[33] != '.' ||
                    !Guid.TryParseExact(name.AsSpan(1, 32), "N", out var nonce))
                    continue;
                if (!directory.Any(document =>
                        string.Equals(name, StageName(stamp, document.RelativePath, nonce), StringComparison.Ordinal) ||
                        string.Equals(name, StageName(parent, document.RelativePath, nonce), StringComparison.Ordinal)))
                    continue;
                using (var held = OpenLiveStage(path, allowDelete: true))
                {
                    if (held.Length > LayeredEmd3.MaxBytes)
                        throw new InvalidDataException("Authenticated live stage exceeds the ciphertext bound.");
                    int length = (int)Math.Min(held.Length, 4);
                    Span<byte> header = new byte[length];
                    held.ReadExactly(header);
                    if (!header.SequenceEqual("ELK1"u8[..length]))
                        throw new InvalidDataException("Authenticated live stage is not an ELK1 prefix.");
                    requireCurrent();
                    if (OperatingSystem.IsWindows())
                    {
                        VaultWindowsStageFile.MarkForDeletion(held.SafeFileHandle);
                        count++;
                        continue;
                    }
                }
                if (files.DeleteFile(name)) count++;
            }
        }
        return count;
    }

    private static FileStream OpenLiveStage(string path, bool allowDelete)
    {
        if (OperatingSystem.IsWindows()) return VaultWindowsStageFile.Open(path, allowDelete);
        if ((File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            throw new IOException("Live stage is not an ordinary file.");
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
    }

    private static void RemoveOwnedLiveStage(string path, WindowsVaultFileIdentity? expected)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.Delete(path);
            return;
        }
        try
        {
            using var file = VaultWindowsStageFile.Open(path, allowDelete: true);
            if (expected.HasValue && WindowsVaultFileIdentity.Read(file.SafeFileHandle) == expected.Value)
                VaultWindowsStageFile.MarkForDeletion(file.SafeFileHandle);
        }
        catch (IOException)
        {
            // Preserve an uninspectable or locked path for authenticated reconciliation.
        }
    }
}
