using System.Security.Cryptography;
using Embasic.ENote.Core.Printing;
using Embasic.ENote.Storage.Workspaces;

namespace Embasic.ENote.Storage.Documents;

public enum PlainMarkdownRevisionState { Current, Changed, Missing, Unavailable }

public static class PlainMarkdownRevision
{
    public static PlainMarkdownRevisionState Check(string path, string noteDigest, string? layoutDigest)
    {
        try
        {
            string sidecar = DocumentPageSettingsStore.Sidecar(path);
            string? note = Hash(path);
            if (note is null) return PlainMarkdownRevisionState.Missing;
            string? layout = Hash(sidecar);
            return note == noteDigest && layout == layoutDigest
                ? PlainMarkdownRevisionState.Current : PlainMarkdownRevisionState.Changed;
        }
        catch (DirectoryNotFoundException) { return PlainMarkdownRevisionState.Missing; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return PlainMarkdownRevisionState.Unavailable;
        }
    }

    private static string? Hash(string path)
    {
        DocumentPageSettingsStore.CheckPlainFile(path);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                65536, FileOptions.SequentialScan);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (FileNotFoundException) { return null; }
    }

    public static void WriteDraftCopy(string source, string destination, string text,
        PageLayoutSettings layout, bool writeBom)
    {
        // The original may have disappeared, including its parent directory.
        string target = PlainMarkdownDocumentStore.ValidatePath(destination);
        if (string.Equals(Path.GetFullPath(source), target, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Taslak için özgün nottan farklı bir yol seçin.");

        string targetLayout = DocumentPageSettingsStore.Sidecar(target);
        DocumentPageSettingsStore.CheckPlainFile(target);
        DocumentPageSettingsStore.CheckPlainFile(targetLayout);
        if (File.Exists(target) || File.Exists(targetLayout))
            throw new IOException("Hedef not veya sayfa yan dosyası zaten var.");

        // Stage both files beside the chosen destination before publishing either.
        // Use a sibling staging directory so publication is a same-volume move.
        string directory = Path.Combine(Path.GetDirectoryName(target)!, ".enote-draft-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string draft = Path.Combine(directory, "draft.md");
            new PlainMarkdownDocumentStore().Save(draft, text, null, writeBom);
            new DocumentPageSettingsStore().Save(draft, layout, null);
            WorkspaceFileOperations.MoveCore(draft, target, directory: false);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
