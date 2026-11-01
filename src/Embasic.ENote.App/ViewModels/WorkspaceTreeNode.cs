using System.Collections.ObjectModel;
using Embasic.ENote.Storage.Workspaces;

namespace Embasic.ENote.App.ViewModels;

/// <summary>Lazy directory node. Children are fetched only when expanded or refreshed.</summary>
public sealed class WorkspaceTreeNode
{
    private readonly IWorkspaceStorage? _storage;
    private readonly WorkspaceRoot? _workspace;
    private readonly Action<string>? _report;
    private bool _loaded;
    private bool _loading;

    public string Name { get; }
    public string FullPath { get; }
    public WorkspaceEntryKind? Kind { get; }
    public bool IsFolder => Kind == WorkspaceEntryKind.Folder;
    public bool IsPlaceholder => Kind is null;
    public string Symbol => Kind switch
    {
        WorkspaceEntryKind.Folder => "📁",
        WorkspaceEntryKind.PlainMarkdown => "📝",
        WorkspaceEntryKind.EncryptedEmd => "🔒",
        _ => "…"
    };
    public ObservableCollection<WorkspaceTreeNode> Children { get; } = new();

    public WorkspaceTreeNode(
        string name, string fullPath, WorkspaceEntryKind kind,
        IWorkspaceStorage storage, WorkspaceRoot workspace, Action<string> report)
    {
        Name = name;
        FullPath = fullPath;
        Kind = kind;
        _storage = storage;
        _workspace = workspace;
        _report = report;
        if (IsFolder) Children.Add(Placeholder());
    }

    private WorkspaceTreeNode()
    {
        Name = "Yüklemek için genişletin…";
        FullPath = "";
        Kind = null;
    }

    private static WorkspaceTreeNode Placeholder() => new();

    public async Task RefreshAsync()
    {
        if (!IsFolder || _storage is null || _workspace is null || _loading) return;
        _loading = true;
        try
        {
            var results = await _storage.ListChildrenAsync(_workspace, FullPath);
            Children.Clear();
            foreach (var entry in results)
            {
                Children.Add(new WorkspaceTreeNode(
                    entry.Name, entry.FullPath, entry.Kind,
                    _storage, _workspace, _report!));
            }

            _loaded = true;
            _report?.Invoke($"{Name}: {results.Count} öğe listelendi.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _loaded = false;
            Children.Clear();
            Children.Add(Placeholder());
            _report?.Invoke($"Klasör okunamadı ({Name}): {error.Message}");
        }
        finally
        {
            _loading = false;
        }
    }

    public Task ExpandAsync() => _loaded ? Task.CompletedTask : RefreshAsync();
}
