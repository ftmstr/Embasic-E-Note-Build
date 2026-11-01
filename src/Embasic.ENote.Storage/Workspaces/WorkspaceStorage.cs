using Embasic.ENote.Core.Notes;

namespace Embasic.ENote.Storage.Workspaces;

/// <summary>
/// Enumerates one directory level at a time. Never traverses directory links or
/// reads file contents. This is a workspace navigator, not a security sandbox.
/// </summary>
public sealed class WorkspaceStorage : IWorkspaceStorage
{
    public WorkspaceRoot OpenExisting(string selectedFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedFolder);
        var path = Normalize(selectedFolder);
        AssertRealDirectory(path);
        return new WorkspaceRoot(path);
    }

    public Task<IReadOnlyList<WorkspaceEntry>> ListChildrenAsync(
        WorkspaceRoot workspace, string folderPath, CancellationToken cancellationToken = default) =>
        Task.Run(() => ListChildren(workspace, folderPath, cancellationToken), cancellationToken);

    private static IReadOnlyList<WorkspaceEntry> ListChildren(
        WorkspaceRoot workspace, string folderPath, CancellationToken cancellationToken)
    {
        var directory = CheckDirectory(workspace, folderPath);
        var entries = new List<WorkspaceEntry>();

        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(path);
            }
            catch (FileNotFoundException)
            {
                continue; // Removed by another process while enumerating.
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                continue; // Never follow symbolic links or directory junctions.
            }

            var name = Path.GetFileName(path);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                entries.Add(new WorkspaceEntry(name, path, WorkspaceEntryKind.Folder));
                continue;
            }

            var format = NoteFilePolicy.Detect(path);
            if (format == NoteFileFormat.PlainMarkdown)
            {
                entries.Add(new WorkspaceEntry(name, path, WorkspaceEntryKind.PlainMarkdown));
            }
            else if (format == NoteFileFormat.EncryptedEmd)
            {
                entries.Add(new WorkspaceEntry(name, path, WorkspaceEntryKind.EncryptedEmd));
            }
        }

        entries.Sort(static (left, right) =>
        {
            var byKind = (left.Kind == WorkspaceEntryKind.Folder ? 0 : 1)
                .CompareTo(right.Kind == WorkspaceEntryKind.Folder ? 0 : 1);
            return byKind != 0 ? byKind : StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
        });
        return entries;
    }

    public string CreateFolder(WorkspaceRoot workspace, string parentPath, string folderName)
    {
        var parent = CheckDirectory(workspace, parentPath);
        ValidateName(folderName);
        var target = Normalize(Path.Combine(parent, folderName));
        EnsureContained(workspace, target);
        if (File.Exists(target) || Directory.Exists(target))
        {
            throw new IOException("A file or folder already exists with this name.");
        }

        // Directory.CreateDirectory is recursive, but the parent must already
        // exist inside the selected workspace (verified above).
        Directory.CreateDirectory(target);
        return target;
    }

    private static string CheckDirectory(WorkspaceRoot workspace, string folderPath)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        var normalized = Normalize(folderPath);
        EnsureContained(workspace, normalized);
        AssertRealDirectory(workspace.FullPath);

        // Reject links at each existing component, including junctions inside
        // the workspace. This does not defend against hostile filesystem races.
        var relative = Path.GetRelativePath(workspace.FullPath, normalized);
        var cursor = workspace.FullPath;
        if (relative != ".")
        {
            foreach (var segment in relative.Split(Path.DirectorySeparatorChar))
            {
                cursor = Path.Combine(cursor, segment);
                AssertRealDirectory(cursor);
            }
        }

        return normalized;
    }

    private static void AssertRealDirectory(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.Directory) == 0 ||
            (attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new UnauthorizedAccessException("Linked or non-directory paths are not supported as workspace folders.");
        }
    }

    private static void EnsureContained(WorkspaceRoot workspace, string candidate)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var relative = Path.GetRelativePath(workspace.FullPath, candidate);
        if (Path.IsPathRooted(relative) || relative == ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("The selected path is outside the workspace.");
        }
    }

    private static string Normalize(string value) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));

    private static void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name != name.Trim() || name is "." or ".." ||
            name.EndsWith('.') || name.EndsWith(' ') ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            name.Contains('/') || name.Contains('\\') || name.Contains(':'))
        {
            throw new ArgumentException("Invalid Windows folder name.", nameof(name));
        }

        var stem = name.Split('.')[0].ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" ||
            stem is "COM1" or "COM2" or "COM3" or "COM4" or "COM5" or "COM6" or "COM7" or "COM8" or "COM9" ||
            stem is "LPT1" or "LPT2" or "LPT3" or "LPT4" or "LPT5" or "LPT6" or "LPT7" or "LPT8" or "LPT9")
        {
            throw new ArgumentException("Reserved Windows device name.", nameof(name));
        }
    }
}
