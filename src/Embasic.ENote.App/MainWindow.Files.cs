using Embasic.ENote.Storage.Documents;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Embasic.ENote.App.ViewModels;
using Embasic.ENote.Storage.Workspaces;
using Microsoft.Win32;

namespace Embasic.ENote.App;

public partial class MainWindow
{
    private readonly WorkspaceFileOperations _fileOperations = new();
    private bool _fileOperationBusy;

    private void UpdateWorkspaceActions()
    {
        if (_viewModel is null || RenameEntryButton is null) return;
        var root = _viewModel.CurrentWorkspace;
        var selected = _viewModel.SelectedNode;
        bool editable = root is not null && selected is not null &&
            selected.Kind is WorkspaceEntryKind.Folder or WorkspaceEntryKind.PlainMarkdown &&
            !string.Equals(root.FullPath, selected.FullPath, StringComparison.OrdinalIgnoreCase);
        RenameEntryButton.IsEnabled = MoveEntryButton.IsEnabled = TrashEntryButton.IsEnabled = editable;
        UndoTrashButton.IsEnabled = root is not null;
        CreateWorkspaceMarkdownButton.IsEnabled = root is not null &&
            WorkspaceTree?.SelectedItem is WorkspaceTreeNode { IsFolder: true };
    }

    private void UpdateDocumentActions()
    {
        bool available = SelectedTab is { IsInTrash: false };
        RevealWorkspaceNoteButton.IsEnabled = available && !_revealingWorkspaceNote;
        ExportMarkdownUdfButton.IsEnabled = ExportDocumentCopyButton.IsEnabled =
            PrintPreviewButton.IsEnabled = available;
    }

    private async void CreateWorkspaceMarkdown_Click(object sender, RoutedEventArgs e)
    {
        if (_fileOperationBusy || _viewModel.CurrentWorkspace is not { } root ||
            WorkspaceTree.SelectedItem is not WorkspaceTreeNode { IsFolder: true } folder) return;

        var dialog = new FolderNameDialog("Klasörde yeni Markdown notu", $"Not adı · {folder.Name}",
            "", "Oluştur") { Owner = this };
        if (dialog.ShowDialog() != true) return;

        string name = dialog.FolderName;
        string[] openPaths = _tabs.Where(tab => !tab.IsInTrash).Select(tab => tab.FilePath).ToArray();
        _fileOperationBusy = true;
        IsEnabled = false;
        bool opened = false;
        Exception? failure = null;
        try
        {
            string created = await Task.Run(() =>
                WorkspaceFileOperations.CreateMarkdown(root, folder.FullPath, name, openPaths));
            opened = OpenMarkdown(created);
            await folder.RefreshAsync();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            failure = error;
        }
        finally
        {
            _fileOperationBusy = false;
            IsEnabled = true;
            if (opened)
                ActiveEditor?.Focus();
        }
        if (failure is not null)
            MessageBox.Show(this, failure.Message, "Not oluşturulamadı", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private async void RenameEntry_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedNode is not { } selected) return;
        var dialog = new FolderNameDialog("Yeniden adlandır", selected.IsFolder ? "Yeni klasör adı" : "Yeni not adı (.md)",
            selected.Name, "Değiştir") { Owner = this };
        if (dialog.ShowDialog() != true) return;
        string name = dialog.FolderName;
        await RunFileChangeAsync((root, tabs) =>
        {
            ValidateOpenSnapshots(tabs, selected.FullPath);
            ValidateNewTabPaths(tabs, selected.FullPath, Path.Combine(Path.GetDirectoryName(selected.FullPath)!, name));
            return _fileOperations.Rename(root, selected.FullPath, name);
        });
    }

