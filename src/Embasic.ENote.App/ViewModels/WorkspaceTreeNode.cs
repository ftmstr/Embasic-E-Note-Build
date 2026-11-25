using System.IO;
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
    private Task<string?>? _refreshTask;

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
        WorkspaceEntryKind.RichTextRtf => "📃",
        WorkspaceEntryKind.WordDocument => "📄",
        WorkspaceEntryKind.ReadOnlyDocument => "📑",
        WorkspaceEntryKind.OnlineShortcut => "🔗",
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
        if (!IsFolder || _storage is null || _workspace is null) return;
        string? error = await RefreshChildrenAsync(CancellationToken.None);
        _report?.Invoke(error is null ? $"{Name}: {Children.Count} öğe listelendi." : error);
    }

    internal async Task<string?> RefreshChildrenAsync(CancellationToken cancellationToken)
    {
        while (_refreshTask is { IsCompleted: false } previous)
        {
            try { await previous.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        }
        cancellationToken.ThrowIfCancellationRequested();
        _refreshTask = ReadChildrenAsync(cancellationToken);
        return await _refreshTask;
    }

    private async Task<string?> ReadChildrenAsync(CancellationToken cancellationToken)
    {
        try
        {
            var entries = await _storage!.ListChildrenAsync(_workspace!, FullPath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            ApplyChildren(entries);
            _loaded = true;
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Keep the last readable listing until a successful refresh can reconcile it.
            return $"Klasör okunamadı ({Name}): {error.Message}";
        }
    }

    private void ApplyChildren(IReadOnlyList<WorkspaceEntry> entries)
    {
        var wanted = entries.ToDictionary(entry => entry.FullPath, StringComparer.Ordinal);
        for (int i = Children.Count - 1; i >= 0; i--)
        {
            var child = Children[i];
            if (!wanted.TryGetValue(child.FullPath, out var entry) || child.Kind != entry.Kind)
                Children.RemoveAt(i);
        }

        var retained = Children.ToDictionary(child => child.FullPath, StringComparer.Ordinal);
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (!retained.TryGetValue(entry.FullPath, out var child))
            {
                Children.Insert(i, new WorkspaceTreeNode(entry.Name, entry.FullPath, entry.Kind,
                    _storage!, _workspace!, _report!));
            }
            else if (!ReferenceEquals(Children[i], child))
            {
                Children.Move(Children.IndexOf(child), i);
            }
        }
    }

    public async Task<string?> RefreshLoadedBranchesAsync(CancellationToken cancellationToken)
    {
        string? firstError = null;
        var pending = new Stack<WorkspaceTreeNode>();
        pending.Push(this);
        while (pending.TryPop(out var folder))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!folder._loaded && folder._refreshTask is null) continue;
            string? error = await folder.RefreshChildrenAsync(cancellationToken);
            if (error is not null)
            {
                firstError ??= error;
                continue;
            }
            foreach (var child in folder.Children)
                if (child.IsFolder) pending.Push(child);
        }
        return firstError;
    }

    public Task ExpandAsync() => _loaded ? Task.CompletedTask : RefreshAsync();
}
