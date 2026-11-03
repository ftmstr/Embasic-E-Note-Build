namespace Embasic.ENote.Storage.Workspaces;

/// <summary>
/// Filesystem-facing workspace service. No note contents or cryptographic keys
/// are exposed by this API.
/// </summary>
public interface IWorkspaceStorage
{
    WorkspaceRoot OpenExisting(string selectedFolder);
    Task<IReadOnlyList<WorkspaceEntry>> ListChildrenAsync(
        WorkspaceRoot workspace, string folderPath, CancellationToken cancellationToken = default);
    string CreateFolder(WorkspaceRoot workspace, string parentPath, string folderName);
}

public sealed record WorkspaceRoot(string FullPath);

public sealed record WorkspaceEntry(string Name, string FullPath, WorkspaceEntryKind Kind);

public enum WorkspaceEntryKind
{
    Folder,
    PlainMarkdown,
    EncryptedEmd,
    RichTextRtf,
    WordDocument,
    ReadOnlyDocument,
    OnlineShortcut
}