    private async void MoveEntry_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedNode is not { } selected) return;
        var dialog = new OpenFolderDialog { Title = "Çalışma alanı içinde hedef klasörü seçin" };
        if (dialog.ShowDialog(this) != true) return;
        string destination = dialog.FolderName;
        await RunFileChangeAsync((root, tabs) =>
        {
            ValidateOpenSnapshots(tabs, selected.FullPath);
            ValidateNewTabPaths(tabs, selected.FullPath, Path.Combine(destination, selected.Name));
            return _fileOperations.Move(root, selected.FullPath, destination);
        });
    }

    private async void TrashEntry_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedNode is not { } selected) return;
        var answer = MessageBox.Show(this,
            $"{selected.Name} silinenlere taşınacak. Kalıcı silme yapılmaz; 'Silmeyi geri al' ile geri getirebilirsiniz. " +
            "Açık notların kaydedilmemiş metni bellekte kalır; geri alınana kadar bu sekmeler salt okunur. " +
            "Bu ilk akış yalnız .md notları ve .md/sayfa yan dosyası içeren klasörleri destekler.",
            "Silinenlere taşı", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.OK) return;
        await RunFileChangeAsync((root, tabs) =>
        {
            ValidateOpenSnapshots(tabs, selected.FullPath);
            return _fileOperations.Trash(root, selected.FullPath);
        });
    }

    private async void UndoTrash_Click(object sender, RoutedEventArgs e)
    {
        await RunFileChangeAsync((root, tabs) =>
        {
            var latest = _fileOperations.LatestTrash(root);
            if (latest is null) return null;
            if (tabs.Any(t => t.TrashId is null && WorkspaceFileOperations.ContainsPath(latest.OriginalPath, t.Path)))
                throw new IOException("Özgün yolda başka bir açık sekme var; geri alma mevcut sekmeyi değiştirmedi.");
            return _fileOperations.Restore(root, latest);
        });
    }

    private async Task RunFileChangeAsync(Func<WorkspaceRoot, TabLocation[], WorkspaceChange?> action)
    {
        if (_fileOperationBusy || _viewModel.CurrentWorkspace is not { } root) return;
        var locations = _tabs.Select(t => new TabLocation(t, t.FilePath, t.Digest, t.LayoutDigest, t.TrashId)).ToArray();
        _fileOperationBusy = true; IsEnabled = false; Mouse.OverrideCursor = Cursors.Wait;
        Exception? failure = null;
        try
        {
            MarkdownStatusText.Text = "Dosya işlemi sürüyor; açık metin otomatik kaydedilmez.";
            var change = await Task.Run(() => action(root, locations));
            if (change is null) { MarkdownStatusText.Text = "Geri alınacak silme yok."; return; }
            foreach (var item in locations)
            {
                if (change.Kind == WorkspaceChangeKind.Restore)
                {
                    if (item.TrashId == change.TrashId) item.Tab.SetTrashState(null);
                }
                else if (item.TrashId is null && WorkspaceFileOperations.ContainsPath(change.Source, item.Path))
                {
                    if (change.Kind == WorkspaceChangeKind.Trash) item.Tab.SetTrashState(change.TrashId);
                    else item.Tab.RelocatePath(WorkspaceFileOperations.MapPath(item.Path, change.Source, change.Target));
                }
            }
            UpdateDocumentActions();
            await _viewModel.RefreshRootAsync();
            MarkdownStatusText.Text = change.Kind switch
            {
                WorkspaceChangeKind.Trash => "Silinenlere taşındı. Açık metin korundu; kaydetmek için silmeyi geri alın.",
                WorkspaceChangeKind.Restore => "Silme geri alındı. Açık metin ve kaydedilmemiş değişiklikler korundu.",
                _ => "Dosya yolu güncellendi. Açık metin ve geri alma geçmişi korundu."
            };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException or System.Text.Json.JsonException)
        { failure = error; }
        finally
        {
            _fileOperationBusy = false; IsEnabled = true; Mouse.OverrideCursor = null;
            UpdateWorkspaceActions(); UpdateDocumentActions();
            if (SelectedTab is { IsInTrash: true }) ShowRevisionState(PlainMarkdownRevisionState.Current);
            else _ = CheckSelectedRevisionAsync();
        }
        if (failure is not null)
            MessageBox.Show(this, "İşlem tamamlanamadı: " + failure.Message, "Dosya işlemi",
                MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void ValidateOpenSnapshots(IEnumerable<TabLocation> tabs, string source)
    {
        foreach (var item in tabs.Where(t => t.TrashId is null && WorkspaceFileOperations.ContainsPath(source, t.Path)))
        {
            if (_noteStore.Read(item.Path).Sha256 != item.Digest || _pageStore.Read(item.Path).Sha256 != item.LayoutDigest)
                throw new IOException("Açık not veya sayfa yan dosyası dışarıdan değişti. Dosya işlemi reddedildi; açık metin korundu.");
        }
    }

    private static void ValidateNewTabPaths(IEnumerable<TabLocation> tabs, string source, string target)
    {
        var open = tabs.Where(t => t.TrashId is null).ToArray();
        foreach (var item in open.Where(t => WorkspaceFileOperations.ContainsPath(source, t.Path)))
        {
            string mapped = WorkspaceFileOperations.MapPath(item.Path, source, target);
            if (open.Any(t => !WorkspaceFileOperations.ContainsPath(source, t.Path) &&
                string.Equals(t.Path, mapped, StringComparison.OrdinalIgnoreCase)))
                throw new IOException("Hedef yol başka bir açık sekmeye ait; işlem reddedildi.");
        }
    }

    private sealed record TabLocation(PlainMarkdownEditorTab Tab, string Path, string Digest,
        string? LayoutDigest, string? TrashId);
}
